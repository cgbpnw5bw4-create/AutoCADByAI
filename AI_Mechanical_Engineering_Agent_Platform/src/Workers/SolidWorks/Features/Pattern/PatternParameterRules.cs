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
        var seedValidation = ValidateSingleSeed(feature, handlerName);
        if (seedValidation is not null) return seedValidation;

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

    public static FeatureHandlerValidationResult? ValidateSingleSeed(FeatureDefinition feature, string handlerName)
    {
        if (!feature.Parameters.TryGetValue("seed_feature", out var seed) ||
            string.IsNullOrWhiteSpace(seed) || seed.IndexOfAny([';', ',']) >= 0)
        {
            return Invalid(feature, "seed_feature 必须为一个非空特征标识，不允许分号或逗号分隔多个种子。");
        }

        if (feature.ReferencedFeatures.Count != 1 ||
            !string.Equals(feature.ReferencedFeatures[0], seed, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(
                feature,
                $"{handlerName}: referenced_features 必须仅包含 seed_feature={seed}，不能省略绑定或声明多个目标。");
        }

        // ReferencedFeatures 已由 FeatureDefinition.DependsOn 合并为排序依赖，无需重复声明 Dependencies。
        if (string.Equals(seed, feature.FeatureId, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(feature, $"seed_feature={seed} 不能引用阵列自身。");
        }

        return null;
    }

    /// <summary>
    /// 校验一条"必须唯一命中"的边判据。方向与轴都只能来自一条边，
    /// 因此 ExpectedCount 必须是 1——允许多条就等于允许执行时任选一条。
    /// </summary>
    public static FeatureHandlerValidationResult? ValidateSingleEdgeCriteria(
        FeatureDefinition feature,
        string parameterName,
        string? principalAxis = null)
    {
        feature.Parameters.TryGetValue(parameterName, out var json);
        if (!EdgeSelectionCriteriaParser.TryParse(json, out var criteria, out var issue) ||
            criteria is null)
        {
            return Invalid(feature, $"{parameterName} is unusable: {issue}");
        }

        if (principalAxis is not null &&
            (!PrincipalAxisRules.TryParse(principalAxis, out var axis) ||
             !PrincipalAxisRules.HasExplicitOriginAxisPosition(criteria, axis)))
        {
            return Invalid(feature, $"{parameterName} 必须明确主轴横向的两个圆心坐标为 0；不允许用偏心平行轴代替过原点的主轴。");
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
