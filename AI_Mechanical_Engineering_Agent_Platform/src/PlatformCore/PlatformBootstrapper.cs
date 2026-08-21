using AgentContracts;
using ModuleContracts;
using PlatformCore.Modules.CADModeling.Agents;
using PlatformCore.Modules.CADModeling.Skills;
using PlatformCore.Modules.CodeEngineering.Agents;
using PlatformCore.Modules.CodeReview.Agents;
using PlatformCore.Modules.DrawingGeneration.Agents;
using PlatformCore.Modules.DrawingReview.Agents;
using PlatformCore.Modules.ErrorDiagnosis.Agents;
using PlatformCore.Modules.MechanicalDesign.Agents;
using PlatformCore.Modules.RequirementUnderstanding.Agents;
using System.Reflection;
using WorkerContracts;

namespace PlatformCore;

public static class PlatformBootstrapper
{
    public static PlatformKernel CreateDefault(string? projectRoot = null, Func<string, IAgent>? runtimeAgentFactory = null)
    {
        var platform = new PlatformKernel();

        RegisterModules(platform, projectRoot);
        RegisterAgents(platform, runtimeAgentFactory);
        RegisterSkills(platform);
        RegisterWorkers(platform, projectRoot);

        platform.EventBus.Publish("platform.initialized", "Default platform skeleton initialized.");
        platform.AuditLog.Record("platform", "bootstrapper", "initialized", "Default platform skeleton initialized.");

        return platform;
    }

    private static void RegisterModules(PlatformKernel platform, string? projectRoot)
    {
        var root = projectRoot ?? PlatformPathResolver.FindProjectRoot();
        var modulesRoot = Path.Combine(root, "src", "Modules");
        var loader = new ModuleManifestLoader();
        var loadResult = loader.LoadFromModulesDirectory(modulesRoot);
        var fallbackManifests = CreateModuleManifests();

        foreach (var error in loadResult.Errors)
        {
            platform.EventBus.Publish(
                "module.manifest.load_failed",
                error.Message,
                new Dictionary<string, string> { ["path"] = error.Path });
            platform.AuditLog.Record("module", "ModuleManifestLoader", "load_failed", $"{error.Path}: {error.Message}");
        }

        foreach (var manifest in loadResult.Manifests)
        {
            platform.ModuleRegistry.Register(manifest, "yaml");
        }

        foreach (var fallback in fallbackManifests.Where(fallback => platform.ModuleRegistry.GetByName(fallback.Name) is null))
        {
            platform.ModuleRegistry.Register(fallback, "fallback");
            platform.EventBus.Publish(
                "module.manifest.fallback_used",
                $"Fallback manifest registered for {fallback.Name}.",
                new Dictionary<string, string> { ["module"] = fallback.Name });
            platform.AuditLog.Record("module", "bootstrapper", "fallback_registered", $"Fallback manifest registered for {fallback.Name}.");
        }

        platform.AuditLog.Record("module", "bootstrapper", "registered", "Registered module manifests from yaml with fallback support.");
    }

    private static void RegisterAgents(PlatformKernel platform, Func<string, IAgent>? runtimeAgentFactory)
    {
        if (runtimeAgentFactory is not null)
        {
            foreach (var agentId in new[]
                     {
                         "chief-engineer",
                         "mechanical-designer",
                         "cad-modeler",
                         "drawing-engineer",
                         "drawing-reviewer",
                         "code-engineer",
                         "code-reviewer",
                         "error-diagnosis"
                     })
            {
                platform.AgentRegistry.Register(runtimeAgentFactory(agentId));
            }

            platform.AuditLog.Record("agent", "bootstrapper", "runtime_registered", "Registered agents from runtime agent factory.");
            return;
        }

        var internalAgentRouter = new InternalAgentRouter(platform.AgentRegistry, platform.AuditLog);
        var chiefEngineerOrchestrator = new ChiefEngineerOrchestrator(
            internalAgentRouter,
            platform.AgentRegistry,
            platform.AuditLog,
            platform.WorkflowEngine,
            new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine),
            internalWorkflowRoute: InternalWorkflowRoute.EngineeringDefault);

