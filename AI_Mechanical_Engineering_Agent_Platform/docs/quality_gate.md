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
