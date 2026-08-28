using System.Text.Json;

namespace PlatformCore;

/// <summary>
/// 保护已经通过的能力字段不在后续阶段迁移时静默退化。
/// 基线是受版本控制的能力契约，而不是某次机器环境的完整 self-check 输出，
/// 因此不会把 CAD smoke 未运行等环境状态误判为能力回归。
/// </summary>
public static class SelfCheckCapabilityRegressionGate
{
    public const string BaselineRelativePath = "docs/self_check_capability_baseline.json";

    public static CapabilityRegressionGateResult Evaluate(
        string projectRoot,
        IReadOnlyDictionary<string, bool> currentCapabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(currentCapabilities);

        var baselinePath = Path.Combine(projectRoot, BaselineRelativePath);
        if (!File.Exists(baselinePath))
        {
            return CapabilityRegressionGateResult.ConfigurationFailure(
                baselinePath,
                $"self-check capability baseline is missing: {BaselineRelativePath}");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(baselinePath));
            if (!document.RootElement.TryGetProperty("protected_capabilities", out var protectedCapabilities) ||
                protectedCapabilities.ValueKind != JsonValueKind.Object)
            {
                return CapabilityRegressionGateResult.ConfigurationFailure(
                    baselinePath,
                    "self-check capability baseline must contain an object named protected_capabilities.");
            }

            var regressions = new List<string>();
            var configurationErrors = new List<string>();
            var protectedCount = 0;
            foreach (var baselineField in protectedCapabilities.EnumerateObject())
            {
                if (baselineField.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    configurationErrors.Add($"baseline field {baselineField.Name} must be boolean.");
                    continue;
                }

                // 基线的语义是「该能力已达成且不得退化」，false 没有合法用途。
                // 若允许 false，削弱基线就成了绕过本闸门阻力最小的路径：
                // 改一个布尔值即可让回归合法化，而自检依旧全绿。
                if (!baselineField.Value.GetBoolean())
                {
                    configurationErrors.Add(
                        $"baseline field {baselineField.Name} is false; " +
                        "a protected capability must stay true. Remove the field deliberately " +
                        "instead of weakening it in place.");
                    continue;
                }

                protectedCount++;
                if (!currentCapabilities.TryGetValue(baselineField.Name, out var currentValue))
                {
                    // 基线保护了一个自检根本没有观测的字段。两者都会让闸失败，
                    // 但必须和真实回归区分开：前者是判据接线漏了，后者是能力掉了，
                    // 混为一谈会让人照着修错的那一头。
                    configurationErrors.Add(
                        $"baseline field {baselineField.Name} is not observed by the self-check; " +
                        "wire it into the capability snapshot or remove it from the baseline.");
                    continue;
                }

                if (!currentValue)
                {
                    regressions.Add(baselineField.Name);
                }
            }

            if (protectedCount == 0)
            {
                configurationErrors.Add("self-check capability baseline must protect at least one field.");
            }

            return new CapabilityRegressionGateResult(
                BaselineLoaded: true,
                BaselinePath: baselinePath,
                ProtectedCapabilityCount: protectedCount,
                RegressedFields: regressions.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                ConfigurationErrors: configurationErrors,
                Passed: regressions.Count == 0 && configurationErrors.Count == 0);
        }
        catch (JsonException exception)
        {
            return CapabilityRegressionGateResult.ConfigurationFailure(
                baselinePath,
                $"self-check capability baseline is invalid JSON: {exception.Message}");
        }
        catch (IOException exception)
        {
            return CapabilityRegressionGateResult.ConfigurationFailure(
                baselinePath,
                $"self-check capability baseline cannot be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return CapabilityRegressionGateResult.ConfigurationFailure(
                baselinePath,
                $"self-check capability baseline cannot be accessed: {exception.Message}");
        }
    }
}

public sealed record CapabilityRegressionGateResult(
    bool BaselineLoaded,
    string BaselinePath,
    int ProtectedCapabilityCount,
    IReadOnlyList<string> RegressedFields,
    IReadOnlyList<string> ConfigurationErrors,
    bool Passed)
{
    public static CapabilityRegressionGateResult ConfigurationFailure(string baselinePath, string issue) =>
        new(false, baselinePath, 0, Array.Empty<string>(), [issue], false);
}
