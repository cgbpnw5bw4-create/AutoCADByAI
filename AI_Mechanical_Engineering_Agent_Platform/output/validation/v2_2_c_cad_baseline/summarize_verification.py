"""汇总本阶段原始运行记录；全局 self-check 失败必须原样披露。"""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--release-verification", required=True)
parser.add_argument("--tests", default="final_tests/full.trx")
parser.add_argument("--self-check", default="final_self_check/reports/platform_self_check_report.json")
parser.add_argument("--preservation", default="preservation_final_20260914.json")
parser.add_argument("--revisions", default="revisions_20260914.json")
parser.add_argument("--output", default="verification_summary_20260914.json")
parser.add_argument("--verification-date", default="2026-09-14")
args = parser.parse_args()
base = Path(__file__).resolve().parent
def read(relative):
    return json.loads((base / relative).read_text(encoding="utf-8-sig"))
def trx(relative):
    tree = ET.parse(base / relative)
    ns = {"t":"http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = tree.find(".//t:Counters", ns).attrib
    results = {x.attrib["testName"]:x.attrib["outcome"] for x in tree.findall(".//t:UnitTestResult", ns)}
    return counters, results
baseline, old_results = trx("baseline/baseline.trx")
final, new_results = trx(args.tests)
failures = [{"test":name,"before":outcome,"after":new_results.get(name,"Missing")} for name,outcome in old_results.items() if outcome == "Failed"]
self_check = read(args.self_check)
release = read(args.release_verification)
preserved = read(args.preservation)
comparison = read("comparison_regression_20260914.json")
result = {
    "stage":"V2.2-C", "verification_date":args.verification_date,
    "test_source":args.tests, "self_check_source":args.self_check,
    "cad_geometry_source":args.release_verification,
    "baseline_tests":baseline, "final_tests":final,
    "original_failures":failures,
    "original_eight_failures_resolved":len(failures) == 8 and all(x["after"] == "Passed" for x in failures),
    "all_tests_passed":int(final["failed"]) == 0 and int(final["passed"]) == int(final["total"]),
    "geometry_comparison_regression":{"passed":comparison["passed"],"cases":len(comparison["checks"])},
    "main_workflow_geometry_verification":release["status"],
    "historical_files_preserved":preserved["status"],
    "self_check":{key:value for key,value in self_check.items() if key in ("final_status","feature_production_evidence_active",
         "v20_d_production_evidence_active","v20_e_controlled_plate_evidence_active","v20_e_step_content_gate_active",
         "v20_e_capability_regression_gate_passed","v20_e_capability_regressions","v20_e_final_status",
         "jacket_real_workflow_supported","jacket_production_evidence_active",
         "shaft_real_workflow_supported","markdown_chinese_check_passed") or
         key.startswith("v2_0_d") or key.startswith("v2_0_e")},
    "revisions":read(args.revisions)
}
with (base / args.output).open("x", encoding="utf-8") as stream:
    json.dump(result, stream, ensure_ascii=False, indent=2)
print(json.dumps(result, ensure_ascii=False, indent=2))
raise SystemExit(0 if result["original_eight_failures_resolved"] and result["all_tests_passed"] and
                 release["status"] == "Passed" and preserved["status"] == "Passed" else 2)
