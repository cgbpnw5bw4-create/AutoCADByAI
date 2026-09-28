using DomainSchemas;

namespace QualityGate;

public sealed class DefaultGatekeeper : IGatekeeper
{
    private readonly IReadOnlyList<IQualityGate> _gates;
    private readonly RejectReportBuilder _rejectReportBuilder;

    public DefaultGatekeeper(GateDecisionPolicy policy, RejectReportBuilder rejectReportBuilder)
        : this(policy, rejectReportBuilder, [])
    {
    }

    public DefaultGatekeeper(
        GateDecisionPolicy policy,
        RejectReportBuilder rejectReportBuilder,
        IEnumerable<IQualityGate> additionalGates)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(rejectReportBuilder);
        ArgumentNullException.ThrowIfNull(additionalGates);
        _gates = new IQualityGate[] { policy }.Concat(additionalGates).ToArray();
        if (_gates.Any(gate => gate is null || string.IsNullOrWhiteSpace(gate.Name) || !Enum.IsDefined(gate.Domain)))
        {
            throw new ArgumentException("质量门禁必须提供名称和有效领域。", nameof(additionalGates));
        }

        if (_gates.Select(gate => gate.Name).Distinct(StringComparer.Ordinal).Count() != _gates.Count)
        {
            throw new ArgumentException("质量门禁名称不得重复。", nameof(additionalGates));
        }

        _rejectReportBuilder = rejectReportBuilder;
    }

    public GateEvaluationResult Evaluate(ReviewReport reviewReport)
    {
        ArgumentNullException.ThrowIfNull(reviewReport);
        var checks = _gates.Select(gate => EvaluateGate(gate, reviewReport)).ToArray();
        // 独立门禁的打回不能被另一门禁的人工审批覆盖，避免批准后绕过验收。
        var decision = checks.OrderByDescending(check => Priority(check.Decision.Result)).First().Decision;
        if (decision.Result != GateDecisionResult.Rejected)
        {
            return new GateEvaluationResult(decision, null) { Checks = checks };
        }

        var extensionIssues = checks.Skip(1)
            .Where(check => check.Decision.Result == GateDecisionResult.Rejected)
            .Select(check => check.Decision.Reason).ToArray();
        var reportWithIssues = extensionIssues.Length == 0 ? reviewReport : reviewReport with
        {
            Issues = reviewReport.Issues.Concat(extensionIssues).Distinct(StringComparer.Ordinal).ToArray()
        };
        var rejectReport = _rejectReportBuilder.Build(reportWithIssues, decision);
        return new GateEvaluationResult(decision with { RejectReport = rejectReport }, rejectReport) { Checks = checks };
    }

    private static QualityGateCheckResult EvaluateGate(IQualityGate gate, ReviewReport report)
    {
        GateDecision decision;
        try
        {
            decision = gate.Evaluate(report)
                ?? throw new InvalidOperationException("质量门禁未返回裁决。");
            if (!Enum.IsDefined(decision.Result))
            {
                throw new InvalidOperationException("质量门禁返回未知裁决类型。");
            }
        }
        catch (Exception exception)
        {
            decision = new GateDecision(
                $"gate-{report.ReviewId}-{gate.Name}",
                GateDecisionResult.Failed,
                $"质量门禁 {gate.Name} 执行失败：{exception.Message}",
                "保留失败，修复门禁后重新验证。");
        }

        return new QualityGateCheckResult(gate.Name, gate.Domain, decision);
    }

    private static int Priority(GateDecisionResult result) => result switch
    {
        GateDecisionResult.Failed => 3,
        GateDecisionResult.Rejected => 2,
        GateDecisionResult.NeedsHumanApproval => 1,
        GateDecisionResult.Passed => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };
}
