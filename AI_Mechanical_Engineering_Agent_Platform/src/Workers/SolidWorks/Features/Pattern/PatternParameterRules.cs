using System.Globalization;
using DomainSchemas;

namespace SolidWorksWorker.Features.Pattern;

/// <summary>
/// 线性阵列与圆周阵列共用的参数规则。抽出来避免两个 Handler 各写一份，
/// 也避免任何一方悄悄放宽约束。
/// </summary>
internal static class PatternParameterRules
{
    public const int MinimumInstanceCount = 2;
    public const int MaximumInstanceCount = 512;

    public static FeatureHandlerValidationResult? ValidateSeedAndCount(
        FeatureDefinition feature,
        string handlerName)
    {
        if (!feature.Parameters.TryGetValue("seed_feature", out var seed) ||
            string.IsNullOrWhiteSpace(seed))
        {
            return Invalid(feature, "seed_feature must reference an existing feature id.");
        }

        if (feature.ReferencedFeatures.Count > 0 &&
            !feature.ReferencedFeatures.Contains(seed, StringComparer.OrdinalIgnoreCase))
        {
            return Invalid(
                feature,
                $"seed_feature={seed} is not present in referenced_features, so {handlerName} cannot bind it.");
        }

        if (!TryInteger(feature, "instance_count", out var instances) ||
            instances < MinimumInstanceCount ||
            instances > MaximumInstanceCount)
        {
            return Invalid(
                feature,
                $"instance_count must be an integer between {MinimumInstanceCount} and {MaximumInstanceCount}.");
        }

        return null;
    }

    /// <summary>
    /// 校验一条"必须唯一命中"的边判据。方向与轴都只能来自一条边，
    /// 因此 ExpectedCount 必须是 1——允许多条就等于允许执行时任选一条。
    /// </summary>
    public static FeatureHandlerValidationResult? ValidateSingleEdgeCriteria(
        FeatureDefinition feature,
        string parameterName)
    {
        feature.Parameters.TryGetValue(parameterName, out var json);
        if (!EdgeSelectionCriteriaParser.TryParse(json, out var criteria, out var issue) ||
            criteria is null)
        {
            return Invalid(feature, $"{parameterName} is unusable: {issue}");
        }

        return criteria.ExpectedCount == 1
            ? null
            : Invalid(
                feature,
                $"{parameterName} must declare expected_count=1; " +
                $"a direction or axis can only come from one edge, and {criteria.ExpectedCount} " +
                "would leave the choice to execution time.");
    }

    public static bool TryPositive(FeatureDefinition feature, string name, out double value)
    {
        value = 0d;
        return feature.Parameters.TryGetValue(name, out var text) &&
               double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value) &&
               value > 0d;
    }

    public static bool TryInteger(FeatureDefinition feature, string name, out int value)
    {
        value = 0;
        return feature.Parameters.TryGetValue(name, out var text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static FeatureHandlerValidationResult Invalid(FeatureDefinition feature, string message) =>
        FeatureHandlerValidationResult.Failed(
            PartFamilyFailureStages.InvalidFeatureParameter,
            $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId}: {message}");
}
