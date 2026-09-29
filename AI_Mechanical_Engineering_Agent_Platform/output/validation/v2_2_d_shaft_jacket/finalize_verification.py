"""只读复核实物和证据，归档测试时间戳副作用后恢复历史夹具，生成新的阶段汇总。"""
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

import collect_candidates as c

base = Path(__file__).resolve().parent
root = c.project_root()
target = base / "verification_summary.json"
c.require(not target.exists(), "验证汇总已经存在，禁止覆盖。")
checked = {}


def verify_fingerprints(value):
    if isinstance(value, list):
        for item in value:
            verify_fingerprints(item)
    elif isinstance(value, dict):
        if {"path", "size_bytes", "sha256"}.issubset(value):
            actual = c.fingerprint(Path(value["path"]))
            c.require(actual["size_bytes"] == value["size_bytes"] and actual["sha256"].lower() == value["sha256"].lower(),
                      "实际文件与记录失配：" + value["path"])
            checked[actual["path"]] = actual
        else:
            for item in value.values():
                verify_fingerprints(item)


initial = c.read_json(base / "initial_snapshot.json")
verify_fingerprints(initial["historical_files"])
historical_count = len(initial["historical_files"])
historical_bytes = sum(item["size_bytes"] for item in initial["historical_files"])

batches = ["collection-20260928_094622-8a31844a", "collection-20260928_094653-23572f9b", "collection-20260928_094845-558791f1"]
items = []
for batch in batches:
    manifest = c.read_json(base / batch / "manifest.json")
    c.require(manifest["status"] == "CandidatesCollected", "候选批次未完成。")
    for item in manifest["items"]:
        verify_fingerprints(item)
        c.require(item["status"] == "CandidateCollected" and item["probe_read_success"] and item["step_probe_read_success"]
                  and item["step_geometry_match"]["passed"] and item["candidate_report_stable"]
                  and item["probe_artifacts_stable"] and item["step_probe_artifacts_stable"], "候选缺少真实双格式验收。")
        native = c.read_json(Path(item["probe"]["path"]))["geometry"]
        step = c.read_json(Path(item["step_probe"]["path"]))["geometry"]
        c.require(native["SolidWorksVersion"] == step["SolidWorksVersion"] == "31.5.0", "候选重开版本不符。")
        c.require(c.compare_geometry(native, step)["passed"], "复算双格式几何不一致。")
        items.append(item)
c.require(len(items) == 14, "最终候选数量不符。")

audit = base / "EvidenceAudit/bin/Debug/net10.0/EvidenceAudit.dll"
def audit_command(command):
    result = subprocess.run(["dotnet", str(audit), command], cwd=root, capture_output=True, text=True,
                            encoding="utf-8-sig", timeout=60, creationflags=subprocess.CREATE_NO_WINDOW)
    c.require(result.returncode == 0, "生产证据复验失败：" + result.stdout + result.stderr)
    return json.loads(result.stdout)

revisions = audit_command("revision")
families = audit_command("family")
c.require(all(item["source_revision"] == revisions["feature"] for item in items), "当前生产源码不匹配最终候选。")
family_manifest = c.read_json(root / "evidence/solidworks/v2_2_d_final/family_acceptance.json")
verify_fingerprints(family_manifest)
c.require(family_manifest["source_revision"] == revisions["feature"] and len(family_manifest["cases"]) == 3, "零件族清单失配。")

main_path = base / "main_workflows_final/verification.json"
main = c.read_json(main_path)
c.require(main["status"] == "Passed" and len(main["cases"]) == 4, "正式主流程未全部验收。")
verify_fingerprints(main["execution_files_after"])
c.require(main["execution_files_before"] == main["execution_files_after"], "正式执行期间源码或程序集改变。")
for case in main["cases"]:
    verify_fingerprints(case)
    c.require(case["status"] == "Passed" and case["step_geometry_match"]["passed"], "正式产物几何未通过。")
    release = Path(case["release_directory"])
    for path in (release / "release_manifest.json", release / "reports/e2e_execution_report.json", release / "reports/package_quality_report.json"):
        report = c.read_json(path)
        c.require(report["final_status"] == "Passed" and report["deliverable_status"] == "Deliverable", "发布包质量链未通过。")

build_log = base / "completion_build.log"
text = build_log.read_text(encoding="utf-8-sig")
c.require("已成功生成" in text and re.search(r"(?m)^\s*0 个错误\s*$", text) and re.search(r"(?m)^\s*0 个警告\s*$", text), "完整构建日志未通过。")
trx = base / "completion_tests/completion.trx"
counters = ET.parse(trx).find(".//{*}Counters").attrib
c.require(counters["total"] == counters["passed"] == counters["executed"] == "783" and counters["failed"] == "0", "完整测试未通过。")
self_check_path = base / "completion_self_check/reports/platform_self_check_report.json"
self_check = c.read_json(self_check_path)
capability_names = ["shaft_real_workflow_supported", "jacket_real_workflow_supported", "jacket_production_evidence_active",
                    "v2_0_e_capability_regression_gate_passed", "markdown_chinese_check_passed"]
