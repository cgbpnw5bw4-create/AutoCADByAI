"""核对本轮验证结果与历史保护快照，单独归档测试夹具时间戳。"""
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parents[3]
base = Path(__file__).resolve().parent
def read(path): return json.loads(path.read_text(encoding="utf-8-sig"))
def fingerprint(path):
    value = path.read_bytes()
    return {"path": str(path), "size_bytes": len(value), "sha256": hashlib.sha256(value).hexdigest()}

protected = read(base / "protected_before.json")
for record in protected:
    assert fingerprint(Path(record["path"])) == record, record["path"]

trx = base / "final_tests/full.trx"
counters = ET.parse(trx).find(".//{*}Counters").attrib
assert counters["total"] == counters["passed"] == counters["executed"] == "797" and counters["failed"] == "0", counters
report_path = base / "final_self_check/reports/platform_self_check_report.json"
report = read(report_path)
fields = ["unverified_api_blocks_real_execution", "unverified_feature_blocks_execution"]
assert report["final_status"] == "Passed" and report["v2_0_e_capability_regression_gate_passed"]
assert all(report[field] is True and report["unverified_evidence_negative_samples"][field] == 1 for field in fields)
assert report["unverified_evidence_self_check_issues"] == []

build = (base / "final_build.log").read_text(encoding="utf-8-sig")
assert "已成功生成" in build and re.search(r"(?m)^\s*0 个错误\s*$", build)
warnings = int(re.search(r"(?m)^\s*(\d+) 个警告\s*$", build)[1])
archive = base / "generated_test_fixtures"
archive.mkdir(exist_ok=False)
restored = []
for name in ["simple", "counterbore", "countersink", "tapped"]:
    original_path = base / f"original_test_fixture_{name}.json"
    current_path = root / f"output/tests/v21_b/{name}/reports/build_report.json"
    original, current = read(original_path), read(current_path)
    changed = [key for key in set(original) | set(current) if original.get(key) != current.get(key)]
    assert changed in ([], ["generated_at"]), (name, changed)
    (archive / f"{name}.json").write_bytes(current_path.read_bytes())
    current_path.write_bytes(original_path.read_bytes())
    restored.append({"name": name, "changed_keys": changed, "restored": fingerprint(current_path)})

audit = root / "output/validation/v2_2_d_shaft_jacket/EvidenceAudit/bin/Debug/net10.0/EvidenceAudit.dll"
current_revision = json.loads(subprocess.check_output(["dotnet", str(audit), "revision"], cwd=root, encoding="utf-8-sig"))
old_revision = read(root / "output/validation/v2_2_d_shaft_jacket/verification_summary.json")["cad_evidence"]["source_revisions"]
assert current_revision == old_revision
summary = {
    "issue": "HIGH-002", "review_report": str(root.parent / "reviewrep/2026-09-29-v2.2-d-project-review.md"),
    "status": "Passed", "build": {"status": "Passed", "errors": 0, "warnings": warnings, "log": fingerprint(base / "final_build.log")},
    "tests": {"total": 797, "passed": 797, "failed": 0, "skipped": 0, "new_regressions": 14, "trx": fingerprint(trx)},
    "self_check": {"final_status": report["final_status"], "report": fingerprint(report_path),
                   "capabilities": {field: report[field] for field in fields},
                   "observed_negative_samples": report["unverified_evidence_negative_samples"], "issues": report["unverified_evidence_self_check_issues"]},
    "historical_preservation": {"checked_files": len(protected), "changed_files": [], "snapshot": fingerprint(base / "protected_before.json")},
    "restored_test_fixtures": restored, "cad_source_revisions_unchanged": current_revision,
    "scope": "仅修改PlatformCore自检、增加14项测试及中文修复说明；未修改Worker、Adapter、能力基线、历史审查或CAD证据。",
    "cad_execution": "本轮未启动SolidWorks、未重新采证。",
    "build_note": "最终构建发生4次旧测试进程文件占用重试后成功；没有编译错误，随后完整测试及自检通过。" if warnings else "构建无警告。"
}
with (base / "verification_summary.json").open("x", encoding="utf-8") as stream:
    json.dump(summary, stream, ensure_ascii=False, indent=2)
print(json.dumps({"status": "Passed", "tests": "797/797", "new_tests": 14, "self_check": "Passed", "protected_files_unchanged": len(protected)}, ensure_ascii=False))
