"""绑定本轮不可变新证据，不改写历史报告、哈希或长度；指纹仍由原生产算法计算。"""
import json
import re
import sys
from pathlib import Path
import collect_candidates as c

base = Path(__file__).resolve().parent
root = c.project_root()
revisions = c.read_json(base / "revisions.json")
manifests = [c.read_json(Path(arg)) for arg in sys.argv[1:]]
c.require(len(manifests) == 3 and all(m["status"] == "CandidatesCollected" for m in manifests), "须提供完整3个已完成采集批次。")
items = [item for m in manifests for item in m["items"]]
c.require(len(items) == 14, "本次须含两类三样例、既有七样例及四方向。")
for manifest in manifests:
    c.require(manifest["source_revision"] == revisions["feature"], "批次源码修订不一致。")
    c.require(all(check["stable"] for check in manifest["freeze_checks"]), "采集中冻结检查失败。")
for item in items:
    c.require(item["step_geometry_match"]["passed"] and item["probe_artifacts_stable"] and item["step_probe_artifacts_stable"], "两格式几何或物理稳定性未通过。")
    for entry in [item["input"], item["report"], item["probe"], item["step_probe"], *item["artifacts_before_probe"].values()]:
        c.require(c.fingerprint(Path(entry["path"])) == entry, "新证据文件发生变化。")
for filename, count in (("topology_acceptance.json", 7), ("profile_topology_acceptance.json", 4)):
    check = c.read_json(base / filename)
    c.require(check["status"] == "Passed" and len(check["checks"]) == count, "既有能力独立工程验收不完整。")
    for row in check["checks"]:
        c.require(row["passed"] and c.fingerprint(Path(row["probe"]["path"])) == row["probe"], "工程验收探针已变化。")
by_name = {Path(item["input"]["path"]).name: item for item in items}
family_cases = []
for name, acceptance_name in (("real_cad_shaft_request.json", "shaft_stepped_acceptance.json"),
                               ("real_cad_shaft_plain_request.json", "shaft_plain_acceptance.json"),
                               ("real_cad_jacket_request.json", "jacket_acceptance.json")):
    item = by_name[name]
    check = c.read_json(base / acceptance_name)
    c.require(len(check["results"]) == 2 and all(row["validation"]["is_valid"] for row in check["results"]), "族截面复算未通过。")
    c.require({row["probe_path"] for row in check["results"]} == {item["probe"]["path"], item["step_probe"]["path"]}, "验收不是当前两份探针。")
    report = c.read_json(Path(item["report"]["path"]))
    family_cases.append({"part_type": report["build_plan"]["part_type"], "plan_sha256": check["plan_sha256"],
                         "input": item["input"], "diagnostic_report": item["report"],
                         "native": item["artifacts_before_probe"]["model"], "step": item["artifacts_before_probe"]["step"],
                         "native_probe": item["probe"], "step_probe": item["step_probe"],
                         "engineering_acceptance": c.fingerprint(base / acceptance_name)})
family_path = root / "evidence/solidworks/v2_2_d_refresh/family_acceptance.json"
with family_path.open("x", encoding="utf-8") as stream:
    json.dump({"schema_version": "v2.2-d-independent-family-acceptance-1", "source_revision": revisions["feature"],
               "solid_works_version": "31.5.0", "candidate_only": True, "cases": family_cases}, stream, ensure_ascii=False, indent=2)

changes = []
feature_dir = root / "src/Workers/SolidWorks/Features"
mapping = {"Sketch/SketchHandler.cs": "feature_pipeline_plate.json", "Extrude/ExtrudeBossHandler.cs": "feature_pipeline_plate.json",
           "Cut/ExtrudeCutHandler.cs": "feature_pipeline_plate.json", "Hole/HoleHandler.cs": "feature_pipeline_plate.json",
           "Fillet/FilletHandler.cs": "feature_pipeline_plate_fillet.json", "Chamfer/ChamferHandler.cs": "feature_pipeline_plate_chamfer.json",
           "Pattern/LinearPatternHandler.cs": "feature_pipeline_plate_linear_pattern.json", "Pattern/CircularPatternHandler.cs": "feature_pipeline_plate_circular_pattern.json",
           "Mirror/MirrorHandler.cs": "feature_pipeline_plate_mirror.json", "Revolve/RevolveBossHandler.cs": "real_cad_shaft_request.json"}

def update_metadata(source, item, label):
    values = {"EvidenceId": "v2.2-d-20260928-" + label,
              "DiagnosticRunPath": Path(item["report"]["path"]).relative_to(root).as_posix(),
              "SourceRevision": revisions["feature"], "SolidWorksVersion": "31.5.0"}
    for key, value in values.items():
        source, count = re.subn(r'(' + key + r':\s*)"[^"\r\n]*"', lambda m: m[1] + json.dumps(value), source)
        c.require(count == 1, f"绑定槽位不唯一：{label}/{key}/{count}")
    return source

for relative, name in mapping.items():
    path = feature_dir / relative
    source = path.read_bytes().decode("utf-8")
    if relative == "Sketch/SketchHandler.cs":
        first, rest = source.split("    public FeatureApiEvidence RightPlaneApiEvidence", 1)
        source = update_metadata(first, by_name[name], "SketchHandler") + "    public FeatureApiEvidence RightPlaneApiEvidence" + update_metadata(rest, by_name["real_cad_shaft_request.json"], "RightPlaneSketch")
    else:
        source = update_metadata(source, by_name[name], path.stem)
    changes.append((path, source))

for filename, values in (
    ("V20DThreeCircleCutEvidencePolicy.cs", {"EvidenceId": "v2.2-d-20260928-three-circle-parameter-rebuild",
        "CandidateDiagnosticRelativePath": Path(by_name["parameter_update_plate.json"]["report"]["path"]).relative_to(root).as_posix(),
        "CandidateReportSha256": by_name["parameter_update_plate.json"]["report"]["sha256"].upper(), "SourceRevision": revisions["v20d"]}),
    ("PartFamilyProductionEvidencePolicy.cs", {"EvidenceId": "sha256:" + c.fingerprint(family_path)["sha256"],
        "SourceRevision": revisions["feature"]})):
    path = feature_dir / filename
    source = path.read_bytes().decode("utf-8")
    for key, value in values.items():
        source, count = re.subn(r'(const string ' + key + r'\s*=\s*)"[^"\r\n]*"', lambda m: m[1] + json.dumps(value), source)
        c.require(count == 1, f"绑定常量不唯一：{filename}/{key}")
    changes.append((path, source))
for path, source in changes:
    path.write_bytes(source.encode("utf-8"))
print("已绑定新证据；须重新计算原算法指纹确认语义源码未变化，并重新build/test/self-check。")
