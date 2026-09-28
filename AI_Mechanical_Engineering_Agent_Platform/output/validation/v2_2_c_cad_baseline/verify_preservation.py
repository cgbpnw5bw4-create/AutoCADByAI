"""只读核验历史保护范围及旧采集清单指纹，输出新的验证记录。"""
import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", required=True)
args = parser.parse_args()
base = Path(__file__).resolve().parent
protected = json.loads((base / "protected_before.json").read_text(encoding="utf-8-sig"))
checks = []
def check(path, size, digest, group):
    path = Path(path)
    data = path.read_bytes() if path.is_file() else b""
    checks.append({"path":str(path), "group":group, "passed":path.is_file() and
                   len(data) == size and hashlib.sha256(data).hexdigest().upper() == digest.upper()})
for item in protected:
    check(item["Path"], item["Length"], item["Hash"], "历史保护范围")
for name in ("collection-20260909_154101-818998b2", "collection-20260909_154158-17372a2e"):
    manifest = json.loads((base / name / "manifest.json").read_text(encoding="utf-8-sig"))
    for item in manifest["items"]:
        for entry in (item["input"], item["report"], item["probe"], *item["artifacts_after_probe"].values()):
            check(entry["path"], entry["size_bytes"], entry["sha256"], "九月九日候选保留")
review_paths = {str(Path(item["Path"]).resolve()) for item in protected if "reviewrep" in Path(item["Path"]).parts}
review_root = Path(next(iter(review_paths))).parent
actual_review = {str(path.resolve()) for path in review_root.rglob("*") if path.is_file()}
result = {"status":"Passed" if all(x["passed"] for x in checks) and review_paths == actual_review else "Failed",
          "protected_count":len(protected), "review_file_set_unchanged":review_paths == actual_review,
          "checks":checks}
with (base / args.output).open("x", encoding="utf-8") as stream:
    json.dump(result, stream, ensure_ascii=False, indent=2)
print(result["status"], "保护文件", len(protected), "检查", len(checks))
raise SystemExit(0 if result["status"] == "Passed" else 2)
