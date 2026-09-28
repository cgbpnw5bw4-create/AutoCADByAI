"""独立核对基础孔、圆角过渡、倒角及更新后四孔的实测截面。"""
import json
import math
import sys
import argparse
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("manifest")
parser.add_argument("--output", default="profile_topology_acceptance.json")
args = parser.parse_args()
manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8-sig"))
expected = {
    "feature_pipeline_plate.json": [(x,y,0,5) for x in (-20,20) for y in (0,10)],
    "feature_pipeline_plate_fillet.json": [(x,y,0,r) for x in (-20,20) for y,r in ((0,5),(9,5),(10,6))],
    "feature_pipeline_plate_chamfer.json": [(x,y,0,r) for x in (-20,20) for y,r in ((0,6),(1,5),(10,5))],
    "parameter_update_plate.json": [(x,y,z,5) for x in (-80,80) for y in (0,15) for z in (-30,30)],
}
checks = []
for item in manifest["items"]:
    name = Path(item["input"]["path"]).name
    if name not in expected:
        continue
    probe = json.loads(Path(item["probe"]["path"]).read_text(encoding="utf-8-sig"))
    actual = [(e["AnchorXMm"],e["AnchorYMm"],e["AnchorZMm"],e["RadiusMm"])
              for e in probe["edges"] if e["Kind"] == "circle"]
    matched = len(actual) == len(expected[name]) and all(
        sum(math.dist(point, measured) < .001 for measured in actual) == 1 for point in expected[name])
    checks.append({"input":name, "probe":item["probe"], "expected_xyz_radius_mm":expected[name],
                   "actual_xyz_radius_mm":actual, "passed":matched})
result = {"status":"Passed" if len(checks)==4 and all(c["passed"] for c in checks) else "Failed", "checks":checks}
target = Path(__file__).parent / args.output
with target.open("x", encoding="utf-8") as stream:
    json.dump(result, stream, ensure_ascii=False, indent=2)
print(result["status"])
raise SystemExit(0 if result["status"] == "Passed" else 2)
