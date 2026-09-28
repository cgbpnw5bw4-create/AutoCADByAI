"""只读重开本轮参数更新发布包，两格式与独立四孔期望均须通过；不修改源报告。"""
import argparse
import json
import math
from pathlib import Path
import subprocess
import sys
import uuid
import collect_candidates as checks

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("release")
parser.add_argument("--output", required=True)
args = parser.parse_args()
base = Path(__file__).resolve().parent
root = checks.project_root()
release = Path(args.release).resolve()
output = (base / args.output).resolve()
checks.require(output.is_relative_to(base) and not output.exists(), "拒绝覆盖或越界输出。")
output.mkdir()
result = {"status":"Running", "release":str(release), "probes":{}, "checks":[]}
try:
    manifest = checks.read_json(release / "release_manifest.json")
    quality = checks.read_json(release / "reports/package_quality_report.json")
    e2e = checks.read_json(release / "reports/e2e_execution_report.json")
    for label, report in (("manifest",manifest), ("quality",quality), ("e2e",e2e)):
        checks.require(report["final_status"] == "Passed", f"{label}未通过。")
        checks.require(report["deliverable_status"] == "Deliverable", f"{label}不可交付。")
    snapshots = {}
    for item in manifest["artifacts"] + manifest["reports"]:
        for key in ("source_path", "package_path"):
            snapshot = checks.fingerprint(Path(item[key]))
            checks.require(snapshot["size_bytes"] == item["size_bytes"] and
                           snapshot["sha256"].upper() == item["sha256"].upper(), f"发布清单物理失配：{item[key]}")
            snapshots[snapshot["path"]] = snapshot
    for path in (release / "release_manifest.json", release / "reports/package_quality_report.json",
                 release / "reports/e2e_execution_report.json"):
        snapshots[str(path)] = checks.fingerprint(path)
    result["files_before"] = snapshots
    models = {"native":release / "artifacts/plate_basic_4holes.SLDPRT", "step":release / "artifacts/plate_basic_4holes.STEP"}
    geometries = {}
    result["probe_input_copies"] = {}
    for kind, source_path in models.items():
        # 原会话可能已有同名旧文档。只读核验使用逐字节一致的唯一名称副本，不关闭未知文档。
        path = output / f"{kind}_{uuid.uuid4().hex}{source_path.suffix}"
        with path.open("xb") as stream:
            stream.write(source_path.read_bytes())
        snapshot = checks.fingerprint(path)
        checks.require(snapshot["sha256"] == snapshots[str(source_path)]["sha256"] and
                       snapshot["size_bytes"] == snapshots[str(source_path)]["size_bytes"], "只读副本与发布件不一致。")
        result["probe_input_copies"][kind] = {"source":snapshots[str(source_path)], "copy":snapshot}
        target = output / f"{kind}_probe.json"
        with (output / f"{kind}_probe.log").open("x", encoding="utf-8") as log:
            process = subprocess.run(["dotnet", "exec", str(root / "tools/SolidWorksEdgeProbe/bin/Debug/net10.0/SolidWorksEdgeProbe.dll"),
                                      "--input", str(path), "--confirm-real-cad", "--json", str(target)],
                                     cwd=root, stdout=log, stderr=subprocess.STDOUT, timeout=300)
        checks.require(process.returncode == 0, f"{kind}重新打开失败。")
        probe = checks.read_json(target)
        geometries[kind] = checks.validate_probe(probe, path)
        checks.require(checks.fingerprint(path) == snapshot, "探针改动了只读副本。")
        result["probes"][kind] = checks.fingerprint(target)
    result["step_geometry_match"] = checks.compare_geometry(geometries["native"], geometries["step"])
    checks.require(result["step_geometry_match"]["passed"], "发布包两格式几何不一致。")
    expected = [(x,y,z,5.) for x in (-80.,80.) for y in (0.,15.) for z in (-30.,30.)]
    actual = [(e["AnchorXMm"],e["AnchorYMm"],e["AnchorZMm"],e["RadiusMm"]) for e in geometries["native"]["Edges"] if e["Kind"] == "circle"]
    checks.require(len(actual) == len(expected) and all(sum(math.dist(p,a) < .001 for a in actual) == 1 for p in expected), "四孔独立期望落点不一致。")
    extents = geometries["native"]["ExactExtents"]
    expected_extents = {"MinXmm":-100.,"MaxXmm":100.,"MinYmm":0.,"MaxYmm":15.,"MinZmm":-50.,"MaxZmm":50.}
    checks.require(all(abs(extents[k]-v) < .001 for k,v in expected_extents.items()), "200×100×15尺寸不符。")
    result["expected_hole_mouth_xyz_radius_mm"] = expected
    result["expected_extents_mm"] = expected_extents
    result["files_after"] = {path:checks.fingerprint(Path(path)) for path in snapshots}
    checks.require(result["files_after"] == snapshots, "重开过程改动了发布文件或源文件。")
    result["status"] = "Passed"
except Exception as error:
    result["status"] = "Failed"
    result["failure"] = str(error)
finally:
    with (output / "verification.json").open("x", encoding="utf-8") as stream:
        json.dump(result, stream, ensure_ascii=False, indent=2)
print(result["status"], result.get("failure", ""))
raise SystemExit(0 if result["status"] == "Passed" else 2)
