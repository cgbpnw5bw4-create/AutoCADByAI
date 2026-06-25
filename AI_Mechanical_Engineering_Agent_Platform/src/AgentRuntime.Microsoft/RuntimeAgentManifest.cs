using AgentContracts;

namespace AgentRuntime.Microsoft;

public sealed record RuntimeAgentManifest(
    string Id,
    string Name,
    AgentRole Role,
    string Description,
    AgentVisibility Visibility)
{
    public static RuntimeAgentManifest Create(string id, AgentVisibility? visibility = null)
    {
        var isChiefEngineer = string.Equals(id, "chief-engineer", StringComparison.OrdinalIgnoreCase);
        var resolvedVisibility = visibility ?? (isChiefEngineer ? AgentVisibility.Public : AgentVisibility.Internal);
        var name = id switch
        {
            "chief-engineer" => "机械总工程师",
            "mechanical-designer" => "机械设计师",
            "cad-modeler" => "CAD建模工程师",
            "drawing-engineer" => "工程图工程师",
            "drawing-reviewer" => "出图复审工程师",
            "code-engineer" => "代码工程师",
            "code-reviewer" => "代码复审工程师",
            "error-diagnosis" => "异常诊断工程师",
            _ => id
        };
        var roleDescription = id switch
        {
            "chief-engineer" => "总调度、任务拆解、内部 Agent 协作、质量裁决",
            "mechanical-designer" => "结构方案、机械合理性、参数建议",
            "cad-modeler" => "建模规划、BuildSpec 生成、Worker 调用规划",
            "drawing-engineer" => "工程图生成规划",
            "drawing-reviewer" => "PDF、尺寸、视图、标题栏复审",
            "code-engineer" => "CAD API / SDK 自动化代码开发规划",
            "code-reviewer" => "代码安全性、稳定性、可维护性审查",
            "error-diagnosis" => "失败原因分析和修复建议",
            _ => "Runtime adapter agent"
        };

        return new RuntimeAgentManifest(
            id,
            name,
            new AgentRole(id, name, roleDescription),
            $"Runtime adapter manifest for {id}.",
            resolvedVisibility);
    }
}
