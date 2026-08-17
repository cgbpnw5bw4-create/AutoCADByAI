using DomainSchemas;

namespace SolidWorksWorker.Features;

/// <summary>
/// V2.0-D builder for an already-authorized generic FeatureGraph. It adds a
/// post-rebuild geometry read/report after the same Handler pipeline has built
/// the model; it neither introduces a Feature type nor routes around handlers.
/// </summary>
public sealed class V20DFeatureGraphPartFamilyBuilder : SolidWorksPartFamilyBuilderBase
{
    private readonly string _partType;
    private readonly FeatureHandlerRegistry _registry;
    private readonly SolidWorksWorkerRequest _request;
    private readonly SolidWorksRuntimeOptions _options;

    public V20DFeatureGraphPartFamilyBuilder(
        string partType,
        FeatureHandlerRegistry registry,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        ISolidWorksComFacade? comFacade = null,
        ISolidWorksFileVerifier? fileVerifier = null,
        ISolidWorksPartFamilyPlaneSelector? planeSelector = null)
        : base(comFacade, fileVerifier, planeSelector)
    {
        _partType = string.IsNullOrWhiteSpace(partType) ? "generic_cad_model" : partType;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public override string PartType => _partType;

    public override string FailureStage => PartFamilyFailureStages.GeometryReadFailed;

    public override bool SupportsRealExecution => true;

    public override string ApiEvidence =>
        "v2_0_d_geometry_reader:GetPartBox_GetBodies2_GetVertices_GetPoint_GetMassProperties_GetExtremePoint_fallback_GetFaces_CylinderParams; " +
        $"v2_0_d_three_circle_cut_profile={V20DThreeCircleCutEvidencePolicy.EvidenceId}; handler evidence remains enforced per node";

    public override string RealExecutionMode => PartFamilyExecutionModes.GenericFeatureGraph;

    protected override void BuildFeatures(
        object model,
        SolidWorksBuildPlan plan,
        SolidWorksPartFamilyBuildDiagnostics diagnostics,
        List<string> logs)
    {
        var pipeline = new FeatureExecutionPipeline(
            _registry,
            currentModel => new RealSolidWorksFeatureAdapter(currentModel, Com));
        var pipelineResult = pipeline.Execute(model, plan, diagnostics, logs);
        if (!pipelineResult.IsSuccess)
        {
            throw Failure(
                pipelineResult.FailureStage ?? PartFamilyFailureStages.FeatureResultInvalid,
                pipelineResult.Issues.FirstOrDefault() ?? "FeatureExecutionPipeline failed.");
        }

        var geometryPath = GeometryReportPath();
        GeometryValidationReport report;
        try
        {
            var measurement = new RealSolidWorksGeometryReader(Com).Read(model);
            report = new GeometryValidator().Validate(
                plan,
                measurement.Geometry,
                pipelineResult.Reports.Select(item => item.FeatureType).ToArray());
            GeometryValidator.WriteReport(geometryPath, report);
            diagnostics.OperationsExecuted.Add($"geometry_validation_report_written:{geometryPath}");
            logs.Add($"geometry_validation_status:{report.FinalStatus}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw Failure(
                PartFamilyFailureStages.GeometryReportFailed,
                $"geometry_validation_report.json could not be written or completed: {exception.GetBaseException().Message}");
        }

        if (!string.Equals(report.FinalStatus, "Passed", StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                report.FailureStage ?? PartFamilyFailureStages.GeometryReadFailed,
                report.FailedChecks.FirstOrDefault() ?? "Geometry validation did not pass.");
        }
    }

    private string GeometryReportPath() => Path.Combine(
        SolidWorksPartFamilyBuildOutput.ResolveOutputDirectory(_request, _options, PartType),
        "geometry_validation_report.json");
}
