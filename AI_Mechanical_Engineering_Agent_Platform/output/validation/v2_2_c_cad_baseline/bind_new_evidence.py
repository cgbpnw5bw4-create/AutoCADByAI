"""仅将生产常量绑定到本轮已采集、已独立验收的新证据；不编辑任何报告。"""
import hashlib
import json
import re
import sys
import argparse
from pathlib import Path

out = Path(__file__).resolve().parent
root = out.parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("manifest")
parser.add_argument("--revisions", default="revisions.json")
parser.add_argument("--topology-acceptance", default="topology_acceptance.json")
parser.add_argument("--profile-acceptance", required=True)
parser.add_argument("--evidence-date", required=True)
args = parser.parse_args()
revisions = json.loads((out / args.revisions).read_text(encoding="utf-8-sig"))
acceptance = json.loads((out / args.topology_acceptance).read_text(encoding="utf-8-sig"))
assert acceptance["status"] == "Passed"
profile_acceptance = json.loads((out / args.profile_acceptance).read_text(encoding="utf-8-sig"))
assert profile_acceptance["status"] == "Passed" and len(profile_acceptance["checks"]) == 4
assert len(acceptance["checks"]) == 7
accepted_probes = {}
for check in acceptance["checks"] + profile_acceptance["checks"]:
    assert check["passed"]
    entry = check["probe"]
    data = Path(entry["path"]).read_bytes()
    assert len(data) == entry["size_bytes"]
    assert hashlib.sha256(data).hexdigest() == entry["sha256"]
    accepted_probes[entry["path"]] = entry["sha256"]
manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8-sig"))
assert manifest["status"] == "CandidatesCollected"
assert manifest["source_revision"] == revisions["feature"]
items = {Path(x["input"]["path"]).name: x for x in manifest["items"]}
mapping = {
    "Sketch/SketchHandler.cs": "feature_pipeline_plate.json",
    "Extrude/ExtrudeBossHandler.cs": "feature_pipeline_plate.json",
    "Cut/ExtrudeCutHandler.cs": "feature_pipeline_plate.json",
    "Hole/HoleHandler.cs": "feature_pipeline_plate.json",
    "Fillet/FilletHandler.cs": "feature_pipeline_plate_fillet.json",
    "Chamfer/ChamferHandler.cs": "feature_pipeline_plate_chamfer.json",
    "Pattern/LinearPatternHandler.cs": "feature_pipeline_plate_linear_pattern.json",
    "Pattern/CircularPatternHandler.cs": "feature_pipeline_plate_circular_pattern.json",
    "Mirror/MirrorHandler.cs": "feature_pipeline_plate_mirror.json",
}
assert set(items) == set(mapping.values()) | {"parameter_update_plate.json"}
for item in items.values():
    assert item["probe_artifacts_stable"] and item["candidate_report_stable"]
    assert item["step_geometry_match"]["passed"]
    assert accepted_probes[item["probe"]["path"]] == item["probe"]["sha256"]
    for entry in [item["report"], item["probe"], item["step_probe"], *item["artifacts_after_probe"].values()]:
        data = Path(entry["path"]).read_bytes()
        assert len(data) == entry["size_bytes"]
        assert hashlib.sha256(data).hexdigest() == entry["sha256"]

feature_dir = root / "src/Workers/SolidWorks/Features"
changes = []
for relative, name in mapping.items():
    path = feature_dir / relative
    source = path.read_bytes().decode("utf-8")
    report_path = Path(items[name]["report"]["path"]).relative_to(root).as_posix()
    assert report_path.startswith("evidence/solidworks/v2_2_c_refresh/")
    values = {"EvidenceId": "v2.2-c-" + args.evidence_date + "-refresh-" + path.stem,
              "DiagnosticRunPath": report_path, "SourceRevision": revisions["feature"],
              "SolidWorksVersion": manifest["solidworks_version"]}
    for key, value in values.items():
        source, count = re.subn(r'(' + key + r':\s*)"[^"]*"', lambda m: m[1] + json.dumps(value), source)
        assert count == 1, (path, key, count)
    changes.append((path, source))

path = feature_dir / "V20DThreeCircleCutEvidencePolicy.cs"
source = path.read_bytes().decode("utf-8")
sample_digest = hashlib.sha256((root / "examples/parameter_update_plate.json").read_bytes()).hexdigest().upper()
assert re.search(r'SampleInputSha256\s*=\s*"([^"]*)"', source)[1] == sample_digest
item = items["parameter_update_plate.json"]
values = {"EvidenceId": "v2.2-c-" + args.evidence_date + "-three-circle-parameter-rebuild",
          "CandidateDiagnosticRelativePath": Path(item["report"]["path"]).relative_to(root).as_posix(),
          "CandidateReportSha256": item["report"]["sha256"].upper(),
          "SourceRevision": revisions["v20d"]}
for key, value in values.items():
    source, count = re.subn(r'(const string ' + key + r'\s*=\s*)"[^"]*"', lambda m: m[1] + json.dumps(value), source)
    assert count == 1, (path, key, count)
changes.append((path, source))

for path, source in changes:
    path.write_bytes(source.encode("utf-8"))
    print(path.relative_to(root))
