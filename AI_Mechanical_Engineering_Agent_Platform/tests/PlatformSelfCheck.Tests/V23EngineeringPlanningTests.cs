using System.Text.Json;
using System.Text.Json.Nodes;
using AgentContracts;
using AgentGatewayHost;
using AgentRuntime.Microsoft;
using DomainSchemas;
using ModelRuntime;
using PlatformCore;

namespace PlatformSelfCheck.Tests;

public sealed class V23EngineeringPlanningTests
{
    private const string Requirement = EngineeringPlanningSelfCheck.Requirement;

    [Fact]
    public void AcceptedPlanUsesExistingCadSchemaAndDeterministicFeatureGraph()
    {
        var plan = EngineeringPlanParser.Parse(ValidJson());
        var first = new EngineeringPlanValidator().Validate(plan, Requirement, "v23-mapping");
        var second = new EngineeringPlanValidator().Validate(plan, Requirement, "v23-mapping");
        Assert.True(first.IsValid, string.Join("; ", first.Issues));
        Assert.IsType<CADModelSpec>(first.ModelSpec);
        Assert.Equal("jacket_basic", first.ModelSpec!.ModelType);
        Assert.True(first.ModelSpec.RequiresFeatureHandlerPipeline);
        Assert.NotEmpty(first.ModelSpec.Sketches);
        Assert.NotEmpty(first.ModelSpec.Features);
        Assert.Equal(SolidWorksBuildExecutionStrategies.FeatureHandlerGraph, first.BuildPlan!.ExecutionStrategy);
        Assert.Equal(JsonSerializer.Serialize(first.ModelSpec), JsonSerializer.Serialize(second.ModelSpec));
        Assert.Equal(JsonSerializer.Serialize(first.BuildPlan), JsonSerializer.Serialize(second.BuildPlan));
        Assert.Empty(plan.CadModelSpec!.Features);
        Assert.Empty(plan.CadModelSpec.ExecutionOptions);
    }

    [Fact]
    public void EngineeringPlanArtifactRoundTripsThroughStrictParser()
    {
        var original = EngineeringPlanParser.Parse(ValidJson());
        var reparsed = EngineeringPlanParser.Parse(EngineeringPlanParser.Serialize(original));
        Assert.Equal(original.Requirement, reparsed.Requirement);
        Assert.Equal(original.CadModelSpec!.Parameters.OrderBy(item => item.Key), reparsed.CadModelSpec!.Parameters.OrderBy(item => item.Key));
        Assert.True(new EngineeringPlanValidator().Validate(reparsed, Requirement, "roundtrip").IsValid);
    }

    [Fact]
    public void EquivalentNumericTextMapsToExactEvidenceProfile()
    {
        var root = JsonNode.Parse(ValidJson())!.AsObject();
        root["cad_model_spec"]!["parameters"]!["outer_diameter_mm"] = "140.0";
        root["cad_model_spec"]!["parameters"]!["length_mm"] = "1.8e2";
        var result = new EngineeringPlanValidator().Validate(EngineeringPlanParser.Parse(root.ToJsonString()), Requirement, "numeric-profile");
        Assert.True(result.IsValid, string.Join("; ", result.Issues));
        Assert.Equal("140", result.ModelSpec!.Parameters["outer_diameter_mm"]);
        Assert.Equal("180", result.BuildPlan!.Dimensions!["length_mm"]);
    }