c.require(self_check["final_status"] == "Passed" and all(self_check[key] for key in capability_names)
          and self_check["v2_0_e_capability_regressions"] == [], "全局自检或能力基线未通过。")

# 完整测试会更新四个受版本控制的旧夹具时间戳。先严格核对仅这一字段变化，再归档新输出并恢复原字节。
restoration = []
archive = base / "test_fixture_side_effects"
archive.mkdir(exist_ok=False)
for name in ("simple", "counterbore", "countersink", "tapped"):
    relative = f"output/tests/v21_b/{name}/reports/build_report.json"
    path = root / relative
    git_path = f"{root.name}/{relative}"
    result = subprocess.run(["git", "cat-file", "--filters", f"HEAD:{git_path}"], cwd=root, capture_output=True)
    c.require(result.returncode == 0, "不能读取原始夹具。")
    original_bytes = result.stdout
    current_bytes = path.read_bytes()
    original = json.loads(original_bytes.decode("utf-8-sig"))
    current = json.loads(current_bytes.decode("utf-8-sig"))
    changed = [key for key in set(original) | set(current) if original.get(key) != current.get(key)]
    c.require(changed in ([], ["generated_at"]), "旧夹具还有非时间戳改动，禁止恢复覆盖：" + relative)
    with (archive / f"{name}_build_report.json").open("xb") as stream:
        stream.write(current_bytes)
    path.write_bytes(original_bytes)
    c.require(path.read_bytes() == original_bytes, "历史夹具恢复字节不一致。")
    restoration.append({"path": str(path), "only_changed_keys": changed, "original_generated_at": original["generated_at"],
                        "test_generated_at": current["generated_at"], "restored": c.fingerprint(path),
                        "test_output_archive": c.fingerprint(archive / f"{name}_build_report.json")})

summary = {
    "stage": "V2.2-D", "verified_at": datetime.now(timezone.utc).isoformat(), "status": "Passed",
    "baseline_head": initial["head"], "verification_order": ["build", "test", "self-check", "CAD evidence"],
    "build": {"status": "Passed", "errors": 0, "warnings": 0, "log": c.fingerprint(build_log)},
    "tests": {"status": "Passed", "baseline_total": 684, "total": 783, "passed": 783, "failed": 0, "skipped": 0,
              "trx": c.fingerprint(trx), "log": c.fingerprint(base / "completion_tests.log")},
    "self_check": {"status": "Passed", "report": c.fingerprint(self_check_path),
                   "capabilities": {key: self_check[key] for key in capability_names}, "capability_regressions": []},
    "cad_evidence": {"status": "Passed", "solidworks_version": "31.5.0", "source_revisions": revisions,
                     "family_activation": families, "candidates": 14, "independent_candidate_reopens": 28,
                     "collection_manifests": [c.fingerprint(base / batch / "manifest.json") for batch in batches],
                     "family_manifest": c.fingerprint(root / "evidence/solidworks/v2_2_d_final/family_acceptance.json"),
                     "formal_workflows": 4, "independent_formal_reopens": 8, "main_verification": c.fingerprint(main_path),
                     "release_directories": [case["release_directory"] for case in main["cases"]]},
    "historical_preservation": {"status": "Passed", "files": historical_count, "total_bytes": historical_bytes,
                                "review_paths": len(initial["historical_review_paths"]), "changed_files": [],
                                "initial_snapshot": c.fingerprint(base / "initial_snapshot.json")},
    "test_fixture_restoration": restoration,
    "physical_fingerprints_rechecked": list(checked.values()),
    "scope": ["直轴Φ40×180", "阶梯轴Φ40/32/24，段长110/40/30", "夹套外径140、内径120、长180"],
    "remaining_issues": ["历史文件增加4096字节的成因未查明；本轮受保护历史文件与新证据字节均复核一致。",
                         "保存失败且路径不可信时的残留文档仍只记录；未发现其改变本轮最终证据。",
                         "其他尺寸、任意旋转形状、约束执行及其他SolidWorks版本尚未获得本轮生产授权。"],
    "failed_attempts_retained": ["main_workflows", "main_workflows_cli_fixed", "evidence/solidworks/v2_2_d_diagnostic_attempts", "evidence/solidworks/v2_2_d_refresh"],
    "verification_notes": ["普通CLI请求已省略不存在的参数更新字段，显式空值拒绝规则和原断言保留。",
                           "早期四孔板复核脚本误把STEP重复圆边条目计为额外圆口；保留失败记录，新验收核对真实位置、半径、周长、邻接面、轴向及体积。",
                           "候选均保持NotDeliverable，交付结论仅引用四次正式主流程的同次质量链。"]
}
with target.open("x", encoding="utf-8") as stream:
    json.dump(summary, stream, ensure_ascii=False, indent=2)
print(json.dumps({"status": "Passed", "historical_files_unchanged": historical_count,
                  "physical_files_verified": len(checked), "tests": "783/783", "self_check": "Passed", "formal_workflows": 4}, ensure_ascii=False))
