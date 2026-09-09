using AgentContracts;

namespace PlatformCore;

public static class TaskLifecycleSelfCheck
{
    public static async Task<IReadOnlyDictionary<string, bool>> RunAsync(string projectRoot)
    {
        var platform = PlatformBootstrapper.CreateDefault(projectRoot);
        var service = new AgentTaskService(platform);
        var input = new AgentInput("self-check", "local", "task-lifecycle-self-check", "self-check", "检查工程规划流程",
            [], new Dictionary<string, string> { ["test_scenario"] = "drawing_reviewer_needs_human_approval", ["dry_run"] = "true" });
        var created = (await service.ExecuteAsync("chief-engineer", input))!;
        var taskId = created.Result.Task.Id;
        var pending = created.Result.PendingApproval!;
        var submission = new WorkflowApprovalSubmission(pending.WorkflowId, WorkflowApprovalDecision.Approve,
            "self-check", ApprovalRequestId: pending.ApprovalRequestId, StepId: pending.StepId);
        var denied = await service.SubmitApprovalAsync(taskId, "wrong-token", submission);
        var accessRequired = service.Get(taskId, null) is null && service.Get(taskId, "wrong-token") is null &&
            denied.NotFound && service.Get(taskId, created.AccessToken)?.PendingApproval == pending;
        var completed = await service.SubmitApprovalAsync(taskId, created.AccessToken, submission);
        var duplicate = await service.SubmitApprovalAsync(taskId, created.AccessToken, submission);
        var roundtrip = completed.Accepted && completed.Result?.Task.Id == taskId &&
            completed.Result.Task.Status == PlatformTaskStatus.Passed && completed.Result.PendingApproval is null && !duplicate.Accepted &&
            completed.Result.Output?.InternalCollaborationReport?.StepResults?.Last().ApprovalResolution?.Decision == "Approve";
        var invalid = (await service.ExecuteAsync("chief-engineer", input with
        {
            Context = new Dictionary<string, string> { ["cad_model_spec_json"] = "{", ["dry_run"] = "true" }
        }))!;
        var blocked = await service.ExecuteAsync("cad-modeler", input);
        var lifecycle = created.Result.Task.Status == PlatformTaskStatus.WaitingForHumanApproval &&
            invalid.Result.Task.Status == PlatformTaskStatus.Failed && blocked is null &&
            platform.TaskStore.Get(taskId)?.Status == PlatformTaskStatus.Passed &&
            platform.AuditLog.GetEntries().Any(entry => entry.Actor == taskId && entry.Action == "task_running");
        return new Dictionary<string, bool>
        {
            ["task_lifecycle_tracked"] = lifecycle,
            ["task_approval_roundtrip_supported"] = roundtrip,
            ["task_access_token_required"] = accessRequired
        };
    }
}