    [Theory]
    [InlineData("missing_top_field")]
    [InlineData("unknown_top_field")]
    [InlineData("unknown_cad_field")]
    [InlineData("execution_options")]
    [InlineData("model_features")]
    [InlineData("legacy_alias")]
    [InlineData("missing_unit")]
    [InlineData("numeric_parameter")]
    [InlineData("null_risks")]
    [InlineData("blank_risk")]
    [InlineData("non_string_output")]
    public void MalformedOrUnauthorizedStructuresAreRejected(string scenario)
    {
        var root = JsonNode.Parse(ValidJson())!.AsObject();
        var cad = root["cad_model_spec"]!.AsObject();
        switch (scenario)
        {
            case "missing_top_field": root.Remove("assumptions"); break;
            case "unknown_top_field": root["api_evidence"] = "verified"; break;
            case "unknown_cad_field": cad["api"] = "FeatureExtrusion3"; break;
            case "execution_options": cad["execution_options"] = new JsonObject { ["dry_run"] = "false" }; break;
            case "model_features": cad["features"] = new JsonArray(); break;
            case "legacy_alias": cad["part_type"] = "jacket_basic"; cad.Remove("model_type"); break;
            case "missing_unit": cad.Remove("unit"); break;
            case "numeric_parameter": cad["parameters"]!["length_mm"] = 180; break;
            case "null_risks": root["risks"] = null; break;
            case "blank_risk": root["risks"] = new JsonArray(""); break;
            case "non_string_output": cad["output_requirements"] = new JsonArray(12); break;
        }
        Assert.ThrowsAny<JsonException>(() => EngineeringPlanParser.Parse(root.ToJsonString()));
    }

    [Theory]
    [InlineData("duplicate_top")]
    [InlineData("duplicate_parameter")]
    [InlineData("markdown_wrapper")]
    [InlineData("oversized")]
    public void AmbiguousOrUnboundedResponsesAreRejected(string scenario)
    {
        var json = ValidJson();
        json = scenario switch
        {
            "duplicate_top" => json.Replace("\"schema_version\":\"2.3\"", "\"schema_version\":\"2.3\",\"schema_version\":\"2.3\"", StringComparison.Ordinal),
            "duplicate_parameter" => json.Replace("\"length_mm\":\"180\"", "\"length_mm\":\"180\",\"LENGTH_MM\":\"180\"", StringComparison.Ordinal),
            "markdown_wrapper" => "```json\n" + json + "\n```",
            "oversized" => new string(' ', 65537),
            _ => json
        };
        Assert.ThrowsAny<JsonException>(() => EngineeringPlanParser.Parse(json));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("rationale")]
    [InlineData("reject")]
    [InlineData("invented_decision")]
    [InlineData("source_mismatch")]
    [InlineData("assumption")]
    [InlineData("missing_parameter_note")]
    [InlineData("risk")]
    [InlineData("null_model")]
    [InlineData("unsupported_family")]
    [InlineData("unit")]
    [InlineData("missing_dimension")]
    [InlineData("unknown_dimension")]
    [InlineData("nan")]
    [InlineData("negative")]
    [InlineData("inner_exceeds_outer")]
    [InlineData("unverified_dimensions")]
    [InlineData("material")]
    [InlineData("missing_output")]
    [InlineData("extra_output")]
    [InlineData("unsafe_model_id")]
    public void InvalidEngineeringPlansReturnNoExecutableCad(string scenario)
    {
        var root = JsonNode.Parse(ValidJson())!.AsObject();
        var cad = root["cad_model_spec"]!.AsObject();
        var parameters = cad["parameters"]!.AsObject();
        switch (scenario)
        {
            case "schema": root["schema_version"] = "2.2"; break;
            case "rationale": root["rationale"] = " "; break;
            case "reject": root["decision"] = "reject"; break;
            case "invented_decision": root["decision"] = "approved"; break;
            case "source_mismatch": root["requirement"] = "建模夹套"; break;
            case "assumption": root["assumptions"] = new JsonArray("假定用户接受默认尺寸"); break;
            case "missing_parameter_note": root["missing_parameters"] = new JsonArray("长度"); break;
            case "risk": root["risks"] = new JsonArray("承压能力未校验"); break;
            case "null_model": root["cad_model_spec"] = null; break;
            case "unsupported_family": cad["model_type"] = "shaft_basic"; break;
            case "unit": cad["unit"] = "inch"; break;
            case "missing_dimension": parameters.Remove("length_mm"); break;
            case "unknown_dimension": parameters["wall_thickness_mm"] = "10"; break;
            case "nan": parameters["length_mm"] = "NaN"; break;
            case "negative": parameters["length_mm"] = "-180"; break;
            case "inner_exceeds_outer": parameters["inner_diameter_mm"] = "160"; break;
            case "unverified_dimensions": parameters["outer_diameter_mm"] = "150"; break;
            case "material": cad["material"] = "Q235"; break;
            case "missing_output": cad["output_requirements"] = new JsonArray("SLDPRT"); break;
            case "extra_output": cad["output_requirements"] = new JsonArray("SLDPRT", "STEP", "SLDDRW"); break;
            case "unsafe_model_id": cad["model_id"] = "../escape"; break;
        }
        var result = new EngineeringPlanValidator().Validate(EngineeringPlanParser.Parse(root.ToJsonString()), Requirement, "invalid-v23");
        Assert.False(result.IsValid);
        Assert.Null(result.ModelSpec);
        Assert.Null(result.BuildPlan);
        Assert.NotEmpty(result.Issues);
    }

