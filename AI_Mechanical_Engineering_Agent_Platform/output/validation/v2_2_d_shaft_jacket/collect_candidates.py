"""串行采集既有 CAD 候选与只读拓扑，失败保留并停止；不更改任何生产证据绑定。

运行前须构建解决方案并确认真实 CAD 环境。此脚本会启动真实 SolidWorks。
示例：python -X utf8 collect_candidates.py --build-log implementation_build.log --inputs feature_pipeline_plate.json
--build-log 必须指向本验证目录内已成功完成、晚于绑定源码和运行程序集的构建日志。
完整采集省略 --inputs；--output-root 仅接受 evidence/solidworks 下以 v2_2_d_ 开头的直接子目录。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import uuid


INPUT_NAMES = (
    "feature_pipeline_plate.json",
    "feature_pipeline_plate_fillet.json",
    "feature_pipeline_plate_chamfer.json",
    "feature_pipeline_plate_linear_pattern.json",
    "feature_pipeline_plate_circular_pattern.json",
    "feature_pipeline_plate_mirror.json",
    "parameter_update_plate.json",
)
VALIDATION_ROOT = Path(__file__).resolve().parent


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def timestamp() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def project_root() -> Path:
    for path in VALIDATION_ROOT.parents:
        if (path / "AI_Mechanical_Engineering_Agent_Platform.sln").is_file():
            return path
    raise RuntimeError("无法从脚本位置定位解决方案。")


def read_json(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    require(isinstance(value, dict), f"JSON 根必须是对象：{path}")
    return value


def fingerprint(path: Path) -> dict:
    before = path.stat()
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    after = path.stat()
    require((before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns),
            f"计算指纹期间文件发生变化：{path}")
    return {"path": str(path), "size_bytes": after.st_size, "sha256": digest.hexdigest()}


def write_manifest(path: Path, manifest: dict) -> None:
    manifest["updated_at"] = timestamp()
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def bound_source_paths(root: Path) -> list[Path]:
    """解析现有策略的文件清单，逐文件记录原始 SHA256；不复制任何 revision 算法。"""
    paths = set()
    policies = sorted((root / "src/Workers/SolidWorks/Features").glob("*EvidencePolicy.cs"))
    require(bool(policies), "没有找到证据策略源码。")
    for policy in policies:
        source = policy.read_text(encoding="utf-8-sig")
        match = re.search(r"BoundSourcePaths\s*=\s*\[(.*?)\];", source, re.S)
        if match is None and policy.name == "GeometryValidationEvidencePolicy.cs":
            match = re.search(r"var\s+paths\s*=\s*new\[\]\s*\{(.*?)\};", source, re.S)
        if match is None and policy.name == "PartFamilyProductionEvidencePolicy.cs":
            paths.add(policy.resolve())
            continue
        require(match is not None, f"无法解析策略文件清单，停止以免漏记绑定：{policy}")
        constants = dict(re.findall(r'const\s+string\s+(\w+)\s*=\s*"([^"\r\n]*)"\s*;', source))
        tokens = re.sub(r"//[^\r\n]*", "", match.group(1)).split(",")
        paths.add(policy.resolve())
        for token in tokens:
            token = token.strip()
            if not token:
                continue
            relative = json.loads(token) if token.startswith('"') else constants.get(token)
            require(isinstance(relative, str), f"无法解析策略文件项：{policy.name}: {token}")
            path = (root / relative).resolve()
            require(path.is_relative_to(root) and path.is_file(), f"绑定源码缺失或越界：{path}")
            paths.add(path)
    # Probe 不在生产证据策略中，但重导入验收必须绑定本次实际构建的工具源码。
    for relative in ("tools/SolidWorksEdgeProbe/Program.cs",
                     "tools/SolidWorksEdgeProbe/SolidWorksEdgeProbe.csproj"):
        path = (root / relative).resolve()
        require(path.is_relative_to(root) and path.is_file(), f"探针源码缺失或越界：{path}")
        paths.add(path)
    return sorted(paths)


def frozen_snapshot(root: Path, assemblies: dict[str, Path], build_log: Path) -> dict:
    return {
        "bound_sources": [fingerprint(path) for path in bound_source_paths(root)],
        "runner_binaries": [fingerprint(path) for path in sorted({
            binary.resolve() for assembly in assemblies.values() for binary in assembly.parent.rglob("*.dll")
        })],
        "build_log": fingerprint(build_log),
        "collector_script": fingerprint(Path(__file__).resolve()),
    }


def require_current_build(root: Path, assemblies: dict[str, Path], build_log: Path) -> dict:
    require(build_log.is_relative_to(VALIDATION_ROOT) and build_log.is_file(),
            "--build-log 必须指向本阶段 validation 目录内已经完成的构建日志。")
    content = build_log.read_text(encoding="utf-8-sig", errors="replace")
    require(("已成功生成" in content or "Build succeeded" in content)
            and re.search(r"(?im)^\s*0\s*(?:个错误|error\(s\))\s*$", content) is not None,
            "构建日志缺少成功及 0 错误记录，未开始 CAD 采集。")
    snapshot = frozen_snapshot(root, assemblies, build_log)
    build_time = build_log.stat().st_mtime_ns
    newer = [item["path"] for key in ("bound_sources", "runner_binaries") for item in snapshot[key]
             if Path(item["path"]).stat().st_mtime_ns > build_time]
    require(not newer, f"构建日志早于当前源码或二进制，须完成新构建后采集：{newer}")
    return snapshot


def execution_environment(root: Path) -> tuple[dict, dict]:
    environment = os.environ.copy()
    config_path = root / "config" / "solidworks.local.json"
    config = read_json(config_path) if config_path.is_file() else {}
    mapping = {
        "template_part_path": "SW_TEMPLATE_PART_PATH",
        "template_drawing_path": "SW_TEMPLATE_DRAWING_PATH",
        "connect_timeout_seconds": "SW_CONNECT_TIMEOUT_SECONDS",
        "execution_timeout_seconds": "SW_EXECUTION_TIMEOUT_SECONDS",
        "visible": "SW_VISIBLE",
    }
    for field, key in mapping.items():
        if not environment.get(key) and field in config:
            value = config[field]
            environment[key] = str(value).lower() if isinstance(value, bool) else str(value)
    environment["SW_FEATURE_EXECUTION_SMOKE_TEST"] = "true"
    environment.setdefault("SW_VISIBLE", "true")
    enabled = lambda key: environment.get(key, "").lower() in {"true", "1", "yes"}
    blockers = [key for key in (
        "SW_DISABLE_REAL_EXECUTION", "SW_FORCE_FAKE_WORKER", "CI", "TF_BUILD",
        "GITHUB_ACTIONS", "GITLAB_CI", "SW_UNIT_TEST_MODE", "DOTNET_RUNNING_IN_TEST",
    ) if enabled(key)]
    blockers += [key for key in ("JENKINS_URL", "TEAMCITY_VERSION", "BUILD_BUILDID")
                 if environment.get(key, "").strip()]
    require(not blockers, f"真实执行已被环境禁止，未覆盖这些开关：{', '.join(blockers)}")
    template = Path(environment.get("SW_TEMPLATE_PART_PATH", ""))
    if not template.is_absolute():
        template = root / template
    require(template.is_file(), f"缺少有效 SW_TEMPLATE_PART_PATH：{template}")
    environment["SW_TEMPLATE_PART_PATH"] = str(template.resolve())
    return environment, {"config_path": str(config_path), "cad_environment": {
        key: environment[key] for key in (*mapping.values(), "SW_FEATURE_EXECUTION_SMOKE_TEST")
        if key in environment
    }}


def run_process(command: list[str], root: Path, environment: dict, log_path: Path,
                timeout: int, record: dict, checkpoint) -> None:
    record.update(command=command, log_path=str(log_path), started_at=timestamp(), timeout_seconds=timeout)
    checkpoint()
    print(f"执行：{subprocess.list2cmdline(command)}", flush=True)
    with log_path.open("wb") as log:
        process = subprocess.Popen(command, cwd=root, env=environment, stdout=log,
                                   stderr=subprocess.STDOUT,
                                   creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        try:
            record["exit_code"] = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            # 只结束本脚本启动的诊断进程；不杀 SolidWorks，不自动重放有副作用的操作。
            process.kill()
            record["exit_code"] = process.wait()
            record["timed_out"] = True
            raise RuntimeError(f"诊断超时，已停止继续采集；须检查 CAD 会话及当次产物：{log_path}")
        except BaseException:
            if process.poll() is None:
                process.kill()
                record["exit_code"] = process.wait()
            record["interrupted"] = True
            raise
        finally:
            record["finished_at"] = timestamp()
            checkpoint()


def validate_candidate(report_path: Path, input_path: Path, item: dict) -> tuple[dict, dict]:
    report = read_json(report_path)
    item["report"] = fingerprint(report_path)
    require(report.get("final_status") == "CandidatePassed", f"候选未通过：{report.get('final_status')}")
    for key, expected in {
        "candidate_only": True, "dedicated_smoke_flag_enabled": True,
        "real_execution_allowed": True, "solid_works_connected": True, "real_cad_executed": True,
        "main_workflow_accepted": False, "quality_gate_passed": False,
    }.items():
        require(report.get(key) is expected, f"候选标志不满足要求：{key}")
    require(report.get("deliverable_status") == "NotDeliverable", "诊断不能声称可交付。")
    require(not report.get("failure_stage") and report.get("issues") == [], "候选含失败阶段或问题。")
    require(Path(report.get("input_path", "")).resolve() == input_path, "诊断输入路径与本次原样例不符。")
    version = report.get("solid_works_version")
    require(isinstance(version, str) and bool(version.strip()), "候选缺少实际 SolidWorks 版本。")
    features = report.get("feature_handler_reports")
    require(isinstance(features, list) and len(features) > 0, "候选缺少逐特征报告。")
    source = read_json(input_path)
    model_spec = source.get("cad_model_spec", source)
    expected_ids = [value["sketch_id"] for value in model_spec.get("sketches", [])]
    expected_ids += [value["feature_id"] for value in model_spec.get("features", [])]
    if model_spec.get("model_type") in {"shaft_basic", "jacket_basic"}:
        expected_ids = [o["parameters"].get("feature_id", o["parameters"].get("sketch_id", o["operation_id"]))
                        for o in report["build_plan"]["operations"] if o["operation_type"] not in {"SavePart", "ExportStep"}]
    actual_ids = [feature.get("feature_id") for feature in features if isinstance(feature, dict)]
    require(bool(expected_ids) and (model_spec.get("model_type") in {"shaft_basic", "jacket_basic"} or len(expected_ids) == len(set(expected_ids)))
            and sorted(actual_ids) == sorted(expected_ids), "逐特征报告与原输入的完整特征集合不符。")
    revisions = set()
    for feature in features:
        require(isinstance(feature, dict), "逐特征报告格式无效。")
        for flag in ("result_object_validated", "rebuild_passed", "geometry_change_validated"):
            require(feature.get(flag) is True, f"特征 {feature.get('feature_id')} 未通过 {flag}")
        require(not feature.get("failure_stage") and feature.get("issues") == [], "逐特征报告含失败。")
        require(feature.get("api_evidence_status") == "diagnostic_candidate", "候选状态被错误提升。")
        require(feature.get("evidence_solid_works_version") == version, "特征版本与诊断版本不一致。")
        revision = feature.get("evidence_source_revision")
        require(isinstance(revision, str) and revision.startswith("feature-execution-source-sha256:"),
                "特征缺少受绑定源码指纹。")
        revisions.add(revision)
    require(len(revisions) == 1, "同次特征源码指纹不一致。")
    item["source_revision"] = next(iter(revisions))
    item["solidworks_version"] = version
    item["feature_checks"] = features
    snapshots = {}
    for key, name in (("model", "model.SLDPRT"), ("step", "model.STEP")):
        artifact = report.get(key, {})
        path = report_path.parent / name
        require(artifact.get("exists") is True and artifact.get("status") == "Passed", f"{key} 未通过。")
        require(Path(artifact.get("file_path", "")).resolve() == path.resolve(), f"{key} 未指向同次目录。")
        current = fingerprint(path)
        snapshots[key] = current
        item["artifacts_before_probe"] = snapshots
        require(current["size_bytes"] > 0 and current["size_bytes"] == artifact.get("size_bytes"),
                f"{key} 实际长度与新报告不一致：实际 {current['size_bytes']}，报告 {artifact.get('size_bytes')}")
    step_bytes = Path(snapshots["step"]["path"]).read_bytes()
    require(step_bytes.removeprefix(b"\xef\xbb\xbf").lstrip(b"\t\n\f\r ").startswith(b"ISO-10303-21;")
            and b"ENDSEC;" in step_bytes and b"END-ISO-10303-21;" in step_bytes,
            "STEP 缺少物理交换文件标志。")
    return report, snapshots


def validate_probe(probe: dict, input_path: Path) -> dict:
    require(Path(probe.get("input", "")).resolve() == input_path.resolve(), "探针输入不属于本次候选。")
    require(probe.get("read_success") is True and not probe.get("failure_stage"),
            f"探针未成功读取真实几何：{probe.get('failure_stage')}")
    geometry = probe.get("geometry")
    require(isinstance(geometry, dict), "探针缺少实测 geometry。")
    require(geometry.get("RebuildPassed") is True and geometry.get("ReadIssues") == [],
            "探针重建未通过或实测 ReadIssues 非空。")
    require(isinstance(geometry.get("ExactExtents"), dict), "探针缺少真实 ExactExtents。")
    edges = probe.get("edges")
    require(isinstance(edges, list) and len(edges) > 0 and probe.get("edge_count") == len(edges)
            and geometry.get("Edges") == edges, "探针没有一致、完整的非空边数据。")
    return geometry


def compare_geometry(native: dict, step: dict) -> dict:
    """比较实测圆柱轴线与孔口；仅允许由原生圆角内外圈证明的环面分割缝。"""
    coordinate_tolerance = 0.001

    def number(value, label: str) -> float:
        require(type(value) in (int, float) and math.isfinite(value), f"缺少有限实测值：{label}")
        return float(value)

    body_counts = [geometry.get("BodyCount") for geometry in (native, step)]
    require(all(type(value) is int and value > 0 for value in body_counts), "实体数量必须为实测正整数。")
    volumes = [number(geometry.get("VolumeCubicMillimeters"), "VolumeCubicMillimeters")
               for geometry in (native, step)]
    require(all(value > 0 for value in volumes), "实测体积必须为正。")
    volume_tolerance = max(0.001, abs(volumes[0]) * 0.000001)
    volume_delta = abs(volumes[1] - volumes[0])
    extents = []
    for name in ("MinXmm", "MinYmm", "MinZmm", "MaxXmm", "MaxYmm", "MaxZmm"):
        values = []
        for geometry in (native, step):
            box = geometry.get("ExactExtents")
            require(isinstance(box, dict), "缺少实测 ExactExtents。")
            values.append(number(box.get(name), name))
        delta = abs(values[1] - values[0])
        extents.append({"coordinate": name, "native_mm": values[0], "step_mm": values[1],
                        "absolute_difference_mm": delta, "matched": delta <= coordinate_tolerance})

    direction_tolerance = 0.000001

    def dot(left, right) -> float:
        return sum(a * b for a, b in zip(left, right))

    def subtract(left, right) -> tuple:
        return tuple(a - b for a, b in zip(left, right))

    def unit(values) -> tuple:
        length = math.sqrt(dot(values, values))
        require(length > 0 and math.isfinite(length), "实测轴或圆边法向不能为零。")
        values = tuple(value / length for value in values)
        # 最大分量确定无向轴符号，避免近零首分量的正负残差造成错判。
        sign = 1 if values[max(range(3), key=lambda index: abs(values[index]))] >= 0 else -1
        return tuple(sign * value for value in values)

    def near(left, right, tolerance=coordinate_tolerance) -> bool:
        return len(left) == len(right) and all(abs(a - b) <= tolerance for a, b in zip(left, right))

    def direction_near(left, right) -> bool:
        return near(left, right, direction_tolerance) or near(left, tuple(-value for value in right), direction_tolerance)

    def unique(values, equivalent) -> list:
        result = []
        for value in values:
            if not any(equivalent(value, existing) for existing in result):
                result.append(value)
        return result

    def cylinder_axes(geometry: dict) -> list[dict]:
        cylinders = geometry.get("Cylinders")
        require(isinstance(cylinders, list), "缺少完整实测 Cylinders，不能只凭直径证明同轴。")
        axes = []
        for cylinder in cylinders:
            require(isinstance(cylinder, dict), "实测圆柱必须是对象。")
            diameter = number(cylinder.get("DiameterMm"), "DiameterMm")
            require(diameter > 0, "圆柱直径必须为正。")
            origin = tuple(number(cylinder.get(key), key) for key in ("OriginXmm", "OriginYmm", "OriginZmm"))
            axis = unit(tuple(number(cylinder.get(key), key) for key in ("AxisX", "AxisY", "AxisZ")))
            foot = subtract(origin, tuple(dot(origin, axis) * value for value in axis))
            axes.append({"diameter_mm": diameter, "axis": axis, "perpendicular_origin_mm": foot})
        return axes

    def same_cylinder(left: dict, right: dict) -> bool:
        return (abs(left["diameter_mm"] - right["diameter_mm"]) <= coordinate_tolerance
                and direction_near(left["axis"], right["axis"])
                and near(left["perpendicular_origin_mm"], right["perpendicular_origin_mm"]))

    native_axes, step_axes = cylinder_axes(native), cylinder_axes(step)
    missing_axes = unique([value for value in native_axes if not any(same_cylinder(value, other) for other in step_axes)], same_cylinder)
    extra_axes = unique([value for value in step_axes if not any(same_cylinder(value, other) for other in native_axes)], same_cylinder)

    def circles(geometry: dict) -> list[dict]:
        edges = geometry.get("Edges")
        require(isinstance(edges, list), "缺少实测 Edges。")
        values = []
        for edge in edges:
            require(isinstance(edge, dict), "实测边必须是对象。")
            if edge.get("Kind") != "circle":
                continue
            point = tuple(number(edge.get(key), key)
                          for key in ("AnchorXMm", "AnchorYMm", "AnchorZMm", "RadiusMm"))
            require(point[3] > 0, "圆边半径必须为正。")
            direction = edge.get("Direction")
            require(isinstance(direction, dict), "圆边缺少实测法向。")
            normal = unit(tuple(number(direction.get(key), key) for key in ("X", "Y", "Z")))
            values.append({"point_radius_mm": point, "normal": normal, "edge": edge})
        return values

    def same_circle(left: dict, right: dict) -> bool:
        return near(left["point_radius_mm"], right["point_radius_mm"]) and direction_near(left["normal"], right["normal"])

    def coaxial(circle: dict, cylinder: dict) -> bool:
        center = circle["point_radius_mm"][:3]
        axis = cylinder["axis"]
        foot = subtract(center, tuple(dot(center, axis) * value for value in axis))
        return direction_near(circle["normal"], axis) and near(foot, cylinder["perpendicular_origin_mm"])

    native_circles, step_circles = circles(native), circles(step)
    native_mouths = [value for value in native_circles if any(coaxial(value, axis) for axis in native_axes)]
    step_mouths = [value for value in step_circles if any(coaxial(value, axis) for axis in step_axes)]
    # 仅从原生 Fillet 的同轴内外圆及实测圆柱推导环面，不从待验 STEP 自证。
    tori = []
    if "Fillet" in (native.get("FeatureTypes") or []):
        for cylinder in unique(native_axes, same_cylinder):
            related = unique([value for value in native_mouths if coaxial(value, cylinder)], same_circle)
            for inner in related:
                if (sorted(inner["edge"].get("AdjacentSurfaceKinds", [])) != ["cylinder", "other"]
                        or abs(inner["point_radius_mm"][3] - cylinder["diameter_mm"] / 2) > coordinate_tolerance):
                    continue
                for outer in related:
                    if sorted(outer["edge"].get("AdjacentSurfaceKinds", [])) != ["other", "plane"]:
                        continue
                    minor = outer["point_radius_mm"][3] - inner["point_radius_mm"][3]
                    axial_gap = abs(dot(subtract(outer["point_radius_mm"][:3], inner["point_radius_mm"][:3]), cylinder["axis"]))
                    if minor > coordinate_tolerance and abs(minor - axial_gap) <= coordinate_tolerance:
                        tori.append({"center_mm": inner["point_radius_mm"][:3], "axis": cylinder["axis"],
                                     "major_radius_mm": outer["point_radius_mm"][3], "minor_radius_mm": minor,
                                     "native_inner_edge": inner["edge"], "native_outer_edge": outer["edge"]})

    def toroidal_seam(circle: dict) -> dict | None:
        if sorted(circle["edge"].get("AdjacentSurfaceKinds", [])) != ["other", "other"]:
            return None
        for torus in tori:
            radial = subtract(circle["point_radius_mm"][:3], torus["center_mm"])
            length = math.sqrt(dot(radial, radial))
            if (abs(dot(radial, torus["axis"])) > coordinate_tolerance
                    or abs(length - torus["major_radius_mm"]) > coordinate_tolerance
                    or abs(circle["point_radius_mm"][3] - torus["minor_radius_mm"]) > coordinate_tolerance):
                continue
            axis = torus["axis"]
            tangent = (axis[1] * radial[2] - axis[2] * radial[1], axis[2] * radial[0] - axis[0] * radial[2],
                       axis[0] * radial[1] - axis[1] * radial[0])
            if direction_near(circle["normal"], unit(tangent)):
                return torus
        return None

    missing = unique([value for value in native_circles if not any(same_circle(value, other) for other in step_circles)], same_circle)
    extra_circles = [value for value in step_circles if not any(same_circle(value, other) for other in native_circles)]
    unexpected = unique([value for value in extra_circles
                         if any(coaxial(value, axis) for axis in step_axes) or toroidal_seam(value) is None], same_circle)
    missing_mouths = unique([value for value in native_mouths if not any(same_circle(value, other) for other in step_mouths)], same_circle)
    extra_mouths = unique([value for value in step_mouths if not any(same_circle(value, other) for other in native_mouths)], same_circle)
    result = {
        "body_count": {"native": body_counts[0], "step": body_counts[1], "matched": body_counts[0] == body_counts[1]},
        "volume": {"native_mm3": volumes[0], "step_mm3": volumes[1], "absolute_difference_mm3": volume_delta,
                   "tolerance_mm3": volume_tolerance, "matched": volume_delta <= volume_tolerance},
        "exact_extents": {"tolerance_mm": coordinate_tolerance, "coordinates": extents,
                          "matched": all(value["matched"] for value in extents)},
        "cylinders": {"tolerance_mm": coordinate_tolerance, "direction_tolerance": direction_tolerance,
                      "native_unique": unique(native_axes, same_cylinder), "step_unique": unique(step_axes, same_cylinder),
                      "missing_in_step": missing_axes, "unexpected_in_step": extra_axes,
                      "matched": not missing_axes and not extra_axes},
        "circles": {"tolerance_mm": coordinate_tolerance, "direction_tolerance": direction_tolerance,
                    "native_edge_count": len(native_circles), "step_edge_count": len(step_circles),
                    "native_unique": unique(native_circles, same_circle), "step_unique": unique(step_circles, same_circle),
                    "native_coaxial_mouths": unique(native_mouths, same_circle), "step_coaxial_mouths": unique(step_mouths, same_circle),
                    "native_excluded_edges": [value["edge"] for value in native_circles if value not in native_mouths],
                    "step_excluded_edges": [{"edge": value["edge"], "native_torus_basis": toroidal_seam(value),
                                             "matches_native_circle": any(same_circle(value, other) for other in native_circles)}
                                            for value in step_circles if value not in step_mouths],
                    "native_inferred_tori": tori, "missing_mouths_in_step": missing_mouths,
                    "unexpected_mouths_in_step": extra_mouths,
                    "missing_in_step": missing, "unexpected_in_step": unexpected,
                    "matched": not missing and not unexpected and not missing_mouths and not extra_mouths},
    }
    result["passed"] = all(value["matched"] for value in result.values())
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inputs", nargs="+", default=list(INPUT_NAMES), help="项目内已有 JSON 输入；文件名按 examples/ 解析，默认七个既有样例。")
    parser.add_argument("--output-root", default="evidence/solidworks/v2_2_d_refresh")
    parser.add_argument("--build-log", required=True, help="本阶段已完成且晚于绑定源码及 DLL 的成功构建日志。")
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Debug")
    parser.add_argument("--smoke-timeout-seconds", type=int, default=600)
    parser.add_argument("--probe-timeout-seconds", type=int, default=180)
    args = parser.parse_args()
    root = project_root()
    collection = VALIDATION_ROOT / (time.strftime("collection-%Y%m%d_%H%M%S") + "-" + uuid.uuid4().hex[:8])
    collection.mkdir(parents=True, exist_ok=False)
    manifest_path = collection / "manifest.json"
    manifest = {"schema_version": "v2.2-d-candidate-collection-2", "started_at": timestamp(),
                "status": "Running", "candidate_only": True, "production_bindings_modified": False,
                "independent_engineering_acceptance": "Pending", "project_root": str(root), "items": []}
    checkpoint = lambda: write_manifest(manifest_path, manifest)
    checkpoint()
    print(f"验证清单：{manifest_path}", flush=True)
    lock_fd = None
    lock_path = None
    try:
        require(os.name == "nt", "真实诊断仅支持 Windows。")
        require(1 <= args.smoke_timeout_seconds <= 7200 and 1 <= args.probe_timeout_seconds <= 7200,
                "进程超时必须在 1 至 7200 秒之间。")
        inputs = []
        for requested in args.inputs:
            path = Path(requested)
            if not path.is_absolute():
                path = root / (Path("examples") / path if len(path.parts) == 1 else path)
            path = path.resolve()
            require(path.is_relative_to(root) and path.suffix.lower() == ".json",
                    f"仅接受项目内 JSON 输入，禁止项目外路径：{path}")
            require(path.is_file() and path not in inputs, f"输入不存在或重复：{path}")
            read_json(path)  # 只解析 JSON 数据，不解释或执行输入内容中的代码。
            inputs.append(path)
        output_root = Path(args.output_root)
        output_root = (root / output_root).resolve() if not output_root.is_absolute() else output_root.resolve()
        require(output_root.parent == (root / "evidence" / "solidworks").resolve()
                and output_root.name.startswith("v2_2_d_"),
                "--output-root 只能是 evidence/solidworks/ 下名称以 v2_2_d_ 开头的直接子目录。")
        output_root.mkdir(parents=True, exist_ok=True)
        manifest["output_root"] = str(output_root)
        lock_path = output_root / ".candidate_collection.lock"
        lock_fd = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        os.write(lock_fd, f"pid={os.getpid()} manifest={manifest_path}".encode("utf-8"))
        environment, config_record = execution_environment(root)
        manifest.update(config_record)
        dotnet = shutil.which("dotnet")
        require(dotnet is not None, "找不到 dotnet。")
        assemblies = {name: root / "tools" / name / "bin" / args.configuration / "net10.0" / f"{name}.dll"
                      for name in ("SolidWorksFeatureExecutionSmokeRunner", "SolidWorksEdgeProbe")}
        for name, assembly in assemblies.items():
            require(assembly.is_file(), f"请先构建诊断工具：{assembly}")
        build_log = Path(args.build_log)
        build_log = (root / build_log).resolve() if not build_log.is_absolute() else build_log.resolve()
        baseline = require_current_build(root, assemblies, build_log)
        manifest["frozen_baseline"] = baseline
        manifest["collector_script"] = baseline["collector_script"]
        manifest["runner_binaries"] = baseline["runner_binaries"]
        manifest["bound_sources"] = baseline["bound_sources"]
        manifest["freeze_checks"] = []

        def check_frozen(stage: str) -> None:
            current = frozen_snapshot(root, assemblies, build_log)
            stable = current == baseline
            record = {"stage": stage, "at": timestamp(), "stable": stable}
            if not stable:
                record["observed"] = current
            manifest["freeze_checks"].append(record)
            checkpoint()
            require(stable, f"绑定源码、诊断 DLL、构建日志或采集脚本发生变化：{stage}；保留本次失败，禁止继续采集。")

        checkpoint()
        for index, input_path in enumerate(inputs, 1):
            item = {"input": fingerprint(input_path), "status": "Running"}
            manifest["items"].append(item)
            checkpoint()
            check_frozen(f"{index:02d}-before-candidate")
            before = set(output_root.glob("*/feature_execution_report.json"))
            smoke_record = item.setdefault("smoke_process", {})
            try:
                run_process([dotnet, "exec", str(assemblies["SolidWorksFeatureExecutionSmokeRunner"]),
                             "--input", str(input_path), "--output", str(output_root)],
                            root, environment, collection / f"{index:02d}-smoke.log",
                            args.smoke_timeout_seconds, smoke_record, checkpoint)
            finally:
                item["new_reports"] = [str(path) for path in sorted(set(output_root.glob("*/feature_execution_report.json")) - before)]
                checkpoint()
                check_frozen(f"{index:02d}-after-candidate")
            require(smoke_record["exit_code"] == 0, f"候选进程失败：{smoke_record['exit_code']}")
            require(len(item["new_reports"]) == 1, "本次候选未产生唯一新报告，停止防止混用并行或历史产物。")
            report_path = Path(item["new_reports"][0])
            report, snapshots = validate_candidate(report_path, input_path, item)
            require(fingerprint(input_path) == item["input"], "诊断期间原输入发生变化。")
            require(item["source_revision"] == manifest.setdefault("source_revision", item["source_revision"]),
                    "批次期间执行源码指纹发生变化，必须冻结源码后重新采集。")
            require(item["solidworks_version"] == manifest.setdefault("solidworks_version", item["solidworks_version"]),
                    "批次期间 SolidWorks 版本变化。")
            geometries = {}
            stable_files = {**snapshots, "report": item["report"]}
            for probe_key, artifact_key, suffix in (("probe", "model", "probe"), ("step_probe", "step", "step-probe")):
                probe_path = collection / f"{index:02d}-{suffix}.json"
                probe_record = item.setdefault(f"{probe_key}_process", {})
                check_frozen(f"{index:02d}-before-{suffix}")
                require(all(fingerprint(Path(value["path"])) == value for value in stable_files.values()),
                        f"{suffix} 前模型、STEP 或已有报告发生变化。")
                try:
                    run_process([dotnet, "exec", str(assemblies["SolidWorksEdgeProbe"]),
                                 "--input", snapshots[artifact_key]["path"], "--confirm-real-cad", "--json", str(probe_path)]
                                + (["--hidden"] if environment.get("SW_VISIBLE", "true").lower() not in {"true", "1", "yes"} else []),
                                root, environment, collection / f"{index:02d}-{suffix}.log",
                                args.probe_timeout_seconds, probe_record, checkpoint)
                finally:
                    after = {key: fingerprint(Path(value["path"])) for key, value in stable_files.items()}
                    item[f"artifacts_after_{probe_key}"] = after
                    item[f"{probe_key}_artifacts_stable"] = after == stable_files
                    item["candidate_report_stable"] = after["report"] == item["report"]
                    if probe_path.is_file():
                        item[probe_key] = fingerprint(probe_path)
                    checkpoint()
                    check_frozen(f"{index:02d}-after-{suffix}")
                require(probe_record["exit_code"] == 0, f"{suffix} 只读探针失败：{probe_record['exit_code']}")
                require(item[f"{probe_key}_artifacts_stable"], f"{suffix} 前后模型、STEP 或已有报告发生变化。")
                probe = read_json(probe_path)
                geometries[artifact_key] = validate_probe(probe, Path(snapshots[artifact_key]["path"]))
                item[f"{probe_key}_read_success"] = True
                item["edge_count" if artifact_key == "model" else "step_edge_count"] = probe["edge_count"]
                stable_files[probe_key] = item[probe_key]
                checkpoint()
            item["step_geometry_match"] = compare_geometry(geometries["model"], geometries["step"])
            checkpoint()
            require(item["step_geometry_match"]["passed"], "STEP 重导入几何与原生模型不匹配，详见 step_geometry_match。")
            require(all(fingerprint(Path(value["path"])) == value for value in stable_files.values()),
                    "几何比较期间模型、STEP 或探针报告发生变化。")
            item["status"] = "CandidateCollected"
            checkpoint()
            print(f"候选已通过字节稳定性及 STEP 重导入几何一致性检查：{input_path.name}；工程独立复核仍待完成。", flush=True)
        check_frozen("collection-completed")
        manifest["status"] = "CandidatesCollected"
        return 0
    except (Exception, KeyboardInterrupt) as exception:
        manifest["status"] = "Failed"
        manifest["failure"] = {"type": type(exception).__name__, "message": str(exception), "at": timestamp()}
        if manifest["items"] and manifest["items"][-1]["status"] == "Running":
            manifest["items"][-1]["status"] = "Failed"
        print(f"采集停止，保留失败与产物：{exception}\n清单：{manifest_path}", file=sys.stderr, flush=True)
        return 2
    finally:
        if lock_fd is not None:
            os.close(lock_fd)
            lock_path.unlink()
        manifest["finished_at"] = timestamp()
        checkpoint()


if __name__ == "__main__":
    raise SystemExit(main())