        platform.AgentRegistry.Register(new ChiefEngineerAgent(chiefEngineerOrchestrator));
        platform.AgentRegistry.Register(new MechanicalDesignerAgent());
        platform.AgentRegistry.Register(new CadModelerAgent());
        platform.AgentRegistry.Register(new DrawingEngineerAgent());
        platform.AgentRegistry.Register(new DrawingReviewerAgent());
        platform.AgentRegistry.Register(new CodeEngineerAgent());
        platform.AgentRegistry.Register(new CodeReviewerAgent());
        platform.AgentRegistry.Register(new ErrorDiagnosisAgent());

        platform.AuditLog.Record("agent", "bootstrapper", "registered", "Registered module agent implementations.");
    }

    private static void RegisterSkills(PlatformKernel platform)
    {
        platform.SkillRegistry.Register(new PlaceholderSkill(
            "requirement-to-cad-model-spec",
            "Convert natural-language requirements into CADModelSpec placeholders."));

        platform.SkillRegistry.Register(new PlaceholderSkill(
            "build-spec-planning",
            "Plan structured BuildSpec placeholders without CAD execution."));

        platform.SkillRegistry.Register(new SolidWorksBuildPlanSkill());

        platform.SkillRegistry.Register(new PlaceholderSkill(
            "drawing-review-summary",
            "Summarize drawing review inputs into ReviewReport placeholders."));

        platform.AuditLog.Record("skill", "bootstrapper", "registered", "Registered base skill placeholders.");
    }

    private static void RegisterWorkers(PlatformKernel platform, string? projectRoot)
    {
        var root = projectRoot ?? PlatformPathResolver.FindProjectRoot();
        var solidWorksWorker = TryCreateWorker(
            root,
            "SolidWorksWorker",
            "SolidWorksWorker.FakeSolidWorksWorker");

        if (solidWorksWorker is not null)
        {
            platform.WorkerRegistry.Register(solidWorksWorker);
        }
        else
        {
            platform.WorkerRegistry.Register(new PlaceholderWorker("FakeSolidWorksWorker", "SolidWorks"));
            platform.AuditLog.Record(
                "worker",
                "bootstrapper",
                "fallback_registered",
                "FakeSolidWorksWorker could not be loaded; registered PlaceholderWorker fallback.");
        }

        platform.WorkerRegistry.Register(new PlaceholderWorker("FakeAutoCADWorker", "AutoCAD"));

        var message = solidWorksWorker is not null
            ? "Registered SolidWorks dry-run worker and AutoCAD placeholder worker."
            : "Registered PlaceholderWorker fallback for SolidWorks and AutoCAD placeholder worker.";
        platform.AuditLog.Record("worker", "bootstrapper", "registered", message);
    }

    private static IWorker? TryCreateWorker(string projectRoot, string assemblyName, string typeName)
    {
        foreach (var assembly in CandidateAssemblies(projectRoot, assemblyName))
        {
            var workerType = assembly.GetType(typeName, throwOnError: false);
            if (workerType is null)
            {
                continue;
            }

            if (Activator.CreateInstance(workerType) is IWorker worker)
            {
                return worker;
            }
        }

        return null;
    }

    private static IEnumerable<Assembly> CandidateAssemblies(string projectRoot, string assemblyName)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == assemblyName);
        if (loaded is not null)
        {
            yield return loaded;
        }

        foreach (var path in CandidateAssemblyPaths(projectRoot, assemblyName))
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(path);
            }
            catch (Exception ex) when (ex is IOException or FileLoadException or FileNotFoundException or BadImageFormatException)
            {
                continue;
            }

            yield return assembly;
        }
    }

    private static IEnumerable<string> CandidateAssemblyPaths(string projectRoot, string assemblyName)
    {
        var fileName = $"{assemblyName}.dll";
        var baseDirectoryPath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(baseDirectoryPath))
        {
            yield return baseDirectoryPath;
        }

        var workerRoot = Path.Combine(projectRoot, "src", "Workers");
        if (!Directory.Exists(workerRoot))
        {
            yield break;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        };
        foreach (var path in Directory
                     .EnumerateFiles(workerRoot, fileName, options)
                     .Where(path => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("bin", StringComparer.OrdinalIgnoreCase))
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            yield return path;
        }
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