    [Theory]
    [InlineData("建模圆筒夹套，外径140 mm，内径120 mm。输出SLDPRT和STEP。")]
    [InlineData("建模圆筒夹套，外径140 mm，内径120 mm，轴向长度180。输出SLDPRT和STEP。")]
    [InlineData("建模圆筒夹套，外径140 mm，外径150 mm，内径120 mm，轴向长度180 mm。输出SLDPRT和STEP。")]
    [InlineData("建模圆筒夹套，外径140 mm，内径120 mm，轴向长度180 mm。输出SLDPRT和STEP。承压10 MPa。")]
    [InlineData("建模圆筒夹套，外径140 mm，内径120 mm，轴向长度180 mm。输出SLDPRT和STEP。添加螺纹孔。")]
    public void PlatformCrossChecksOriginalRequirementInsteadOfTrustingReady(string requirement)
    {
        var plan = EngineeringPlanParser.Parse(ValidJson(requirement));
        var result = new EngineeringPlanValidator().Validate(plan, requirement, "source-cross-check");
        Assert.False(result.IsValid);
        Assert.Null(result.BuildPlan);
    }

    [Fact]
    public async Task GatewayRunsOneModelCallAndExistingFakeCadQualityLoop()
    {
        var projectRoot = ProjectRoot();
        var outputDirectory = TemporaryOutput();
        var provider = new ProbeProvider(ValidJson());
        var platform = PlatformBootstrapper.CreateDefault(projectRoot);
        platform.EngineeringPlanningRuntime = new ModelRuntime.ModelRuntime(provider);
        var response = await new AgentMessageDispatcher(platform).DispatchAsync("chief-engineer", Request(projectRoot, outputDirectory));
        Assert.NotNull(response);
        Assert.Equal("completed", response.Status);
        Assert.Equal(GateDecisionResult.Passed, response.GateDecision.Result);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(EngineeringModelPurpose.DesignPlanning, provider.Request!.Purpose);
        Assert.Equal(Requirement, provider.Request.UserMessage);
        Assert.Contains(response.Artifacts, artifact => artifact.Kind == "EngineeringPlan" && File.Exists(artifact.Path));
        var savedPlan = EngineeringPlanParser.Parse(File.ReadAllText(response.Artifacts.Single(artifact => artifact.Kind == "EngineeringPlan").Path));
        Assert.True(new EngineeringPlanValidator().Validate(savedPlan, Requirement, "saved-artifact").IsValid);
        Assert.Contains(response.Artifacts, artifact => artifact.Kind == "MappedCADModelSpec" && File.Exists(artifact.Path));
        Assert.Contains(response.CollaborationReport!.StepResults!, step => step.StepId == EngineeringPlanningStep.Id);
        Assert.Contains(platform.AuditLog.GetEntries(), entry => entry.Action.Contains("quality_gate", StringComparison.Ordinal));
        Assert.DoesNotContain(platform.AuditLog.GetEntries(), entry => entry.Action == "real_cad_connected");
        Assert.Contains(response.Artifacts, artifact => artifact.Path.EndsWith(".SLDPRT.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.Artifacts, artifact => artifact.Path.EndsWith(".STEP.txt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(response.Artifacts, artifact => artifact.Path.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConfiguredModelProviderIsReusedForPlanningWithoutSecondAdvisoryCall()
    {
        var provider = new ProbeProvider(ValidJson());
        var platform = PlatformBootstrapper.CreateDefault(ProjectRoot());
        RuntimePlatformFactory.ApplyRuntimeConfiguration(platform,
            new RuntimeConfiguration(AgentRuntimeMode.Microsoft, AgentRuntimeMode.Microsoft,
                "injected-provider", "configured-model", null, null, null, 60, false, null, false), modelProvider: provider);
        var response = await new AgentMessageDispatcher(platform).DispatchAsync("chief-engineer", Request(ProjectRoot(), TemporaryOutput()));
        Assert.Equal("completed", response!.Status);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(EngineeringModelPurpose.DesignPlanning, provider.Request!.Purpose);
        Assert.Equal(Requirement, provider.Request.UserMessage);
        Assert.True(response.ChiefEngineerRuntimeUsed);
        Assert.Equal("Microsoft", response.RuntimeMode);
        Assert.False(response.RuntimeFallbackUsed);
        Assert.Contains(response.Artifacts, artifact => artifact.Kind == "EngineeringModelInvocationReport");
    }

    [Fact]
    public async Task ReorderedChineseRequirementPreservesItsExplicitDimensions()
    {
        const string requirement = "创建直筒夹套内径120毫米外径140毫米长度180毫米输出STEP与SLDPRT";
        var provider = new ProbeProvider(ValidJson(requirement));
        var platform = PlatformBootstrapper.CreateDefault(ProjectRoot());
        platform.EngineeringPlanningRuntime = new ModelRuntime.ModelRuntime(provider);
        var response = await new AgentMessageDispatcher(platform).DispatchAsync("chief-engineer",
            Request(ProjectRoot(), TemporaryOutput()) with { Message = requirement });
        Assert.Equal("completed", response!.Status);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(requirement, provider.Request!.UserMessage);
        var plan = EngineeringPlanParser.Parse(File.ReadAllText(response.Artifacts.Single(artifact => artifact.Kind == "EngineeringPlan").Path));
        Assert.Equal(requirement, plan.Requirement);
        Assert.Equal("140", plan.CadModelSpec!.Parameters["outer_diameter_mm"]);
        Assert.Equal("120", plan.CadModelSpec.Parameters["inner_diameter_mm"]);
        Assert.Equal("180", plan.CadModelSpec.Parameters["length_mm"]);
        Assert.True(new EngineeringPlanValidator().Validate(plan, requirement, "reordered-source").IsValid);
    }

    [Theory]
    [InlineData("{\"decision\":\"ready\"}")]
    [InlineData("建议直接调用 SolidWorks API")]
    public async Task MalformedModelOutputCannotReachCad(string responseText)
    {
        var provider = new ProbeProvider(responseText);
        var platform = PlatformBootstrapper.CreateDefault(ProjectRoot());
        platform.EngineeringPlanningRuntime = new ModelRuntime.ModelRuntime(provider);
        var response = await new AgentMessageDispatcher(platform).DispatchAsync("chief-engineer", Request(ProjectRoot(), TemporaryOutput()));
        Assert.NotEqual("completed", response!.Status);
        Assert.Equal(1, provider.Calls);
        Assert.All(response.CollaborationReport!.StepResults!, step => Assert.Equal(EngineeringPlanningStep.Id, step.StepId));
        Assert.DoesNotContain(response.Artifacts, artifact => artifact.Path.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(response.Issues);
    }

    [Theory]
    [InlineData("risk")]
    [InlineData("missing_dimension")]
    public async Task StructurallyValidUnsafePlanCannotReachCad(string scenario)
    {
        var plan = JsonNode.Parse(ValidJson())!.AsObject();
        if (scenario == "risk") plan["risks"] = new JsonArray("工程风险尚未解决");
        else plan["cad_model_spec"]!["parameters"]!.AsObject().Remove("length_mm");
        var provider = new ProbeProvider(plan.ToJsonString());
        var platform = PlatformBootstrapper.CreateDefault(ProjectRoot());
        platform.EngineeringPlanningRuntime = new ModelRuntime.ModelRuntime(provider);
        var response = await new AgentMessageDispatcher(platform).DispatchAsync("chief-engineer", Request(ProjectRoot(), TemporaryOutput()));
        Assert.NotEqual("completed", response!.Status);
        Assert.Equal(1, provider.Calls);
        Assert.Contains(response.Artifacts, artifact => artifact.Kind == "EngineeringPlan");
        Assert.DoesNotContain(response.Artifacts, artifact => artifact.Kind == "MappedCADModelSpec" ||
            artifact.Path.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase));
        Assert.All(response.CollaborationReport!.StepResults!, step => Assert.Equal(EngineeringPlanningStep.Id, step.StepId));
        Assert.NotEmpty(response.Issues);
    }

    [Fact]
    public async Task MissingRuntimeAndProviderFailureStopBeforeWorker()
    {
        foreach (var provider in new ProbeProvider?[] { null, new(ValidJson()) { Failure = new InvalidOperationException("provider_failed") } })
        {
            var platform = PlatformBootstrapper.CreateDefault(ProjectRoot());
            if (provider is not null) platform.EngineeringPlanningRuntime = new ModelRuntime.ModelRuntime(provider);
            var response = await new AgentMessageDispatcher(platform).DispatchAsync("chief-engineer", Request(ProjectRoot(), TemporaryOutput()));
            Assert.NotEqual("completed", response!.Status);
            Assert.DoesNotContain(response.Artifacts, artifact => artifact.Kind is "EngineeringPlan" or "EngineeringModelResponse" or
                "MappedCADModelSpec" or "SolidWorksBuildPlan" || artifact.Path.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase));
            Assert.All(response.CollaborationReport!.StepResults!, step => Assert.Equal(EngineeringPlanningStep.Id, step.StepId));
            Assert.NotEmpty(response.Issues);
            if (provider is not null) Assert.Equal(1, provider.Calls);
        }
    }

    [Fact]
    public async Task SelfCheckObservesEveryV23BoundaryWithNoLiveProvider()
    {
        var checks = await EngineeringPlanningSelfCheck.RunAsync(ProjectRoot(), TemporaryOutput());
        Assert.Equal(5, checks.Count);
        Assert.All(checks, check => Assert.True(check.Value, check.Key));
    }

    [Theory]
    [InlineData("cad_model_spec_json")]
    [InlineData("CAD_MODEL_SPEC_JSON")]
    [InlineData("PART_TYPE")]
    [InlineData("OPERATION")]
    [InlineData("CAD_MODEL_TYPE")]
    [InlineData("PARAMETER_UPDATE_JSON")]
    [InlineData("SOLIDWORKS_MODEL_TYPE")]
    [InlineData("PART_NAME")]
    [InlineData("SOLIDWORKS_PART_NAME")]
    [InlineData("CAD_MODEL_NAME")]
    public async Task ConflictingCadInputsAreRejectedBeforeModelCall(string key)
    {
        var provider = new ProbeProvider(ValidJson());
        var platform = PlatformBootstrapper.CreateDefault(ProjectRoot());
        platform.EngineeringPlanningRuntime = new ModelRuntime.ModelRuntime(provider);
        var request = Request(ProjectRoot(), TemporaryOutput());
        var value = key.Equals("cad_model_spec_json", StringComparison.OrdinalIgnoreCase)
            ? JsonSerializer.Serialize(EngineeringPlanParser.Parse(ValidJson()).CadModelSpec) : "jacket_basic";
        var values = new Dictionary<string, string>(request.Context) { [key] = value };
        var response = await new AgentMessageDispatcher(platform).DispatchAsync("chief-engineer", request with { Context = values });
        Assert.NotEqual("completed", response!.Status);
        Assert.Equal(0, provider.Calls);
        Assert.Contains(response.Issues, issue => issue.Contains("engineering_plan_input_conflict", StringComparison.Ordinal));
        Assert.All(response.CollaborationReport!.StepResults!, step => Assert.Equal(EngineeringPlanningStep.Id, step.StepId));
    }

    [Theory]
    [InlineData("cad_model_spec")]
    [InlineData("CAD_MODEL_SPEC")]
    [InlineData("SOLIDWORKS_MODEL_SPEC")]
    [InlineData("MODEL_SPEC")]
    public async Task ConflictingSharedCadModelsAreRejectedBeforeModelCall(string key)
    {
        var provider = new ProbeProvider(ValidJson());
        var input = new AgentInput("test", "local", "shared-model-conflict", "test", Requirement, [],
            new Dictionary<string, string> { ["engineering_planning"] = "true", ["dry_run"] = "true" });
        var context = new AgentContext("shared-model-conflict", input,
            new Dictionary<string, object?> { [key] = EngineeringPlanParser.Parse(ValidJson()).CadModelSpec }, DateTimeOffset.UtcNow);
        var step = new EngineeringPlanningStep(context, new ModelRuntime.ModelRuntime(provider), new InMemoryAuditLog());
        var result = await step.ToWorkflowStep().ExecuteAsync(new WorkflowContext(context.TaskId, new Dictionary<string, object?>()));
        Assert.NotEqual(WorkflowStepStatus.Passed, result.Status);
        Assert.Equal(0, provider.Calls);
        Assert.Null(step.ValidatedSpec);
        Assert.Contains(result.Issues, issue => issue.Contains("engineering_plan_input_conflict", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => step.ToCadContext());
    }

    [Fact]
    public void CommittedReplayIsBoundToExactRequirementFile()
    {
        var root = ProjectRoot();
        var requirement = File.ReadAllText(Path.Combine(root, "examples", "v2_3_jacket_requirement.txt"));
        var plan = EngineeringPlanParser.Parse(File.ReadAllText(Path.Combine(root, "examples", "v2_3_jacket_plan_response.json")));
        Assert.Equal(requirement, plan.Requirement);
        var result = new EngineeringPlanValidator().Validate(plan, requirement, "committed-v23");
        Assert.True(result.IsValid, string.Join("; ", result.Issues));
    }

    private static GatewayMessageRequest Request(string projectRoot, string outputDirectory) =>
        new("test", "local", "v23-" + Guid.NewGuid().ToString("N"), "test", Requirement, [],
            new Dictionary<string, string>
            {
                ["engineering_planning"] = "true", ["dry_run"] = "true", ["project_root"] = projectRoot,
                ["solidworks_output_directory"] = outputDirectory, ["engineering_plan_source"] = "test-probe"
            });

    private static string ValidJson(string requirement = Requirement) => JsonSerializer.Serialize(new
    {
        schema_version = "2.3", requirement, decision = "ready", rationale = "原需求明确且适用已有受证档案。",
        cad_model_spec = new
        {
            model_id = "v23-jacket", model_type = "jacket_basic", unit = "mm",
            parameters = new Dictionary<string, string> { ["outer_diameter_mm"] = "140", ["inner_diameter_mm"] = "120", ["length_mm"] = "180" },
            material = "", output_requirements = new[] { "SLDPRT", "STEP" }
        },
        assumptions = Array.Empty<string>(), missing_parameters = Array.Empty<string>(), risks = Array.Empty<string>()
    });

    private static string TemporaryOutput() => Path.Combine(Path.GetTempPath(), "engineering-v23-tests", Guid.NewGuid().ToString("N"));

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AI_Mechanical_Engineering_Agent_Platform.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("无法定位工程根目录。");
    }

    private sealed class ProbeProvider(string response) : IModelProvider
    {
        public int Calls { get; private set; }
        public ModelRequest? Request { get; private set; }
        public Exception? Failure { get; init; }
        public Task<string> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Request = request;
            return Failure is null ? Task.FromResult(response) : Task.FromException<string>(Failure);
        }
    }
}
