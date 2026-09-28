namespace ModelRuntime;

/// <summary>提供商仅生成工程理解、规划或决策文本，不接收执行工具或 CAD 对象。</summary>
public interface IModelProvider
{
    Task<string> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default);
}

public enum EngineeringModelPurpose
{
    RequirementUnderstanding,
    DesignPlanning,
    FeaturePlanning,
    ModelingSequence,
    AssemblyStrategy,
    RecoveryAdvice,
    EngineeringDecision
}

public sealed record ModelRequest(
    EngineeringModelPurpose Purpose,
    string SystemPrompt,
    string UserMessage);
