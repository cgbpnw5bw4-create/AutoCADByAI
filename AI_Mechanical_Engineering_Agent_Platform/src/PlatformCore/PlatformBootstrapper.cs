using AgentContracts;
using ModuleContracts;

namespace PlatformCore;

public static class PlatformBootstrapper
{
    public static PlatformKernel CreateDefault()
    {
        var platform = new PlatformKernel();

        RegisterModules(platform);
        RegisterAgents(platform);
        RegisterSkills(platform);
        RegisterWorkers(platform);

        platform.EventBus.Publish("platform.initialized", "Default platform skeleton initialized.");
        platform.AuditLog.Record("platform", "bootstrapper", "initialized", "Default platform skeleton initialized.");

        return platform;
    }

    private static void RegisterModules(PlatformKernel platform)
    {
        foreach (var manifest in CreateModuleManifests())
        {
            platform.ModuleRegistry.Register(manifest);
        }

        platform.AuditLog.Record("module", "bootstrapper", "registered", "Registered base module manifests.");
    }

    private static void RegisterAgents(PlatformKernel platform)
    {
        platform.AgentRegistry.Register(new PlaceholderAgent(
            "chief-engineer",
            "机械总工程师",
            new AgentRole("chief-engineer", "机械总工程师", "总调度、任务拆解、内部 Agent 协作、质量裁决"),
            "Public entry point for external gateways. Delegates to internal engineering agents.",
            AgentVisibility.Public,
            "mechanical-designer"));

        platform.AgentRegistry.Register(new PlaceholderAgent(
            "mechanical-designer",
            "机械设计师",
            new AgentRole("mechanical-designer", "机械设计师", "结构方案、机械合理性、参数建议"),
            "Internal mechanical design planner.",
            AgentVisibility.Internal,
            "cad-modeler"));

        platform.AgentRegistry.Register(new PlaceholderAgent(
            "cad-modeler",
            "CAD建模工程师",
            new AgentRole("cad-modeler", "CAD建模工程师", "建模规划、BuildSpec 生成、Worker 调用规划"),
            "Internal CAD modeling planner. Does not call CAD software directly.",
            AgentVisibility.Internal,
            "drawing-engineer"));

        platform.AgentRegistry.Register(new PlaceholderAgent(
            "drawing-engineer",
            "工程图工程师",
            new AgentRole("drawing-engineer", "工程图工程师", "工程图生成规划"),
            "Internal drawing generation planner.",
            AgentVisibility.Internal,
            "drawing-reviewer"));

        platform.AgentRegistry.Register(new PlaceholderAgent(
            "drawing-reviewer",
            "出图复审工程师",
            new AgentRole("drawing-reviewer", "出图复审工程师", "PDF、尺寸、视图、标题栏复审"),
            "Internal drawing reviewer.",
            AgentVisibility.Internal));

        platform.AgentRegistry.Register(new PlaceholderAgent(
            "error-diagnosis",
            "异常诊断工程师",
            new AgentRole("error-diagnosis", "异常诊断工程师", "失败原因分析和修复建议"),
            "Internal failure analysis agent.",
            AgentVisibility.Internal));

        platform.AuditLog.Record("agent", "bootstrapper", "registered", "Registered base agent placeholders.");
    }

    private static void RegisterSkills(PlatformKernel platform)
    {
        platform.SkillRegistry.Register(new PlaceholderSkill(
            "requirement-to-cad-model-spec",
            "Convert natural-language requirements into CADModelSpec placeholders."));

        platform.SkillRegistry.Register(new PlaceholderSkill(
            "build-spec-planning",
            "Plan structured BuildSpec placeholders without CAD execution."));

        platform.SkillRegistry.Register(new PlaceholderSkill(
            "drawing-review-summary",
            "Summarize drawing review inputs into ReviewReport placeholders."));

        platform.AuditLog.Record("skill", "bootstrapper", "registered", "Registered base skill placeholders.");
    }

    private static void RegisterWorkers(PlatformKernel platform)
    {
        platform.WorkerRegistry.Register(new PlaceholderWorker("FakeSolidWorksWorker", "SolidWorks"));
        platform.WorkerRegistry.Register(new PlaceholderWorker("FakeAutoCADWorker", "AutoCAD"));

        platform.AuditLog.Record("worker", "bootstrapper", "registered", "Registered fake CAD worker placeholders.");
    }

    private static IReadOnlyList<ModuleManifest> CreateModuleManifests() =>
        new[]
        {
            Module("RequirementUnderstanding", "Converts natural language requirements into CADModelSpec.", "requirement-to-cad-model-spec"),
            Module("MechanicalDesign", "Checks mechanical feasibility, structure and parameter assumptions.", "mechanical-design-review"),
            Module("CADModeling", "Plans BuildSpec handoff to CAD workers.", "build-spec-planning"),
            Module("DrawingGeneration", "Plans engineering drawing generation tasks.", "drawing-generation-planning"),
            Module("DrawingReview", "Reviews drawings, PDFs, dimensions, views and title blocks.", "drawing-review-summary"),
            Module("CodeEngineering", "Plans future CAD API, SDK and COM automation code work.", "cad-code-engineering"),
            Module("CodeReview", "Reviews future automation code for safety and maintainability.", "cad-code-review"),
            Module("ErrorDiagnosis", "Diagnoses execution failures and suggests fixes.", "error-diagnosis")
        };

    private static ModuleManifest Module(string name, string description, string capabilityName) =>
        new(
            name,
            "0.1.0",
            description,
            new[]
            {
                new ModuleCapability(
                    capabilityName,
                    description,
                    new[] { "AgentInput" },
                    new[] { "StructuredPlatformResult" })
            },
            Array.Empty<ModuleDependency>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());
}
