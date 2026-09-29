"""串行运行正式主流程，并对发布包原件和唯一字节副本做独立重开验收。"""
import json
import argparse
import subprocess
import uuid
from pathlib import Path
import collect_candidates as c

base = Path(__file__).resolve().parent
root = c.project_root()
environment, configuration = c.execution_environment(root)
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", required=True)
args = parser.parse_args()
output = (base / args.output).resolve()
c.require(output.is_relative_to(base), "主流程验收目录不能越界。")
output.mkdir(exist_ok=False)
manifest_path = output / "verification.json"
manifest = {"status": "Running", "configuration": configuration, "cases": []}
tracked = c.bound_source_paths(root) + [root / "src/Interfaces/CliHost/Program.cs"]
tracked += sorted((root / "src/Interfaces/CliHost/bin/Debug/net10.0").glob("*.dll"))
tracked += sorted((root / "tools/SolidWorksEdgeProbe/bin/Debug/net10.0").glob("*.dll"))
manifest["execution_files_before"] = [c.fingerprint(path) for path in tracked]
snapshot = lambda: c.write_manifest(manifest_path, manifest)
family_manifest = c.read_json(root / "evidence/solidworks/v2_2_d_final/family_acceptance.json")
family_cases = {Path(case["input"]["path"]).name: case for case in family_manifest["cases"]}
inputs = ["real_cad_shaft_request.json", "real_cad_shaft_plain_request.json", "real_cad_jacket_request.json", "parameter_update_plate.json"]
try:
    for name in inputs:
        case_dir = output / Path(name).stem
        case_dir.mkdir()
        item = {"input": c.fingerprint(root / "examples" / name), "status": "Running"}
        manifest["cases"].append(item)
        log = case_dir / "main.log"
        c.run_process(["dotnet", str(root / "src/Interfaces/CliHost/bin/Debug/net10.0/CliHost.dll"),
                       "run-cad-workflow", "--input", item["input"]["path"]],
                      root, environment, log, 600, item.setdefault("process", {}), snapshot)
        c.require(item["process"]["exit_code"] == 0, f"正式主流程失败：{name}")
        paths = [line.removeprefix("Output directory: ").strip() for line in log.read_text(encoding="utf-8-sig").splitlines()
                 if line.startswith("Output directory: ")]
        c.require(len(paths) == 1, "不能唯一定位正式发布目录。")
        release = Path(paths[0])
        item["release_directory"] = str(release)
        report = c.read_json(release / "release_manifest.json")
        for label, path in (("release", release / "release_manifest.json"), ("quality", release / "reports/package_quality_report.json"),
                            ("e2e", release / "reports/e2e_execution_report.json")):
            data = c.read_json(path)
            c.require(data["final_status"] == "Passed" and data["deliverable_status"] == "Deliverable", f"{label} 未通过。")
        files = {}
        for record in report["artifacts"] + report["reports"]:
            for key in ("source_path", "package_path"):
                value = c.fingerprint(Path(record[key]))
                c.require(value["size_bytes"] == record["size_bytes"] and value["sha256"].upper() == record["sha256"].upper(), "发布清单与物理文件不一致。")
                files[value["path"]] = value
        for path in (release / "release_manifest.json", release / "reports/package_quality_report.json", release / "reports/e2e_execution_report.json"):
            files[str(path)] = c.fingerprint(path)
        item["files_before"] = files
        probes, geometries = {}, {}
        item["probe_input_copies"] = {}
        for kind, suffix in (("native", ".SLDPRT"), ("step", ".STEP")):
            source = list((release / "artifacts").glob("*" + suffix))
            c.require(len(source) == 1, "发布包存在不唯一的 CAD 文件。")
            source = source[0]
            target = case_dir / (kind + "_" + uuid.uuid4().hex + suffix)
            with target.open("xb") as stream: stream.write(source.read_bytes())
            copy = c.fingerprint(target)
            c.require(copy["sha256"] == files[str(source)]["sha256"] and copy["size_bytes"] == files[str(source)]["size_bytes"], "唯一副本与发布件不同。")
            item["probe_input_copies"][kind] = {"source": files[str(source)], "copy": copy}
            probes[kind] = case_dir / (kind + "_probe.json")
            c.run_process(["dotnet", str(root / "tools/SolidWorksEdgeProbe/bin/Debug/net10.0/SolidWorksEdgeProbe.dll"),
                           "--input", str(target), "--confirm-real-cad", "--json", str(probes[kind])],
                          root, environment, case_dir / (kind + "_probe.log"), 300, item.setdefault(kind + "_process", {}), snapshot)
            c.require(item[kind + "_process"]["exit_code"] == 0 and c.fingerprint(target) == copy, "重新打开失败或改变副本字节。")
            geometries[kind] = c.validate_probe(c.read_json(probes[kind]), target)
            c.require(geometries[kind]["SolidWorksVersion"] == "31.5.0", "重开版本不是当前受证版本。")
        item["step_geometry_match"] = c.compare_geometry(geometries["native"], geometries["step"])
        c.require(item["step_geometry_match"]["passed"], "两格式独立实测不一致。")
        if name in family_cases:
            acceptance = case_dir / "independent_geometry_acceptance.json"
            with acceptance.open("xb") as stream:
                result = subprocess.run(["dotnet", str(base / "EvidenceAudit/bin/Debug/net10.0/EvidenceAudit.dll"), "geometry",
                                         family_cases[name]["diagnostic_report"]["path"], str(probes["native"]), str(probes["step"])],
                                        cwd=root, stdout=stream, stderr=subprocess.STDOUT, timeout=60, creationflags=subprocess.CREATE_NO_WINDOW)
            c.require(result.returncode == 0, "发布包不符合从输入推导的独立截面/体积要求。")
            item["engineering_acceptance"] = c.fingerprint(acceptance)
        else:
            # 旧参数更新的固定四孔期望，独立检查两份实测值。
            import math
            expected = [(x, y, z, 5.) for x in (-80., 80.) for y in (0., 15.) for z in (-30., 30.)]
            for geometry in geometries.values():
                circles = [e for e in geometry["Edges"] if e["Kind"] == "circle"]
                actual = [(e["AnchorXMm"], e["AnchorYMm"], e["AnchorZMm"], e["RadiusMm"]) for e in circles]
                # STEP 的相邻面会重复枚举同一个圆边；按几何位置匹配，既不能缺口，也不能新增未知圆口。
                c.require(all(any(math.dist(a, p) < .001 for a in actual) for p in expected)
                          and all(sum(math.dist(a, p) < .001 for p in expected) == 1 for a in actual), "参数更新发布件四孔落点不匹配。")
                c.require(all(abs(e["LengthMm"] - 10 * math.pi) < .001
                              and set(e["AdjacentSurfaceKinds"]) == {"plane", "cylinder"}
                              and abs(abs(e["Direction"]["Y"]) - 1) < .000001 for e in circles), "参数更新圆口的周长、邻接面或轴向不符。")
                c.require(abs(geometry["VolumeCubicMillimeters"] - (200 * 100 * 15 - 4 * math.pi * 25 * 15)) < .01, "参数更新发布件独立理论体积不符。")
                extents = {"MinXmm": -100., "MaxXmm": 100., "MinYmm": 0., "MaxYmm": 15., "MinZmm": -50., "MaxZmm": 50.}
                c.require(all(abs(geometry["ExactExtents"][key] - value) < .001 for key, value in extents.items()), "参数更新发布件包络不匹配。")
        item["probes"] = {key: c.fingerprint(path) for key, path in probes.items()}
        item["files_after"] = {path: c.fingerprint(Path(path)) for path in files}
        c.require(item["files_after"] == files and c.fingerprint(Path(item["input"]["path"])) == item["input"], "重开改变了正式文件、源报告或输入。")
        item["status"] = "Passed"
        snapshot()
        print(name, "Passed / Deliverable / 两格式独立验收通过", flush=True)
    manifest["execution_files_after"] = [c.fingerprint(path) for path in tracked]
    c.require(manifest["execution_files_before"] == manifest["execution_files_after"], "正式验收期间源码或程序集发生变化。")
    manifest["status"] = "Passed"
except Exception as error:
    manifest["status"] = "Failed"
    manifest["failure"] = str(error)
    raise
finally:
    snapshot()
