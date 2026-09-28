import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parents[3]
out = Path(__file__).resolve().parent
repo = root.parent
prefix = root.name + "/"
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

def run_git(*args):
    return subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True).stdout

def tests(path):
    doc = ET.parse(path)
    counters = doc.find(".//t:Counters", ns).attrib
    results = {r.attrib["testName"]: r.attrib["outcome"] for r in doc.findall(".//t:UnitTestResult", ns)}
    failures = {r.attrib["testName"]: r.findtext("t:Output/t:ErrorInfo/t:Message", namespaces=ns)
                for r in doc.findall(".//t:UnitTestResult", ns) if r.attrib["outcome"] == "Failed"}
    return {"counts": counters, "results": results, "failures": failures}

before = tests(out / "baseline/baseline.trx")
after = tests(out / "full/full.trx")
summary = {
    "baseline": before["counts"], "final": after["counts"],
    "baseline_failures": before["failures"], "final_failures": after["failures"],
    "new_failures": sorted(set(after["failures"]) - set(before["failures"])),
    "baseline_failures_still_present": set(after["failures"]) == set(before["failures"]),
    "baseline_names_missing_after_change": sorted(set(before["results"]) - set(after["results"])),
    "new_or_renamed_test_results": {name: result for name, result in after["results"].items() if name not in before["results"]},
}
old_route_test = "PlatformSelfCheck.Tests.InternalAgentRoutingTests.ChiefEngineerCreatesInternalCollaborationReportWithExpectedCalledAgents"
replacement_tests = [
    "PlatformSelfCheck.Tests.InternalAgentRoutingTests.ChiefEngineerDefaultValidatesPlanningInputWithoutCallingInternalAgents",
    "PlatformSelfCheck.Tests.InternalAgentRoutingTests.ExplicitLegacyRoutePreservesFourInternalAgents",
]
summary["intentional_route_test_migration"] = {"previous": old_route_test, "replacements": replacement_tests,
    "replacement_results": {name: after["results"].get(name) for name in replacement_tests}}
summary["unaccounted_missing_baseline_tests"] = sorted(set(summary["baseline_names_missing_after_change"]) - {old_route_test})
hashes = json.loads((out / "reviewrep_before.json").read_text(encoding="utf-8-sig"))
summary["reviewrep_unchanged"] = all(hashlib.sha256(Path(x["Path"]).read_bytes()).hexdigest().upper() == x["Hash"] for x in hashes)
summary["reviewrep_file_count"] = len(hashes)
summary["reviewrep_file_set_unchanged"] = {Path(x["Path"]).name for x in hashes} == {p.name for p in (repo / "reviewrep").iterdir() if p.is_file()}

protected = [prefix + p for p in ["src/Workers/SolidWorks", "src/Modules/CADModeling", "src/DomainSchemas", "evidence", "docs/self_check_capability_baseline.json"]]
summary["protected_paths_diff"] = run_git("diff", "--name-only", "HEAD", "--", *protected).decode().splitlines()
summary["protected_paths_unchanged"] = not summary["protected_paths_diff"]

report_path = out / "reports/platform_self_check_report.json"
if report_path.exists():
    report = json.loads(report_path.read_text(encoding="utf-8-sig"))
    summary["self_check"] = {k: v for k, v in report.items() if k in ["schema_version", "final_status", "frontier_architecture_checks", "markdown_chinese_check_passed", "v2_0_e_final_status", "v2_0_e_capability_regressions", "jacket_real_workflow_supported", "jacket_production_evidence_active"]}

# 这些受版本控制的模拟报告在修改前工作区干净，现有测试会改写它们。
# 本次结果另存到独立验证目录后，按原 Git blob 字节恢复，避免把测试副产物混入交付。
restored = []
for kind in ["simple", "counterbore", "countersink", "tapped"]:
    relative = f"output/tests/v21_b/{kind}/reports/build_report.json"
    target = root / relative
    original = run_git("cat-file", "--filters", "HEAD:" + prefix + relative)
    if target.read_bytes() != original:
        saved = out / "generated_test_reports" / kind / "build_report.json"
        saved.parent.mkdir(parents=True, exist_ok=True)
        if target.read_bytes().replace(b"\r\n", b"\n") != original.replace(b"\r\n", b"\n"):
            saved.write_bytes(target.read_bytes())
        target.write_bytes(original)
        restored.append(relative)
summary["test_outputs_preserved_and_originals_restored"] = restored
(out / "verification_summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({k: v for k, v in summary.items() if k not in ["baseline_failures", "final_failures", "new_or_renamed_test_results"]}, ensure_ascii=False, indent=2))
