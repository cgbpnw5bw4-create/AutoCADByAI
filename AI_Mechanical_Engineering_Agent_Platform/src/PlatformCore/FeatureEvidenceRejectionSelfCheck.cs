using System.Reflection;
using DomainSchemas;

namespace PlatformCore;

// 只运行注册表预检，不创建 Worker 会话或调用 CAD。未知与错误拒绝必须分开报告。
internal static class FeatureEvidenceRejectionSelfCheck
{
    internal static FeatureDefinition RevolveSample() => new(
        "selfcheck_unverified_revolve", FeatureTypes.RevolveBoss,
        new Dictionary<string, string>
        {
            ["angle_degrees"] = "360", ["profile_selection_mark"] = "0",
            ["axis_selection_mark"] = "16", ["sketch_id"] = "profile"
        }, referencedSketches: ["profile"], targetReference: "RightPlane");

    internal static FeatureDefinition FilletSample() => new(
        "selfcheck_unverified_fillet", FeatureTypes.Fillet,
        new Dictionary<string, string>
        {
            ["radius_mm"] = "1",
            ["edge_selection"] = """{"kind":"circle","adjacent_surface_kinds":["cylinder","plane"],"anchor_y_mm":10,"expected_count":2}"""
        }, targetReference: "TopPlane");

    internal static RejectionProbeResult Run(
        Assembly? assembly, IEnumerable<FeatureDefinition> samples,
        Func<object, SolidWorksBuildPlan, object?>? preflight = null,
        Func<object?>? createRegistry = null)
    {
        var probes = samples.ToArray();
        var outcomes = new List<bool>();
        var issues = new List<string>();
        var registryType = assembly?.GetType("SolidWorksWorker.Features.FeatureHandlerRegistry");
        var contextType = assembly?.GetType("SolidWorksWorker.Features.FeatureHandlerBuildPlanContext");
        createRegistry ??= () => registryType?.GetMethod("CreateDefault")?.Invoke(null, null);
        preflight ??= (registry, plan) => registryType?.GetMethod("ValidateForRealExecution")?.Invoke(registry, [plan]);
        object? Property(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);
        object? Handler(object registry, FeatureDefinition sample) => Property(
            registryType?.GetMethod("Resolve")?.Invoke(registry, [sample]), "Handler");
        bool TargetObserved(object? result, FeatureDefinition sample) =>
            Property(result, "Features") is IEnumerable<FeatureDefinition> features &&
            features.Count(feature => feature.FeatureId == sample.FeatureId && feature.FeatureType == sample.FeatureType) == 1;

        foreach (var sample in probes)
        {
            try
            {
                // 两个全新实例用于确认隔离；证据若改成共享对象，必须停止，不能污染生产状态。
                var controlRegistry = createRegistry();
                var probeRegistry = createRegistry();
                if (controlRegistry is null || probeRegistry is null || contextType is null)
                    throw new InvalidOperationException("注册表或计划上下文不可用。");
                var handler = Handler(probeRegistry, sample);
                var evidence = Property(handler, "ApiEvidence");
                var controlEvidence = Property(Handler(controlRegistry, sample), "ApiEvidence");
                if (handler is null || evidence is null || controlEvidence is null || ReferenceEquals(evidence, controlEvidence))
                    throw new InvalidOperationException("无法获得隔离的 Handler 证据实例。");
                var context = Activator.CreateInstance(contextType,
                    [sample.FeatureId, sample.TargetReference ?? "TopPlane", (IReadOnlyList<string>)Array.Empty<string>()]);
                var built = handler.GetType().GetMethod("BuildPlan")?.Invoke(handler, [sample, context]);
                if (Property(built, "Operation") is not SolidWorksOperation operation || Property(built, "FailureStage") is not null)
                    throw new InvalidOperationException("负样本参数不合法，未形成预检计划。");
                var plan = new SolidWorksBuildPlan("selfcheck-evidence-rejection", "selfcheck", "SolidWorks", "generic_cad_model", "mm",
                    [operation], [], [], [], ExecutionStrategy: SolidWorksBuildExecutionStrategies.FeatureHandlerGraph);
                var positive = preflight(probeRegistry, plan);
                if (Property(positive, "IsPassed") is not true || !TargetObserved(positive, sample))
                    throw new InvalidOperationException("同一计划的已取证正向控制未通过，不能归因于未取证拒绝。");

                var status = evidence.GetType().GetProperty("Status")
                    ?? throw new InvalidOperationException("证据状态不可写。");
                var originalStatus = status.GetValue(evidence);
                bool rejected;
                try
                {
                    // 仅改变私有探针实例的状态，保留原参数、来源、大小及摘要。
                    status.SetValue(evidence, "unverified");
                    if (Property(Property(handler, "ApiEvidence"), "Status")?.ToString() != "unverified")
                        throw new InvalidOperationException("未取证状态未注入成功。");
                    var negative = preflight(probeRegistry, plan);
                    if (negative is null || !TargetObserved(negative, sample))
                        throw new InvalidOperationException("注册表未返回目标负样本的观测结果。");
                    rejected = Property(negative, "IsPassed") is false &&
                        Property(negative, "FailureStage")?.ToString() == PartFamilyFailureStages.FeatureApiUnverified;
                }
                finally { status.SetValue(evidence, originalStatus); }
                outcomes.Add(rejected);
                if (!rejected) issues.Add($"{sample.FeatureType}: 注册表未以 feature_api_unverified 拒绝负样本。");
            }
            catch (Exception exception) when (exception is TargetInvocationException or InvalidOperationException or ArgumentException or MemberAccessException)
            {
                issues.Add($"{sample.FeatureType}: 未观测负样本；{exception.GetBaseException().Message}");
            }
        }
        if (probes.Length == 0) issues.Add("未提供负样本，不能证明未取证拒绝能力。");
        bool? passed = outcomes.Contains(false) ? false : outcomes.Count > 0 && outcomes.Count == probes.Length ? true : null;
        return new(passed, outcomes.Count, issues);
    }

    internal static void AddObserved(IDictionary<string, bool> snapshot, string field, bool? value)
    {
        if (value.HasValue) snapshot[field] = value.Value;
        else snapshot.Remove(field); // 基线门将缺项报告为未观测，不能用旧值或 true 填补。
    }
}

internal sealed record RejectionProbeResult(bool? Passed, int ObservedNegativeSamples, IReadOnlyList<string> Issues);
