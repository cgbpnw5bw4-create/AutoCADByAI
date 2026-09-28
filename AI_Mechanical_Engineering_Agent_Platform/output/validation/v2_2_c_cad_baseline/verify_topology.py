"""依据只读探针的实际孔口坐标验收阵列，预期值由几何公式独立计算。"""
import json
import math
import sys
import argparse
from pathlib import Path

checks = []
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("manifests", nargs="+")
parser.add_argument("--output", default="topology_acceptance.json")
args = parser.parse_args()
for arg in args.manifests:
    manifest_path = Path(arg).resolve()
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    if manifest["status"] != "CandidatesCollected":
        raise RuntimeError(f"候选批次未通过：{manifest_path}")
    for item in manifest["items"]:
        name = Path(item["input"]["path"]).stem
        if "linear_pattern" in name or "linear_opposite" in name:
            expected = [(-30., 0.), (-10., 0.), (10., 0.), (30., 0.)]
        elif "linear_negative" in name:
            expected = [(30., 0.), (15., 0.), (0., 0.)]
        elif "circular_pattern" in name:
            expected = [(20., 0.), (0., -20.), (-20., 0.), (0., 20.)]
        elif "circular_positive_quarter" in name or "circular_negative_quarter" in name:
            sign = -1. if "positive" in name else 1.
            expected = [(20., 0.), (math.sqrt(200), sign * math.sqrt(200)), (0., sign * 20.)]
        elif "mirror" in name:
            expected = [(-25., -15.), (25., -15.)]
        else:
            continue
        probe = json.loads(Path(item["probe"]["path"]).read_text(encoding="utf-8-sig"))
        surfaces = {}
        passed = True
        for y in (0., 10.):
            actual = [(edge["AnchorXMm"], edge["AnchorZMm"]) for edge in probe["edges"]
                      if edge["Kind"] == "circle" and abs(edge["RadiusMm"] - 3.) < 1e-5
                      and abs(edge["AnchorYMm"] - y) < 1e-5]
            remaining = list(actual)
            for point in expected:
                match = next((i for i, value in enumerate(remaining) if math.dist(value, point) < .001), None)
                if match is None:
                    passed = False
                else:
                    remaining.pop(match)
            passed = passed and len(actual) == len(expected) and not remaining
            surfaces[str(y)] = actual
        checks.append({"input": name, "probe": item["probe"], "expected_xz_mm": expected,
                       "actual_by_y_mm": surfaces, "passed": passed})

result = {"status": "Passed" if checks and all(x["passed"] for x in checks) else "Failed", "checks": checks}
output = Path(__file__).parent / args.output
if output.exists():
    raise RuntimeError("拒绝覆盖已有拓扑验收，请保留旧结果后采用新的输出目录。")
output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, ensure_ascii=False, indent=2))
raise SystemExit(0 if result["status"] == "Passed" else 2)
