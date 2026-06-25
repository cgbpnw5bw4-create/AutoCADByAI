namespace PlatformCore;

public enum WorkflowStepStatus
{
    Pending,
    Running,
    Passed,
    Rejected,
    Retrying,
    Failed,
    WaitingForHumanApproval,
    Skipped
}
