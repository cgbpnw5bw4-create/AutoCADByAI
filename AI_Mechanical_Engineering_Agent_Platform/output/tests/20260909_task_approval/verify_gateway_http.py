"""仅使用本机 Mock Runtime 检查实际 HTTP 路由；不调用 CAD 或外部模型。"""
import json
import os
from pathlib import Path
import socket
import subprocess
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
with socket.socket() as listener:
    listener.bind(("127.0.0.1", 0))
    port = listener.getsockname()[1]
base = f"http://127.0.0.1:{port}"
environment = os.environ.copy()
environment.update(AI_AGENT_RUNTIME_MODE="Mock", SW_DISABLE_REAL_EXECUTION="true", SW_FORCE_FAKE_WORKER="true")
checks = []


def call(method, path, body=None, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["X-Task-Access-Token"] = token
    request = urllib.request.Request(base + path, data=None if body is None else json.dumps(body).encode(), headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            text = response.read().decode()
            return response.status, json.loads(text) if text else None
    except urllib.error.HTTPError as response:
        text = response.read().decode()
        return response.code, json.loads(text) if text else None


def check(name, condition):
    checks.append({"name": name, "passed": bool(condition)})
    assert condition, name


with (OUT / "gateway_http_server.log").open("w", encoding="utf-8") as log:
    server = subprocess.Popen(["dotnet", str(ROOT / "src/Interfaces/AgentGatewayHost/bin/Debug/net10.0/AgentGatewayHost.dll"),
                               "--urls", base], cwd=ROOT, env=environment, stdout=log, stderr=subprocess.STDOUT,
                              creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        deadline = time.monotonic() + 20
        while True:
            try:
                status, directory = call("GET", "/agents")
                break
            except urllib.error.URLError:
                if time.monotonic() >= deadline or server.poll() is not None:
                    raise
                time.sleep(0.1)
        check("only_chief_public", status == 200 and [agent["id"] for agent in directory] == ["chief-engineer"])
        message = dict(source="http-smoke", channel="local", conversation_id="v22-http", user="tester", message="检查工程审批流程",
                       attachments=[], context={"dry_run": "true", "test_scenario": "drawing_reviewer_needs_human_approval"})
        status, _ = call("POST", "/agents/cad-modeler/message", message)
        check("internal_agent_hidden", status == 404)
        for decision in ["Approve", "Reject", "RequestRevision"]:
            status, created = call("POST", "/agents/chief-engineer/message", message)
            check(f"{decision}_waiting", status == 200 and created["task_status"] == "WaitingForHumanApproval")
            task_id, token = created["task_id"], created["task_access_token"]
            route = "/tasks/" + task_id
            status, _ = call("GET", route)
            check(f"{decision}_query_requires_token", status == 404)
            status, snapshot = call("GET", route, token=token)
            check(f"{decision}_query_snapshot", status == 200 and snapshot["task"]["id"] == task_id and not snapshot["result"].get("task_access_token"))
            pending = snapshot["pending_approval"]
            approval = {key: pending[key] for key in ["workflow_id", "approval_request_id", "step_id"]}
            approval.update(submitted_by="http-smoke-reviewer")
            status, _ = call("POST", route + "/approvals", approval, token)
            check(f"{decision}_missing_decision_refused", status == 409)
            approval["decision"] = decision
            status, _ = call("POST", route + "/approvals", approval)
            check(f"{decision}_approval_requires_token", status == 404)
            status, _ = call("POST", route + "/approvals", approval | {"approval_request_id": "stale"}, token)
            check(f"{decision}_wrong_identity_refused", status == 409)
            status, result = call("POST", route + "/approvals", approval, token)
            expected = "Passed" if decision == "Approve" else "Rejected"
            check(f"{decision}_correct_terminal", status == 200 and result["accepted"] and result["task"]["task"]["status"] == expected)
            check(f"{decision}_no_pending_or_token", result["task"]["pending_approval"] is None and not result["task"]["result"].get("task_access_token"))
            status, _ = call("POST", route + "/approvals", approval, token)
            check(f"{decision}_replay_refused", status == 409)
            status, final = call("GET", route, token=token)
            check(f"{decision}_terminal_query", status == 200 and final["task"]["status"] == expected)
    finally:
        server.terminate()
        server.wait(timeout=15)
        report = {"checks": checks, "passed": all(item["passed"] for item in checks), "check_count": len(checks),
                  "runtime_mode": "Mock", "real_cad_executed": False, "external_model_called": False,
                  "server_stopped": server.poll() is not None, "tokens_in_report": False}
        (OUT / "gateway_http_validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=False))
