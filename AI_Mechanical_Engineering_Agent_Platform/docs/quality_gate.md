# Quality Gate

QualityGate separates review from execution.

Core components:

- Validator: deterministic checks that produce or support `ReviewReport`
- Reviewer: review logic that produces `ReviewReport`
- Gatekeeper: consumes `ReviewReport` and returns `GateDecision`
- RejectReportBuilder: converts rejected reviews into `RejectReport`
- RetryPolicy: defines retry and escalation behavior

GateDecision results:

- Passed
- Rejected
- Failed
- NeedsHumanApproval

Basic flow:

1. Worker or Skill output is reviewed.
2. Validator or Reviewer creates `ReviewReport`.
3. Gatekeeper applies `GateDecisionPolicy`.
4. If rejected, RejectReportBuilder creates `RejectReport`.
5. WorkflowEngine continues, returns to a previous step, fails, or waits for human approval based on the decision.

## Retry Delay Semantics

`RetryPolicy` does more than decide whether a rejected step can retry. It also owns the retry delay through `GetDelay(retryCount)`.

- `DefaultRetryPolicy` may return `TimeSpan.Zero`; this means retry immediately.
- `ExponentialBackoffRetryPolicy` returns increasing delays based on `BaseDelayMs`, `Multiplier`, and `MaxDelayMs`.
- `SequentialWorkflowEngine` must actually await a positive retry delay before running the next attempt.
- Retry delay awaits must use `CancellationToken`, so a workflow can be cancelled while waiting.
- The delay is recorded in AuditLog with the retry action.

The delay applies only to retryable `Rejected` decisions. `Failed` and `NeedsHumanApproval` are terminal automatic-flow decisions and must not wait for retry delay or retry the step.
