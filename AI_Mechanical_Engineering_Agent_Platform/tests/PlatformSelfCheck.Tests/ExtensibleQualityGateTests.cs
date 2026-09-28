using DomainSchemas;
using QualityGate;

namespace PlatformSelfCheck.Tests;

public sealed class ExtensibleQualityGateTests
{
    [Theory]
    [InlineData(true, 1.0, false, false)]
    [InlineData(true, 0.79, false, false)]
    [InlineData(false, 1.0, false, false)]
    [InlineData(false, 0.1, true, false)]
    [InlineData(true, 1.0, true, true)]
    public void DefaultPathPreservesExistingDecisionAndRejectReasons(
        bool passed, double score, bool approval, bool fatal)
    {
        var review = Review(passed, score, approval, fatal) with { Issues = ["原始问题", "原始问题"] };
        var policy = new GateDecisionPolicy();
        var expected = policy.Decide(review);

        var actual = new DefaultGatekeeper(policy, new RejectReportBuilder()).Evaluate(review);

        Assert.Equal(expected, actual.Decision with { RejectReport = null });
        Assert.Equal(QualityGateDomain.General, Assert.Single(actual.Checks).Domain);
        if (expected.Result == GateDecisionResult.Rejected)
        {
            Assert.Equal(review.Issues, actual.RejectReport!.Reasons);
        }
        else
        {
            Assert.Null(actual.RejectReport);
        }
    }

    [Theory]
    [InlineData(QualityGateDomain.Geometry)]
    [InlineData(QualityGateDomain.APIEvidence)]
    [InlineData(QualityGateDomain.Assembly)]
    [InlineData(QualityGateDomain.Drawing)]
    [InlineData(QualityGateDomain.Manufacturability)]
    public void RegisteredDomainGateCanBlockOtherwisePassedReview(QualityGateDomain domain)
    {
        var gate = new TestGate(domain, _ => Decision(GateDecisionResult.Rejected, "缺少独立验收证据"));
        var review = Review() with { Issues = ["既有复审提示"] };

        var result = Gatekeeper(gate).Evaluate(review);

        Assert.Equal(GateDecisionResult.Rejected, result.Decision.Result);
        Assert.Contains("缺少独立验收证据", result.RejectReport!.Reasons);
        Assert.Contains("既有复审提示", result.RejectReport.Reasons);
        Assert.Contains(result.Checks, check => check.Domain == domain && check.Decision.Result == GateDecisionResult.Rejected);
        Assert.Same(review, gate.LastReview);
    }

    [Theory]
    [InlineData(true, false, true, GateDecisionResult.Passed, GateDecisionResult.Failed)]
    [InlineData(true, true, false, GateDecisionResult.Passed, GateDecisionResult.NeedsHumanApproval)]
    [InlineData(false, false, false, GateDecisionResult.Passed, GateDecisionResult.Rejected)]
    [InlineData(false, false, false, GateDecisionResult.NeedsHumanApproval, GateDecisionResult.Rejected)]
    [InlineData(true, true, false, GateDecisionResult.Rejected, GateDecisionResult.Rejected)]
    [InlineData(true, true, false, GateDecisionResult.Failed, GateDecisionResult.Failed)]
    public void CombinedDecisionPreservesFatalApprovalAndRejectionPriority(
        bool passed, bool approval, bool fatal, GateDecisionResult extra, GateDecisionResult expected)
    {
        var gate = new TestGate(QualityGateDomain.APIEvidence, _ => Decision(extra));

        var result = Gatekeeper(gate).Evaluate(Review(passed, 1, approval, fatal));

        Assert.Equal(expected, result.Decision.Result);
        Assert.Equal(2, result.Checks.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void BrokenExtensionFailsClosedAndRetainsDiagnostic(int failure)
    {
        var gate = new TestGate(QualityGateDomain.Geometry, _ => failure switch
        {
            0 => throw new InvalidOperationException("测量报告不可读"),
            1 => null!,
            _ => Decision((GateDecisionResult)999)
        });

        var result = Gatekeeper(gate).Evaluate(Review());

        Assert.Equal(GateDecisionResult.Failed, result.Decision.Result);
        Assert.Contains(gate.Name, result.Decision.Reason);
        Assert.False(string.IsNullOrWhiteSpace(result.Decision.NextAction));
        Assert.Null(result.RejectReport);
        Assert.Equal(GateDecisionResult.Failed, result.Checks[1].Decision.Result);
    }

    [Fact]
    public void InvalidDomainCannotBeRegisteredAsSupportedGate()
    {
        var gate = new TestGate((QualityGateDomain)999, _ => Decision(GateDecisionResult.Passed));

        Assert.Throws<ArgumentException>(() => Gatekeeper(gate));
    }

    private static DefaultGatekeeper Gatekeeper(IQualityGate gate) =>
        new(new GateDecisionPolicy(), new RejectReportBuilder(), [gate]);

    private static ReviewReport Review(bool passed = true, double score = 1, bool approval = false, bool fatal = false) =>
        new("review-extension", "deterministic-validator", passed, score, [], approval, fatal);

    private static GateDecision Decision(GateDecisionResult result, string reason = "领域验收裁决") =>
        new("domain-check", result, reason);

    private sealed class TestGate(QualityGateDomain domain, Func<ReviewReport, GateDecision> evaluate) : IQualityGate
    {
        public string Name => "test-domain-gate";
        public QualityGateDomain Domain => domain;
        public ReviewReport? LastReview { get; private set; }

        public GateDecision Evaluate(ReviewReport report)
        {
            LastReview = report;
            return evaluate(report);
        }
    }
}
