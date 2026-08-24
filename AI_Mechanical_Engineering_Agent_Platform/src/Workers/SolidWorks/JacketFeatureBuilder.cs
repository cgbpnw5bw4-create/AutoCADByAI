using DomainSchemas;
using SolidWorksWorker.Features;

namespace SolidWorksWorker;

public sealed class JacketFeatureBuilder : SolidWorksPartFamilyBuilderBase
{
    private const double MmToMeters = 0.001;
    public JacketFeatureBuilder(
        ISolidWorksComFacade? comFacade = null,
        ISolidWorksFileVerifier? fileVerifier = null,
        ISolidWorksPartFamilyPlaneSelector? planeSelector = null,
        ISolidWorksGeometryReader? geometryReader = null)
        : base(comFacade, fileVerifier, planeSelector, geometryReader)
    {
    }

    /// <summary>
    /// 几何期望委托给零件族 Definition。校验本身由基类的平台级后置阶段执行，
    /// 这里不再保留第二份实现。
    /// </summary>
    protected override ExpectedPartGeometry? DescribeExpectedGeometry(SolidWorksBuildPlan plan) =>
        new JacketBasicDefinition().DescribeExpectedGeometry(plan);

    public override string PartType => JacketBasicDefinition.Type;
    public override string FailureStage => PartFamilyFailureStages.JacketBuildFailed;
    public override bool SupportsRealExecution => false;
    public override string ApiEvidence => JacketBasicDefinition.ProductionEvidenceStatus;
    public override string RealExecutionMode => PartFamilyExecutionModes.GenericFeatureGraph;

    protected override void BuildFeatures(
        object model,
        SolidWorksBuildPlan plan,
        SolidWorksPartFamilyBuildDiagnostics diagnostics,
        List<string> logs)
    {
        var dimensions = plan.Dimensions ?? throw Failure(
            PartFamilyFailureStages.JacketProfileCreateFailed,
            "jacket dimensions are missing from BuildPlan.");
        var outerDiameter = RequiredPositive(
            dimensions,
            "outer_diameter_mm",
            PartFamilyFailureStages.JacketProfileCreateFailed);
        var innerDiameter = RequiredPositive(
            dimensions,
            "inner_diameter_mm",
            PartFamilyFailureStages.JacketInnerCutFailed);
        var length = RequiredPositive(
            dimensions,
            "length_mm",
            PartFamilyFailureStages.JacketExtrudeFailed);

        diagnostics.OperationsExecuted.Add("jacket_profile_create_started");
        SelectPlane(model, PartFamilyFailureStages.JacketProfileCreateFailed);
        var sketchManager = Com.GetProperty(model, "SketchManager");
        Com.Invoke(sketchManager, "InsertSketch", true);
        Require(
            Com.Invoke(sketchManager, "CreateCircle", 0d, 0d, 0d, outerDiameter * MmToMeters / 2d, 0d, 0d),
            PartFamilyFailureStages.JacketProfileCreateFailed,
            "CreateCircle returned null for the jacket outer profile.");
        diagnostics.OperationsExecuted.Add("jacket_profile_create_success");

        diagnostics.OperationsExecuted.Add("jacket_extrude_started");
        var featureManager = Com.GetProperty(model, "FeatureManager");
        Require(
            Com.InvokeWithArgs(
                featureManager,
                "FeatureExtrusion2",
                FlangeFeatureBuilder.FeatureExtrusion2Arguments(length * MmToMeters)),
            PartFamilyFailureStages.JacketExtrudeFailed,
            "FeatureExtrusion2 returned null for the jacket body.");
        diagnostics.OperationsExecuted.Add("jacket_extrude_success");

        diagnostics.OperationsExecuted.Add("jacket_inner_cut_started");
        SelectPlane(model, PartFamilyFailureStages.JacketInnerCutFailed);
        sketchManager = Com.GetProperty(model, "SketchManager");
        Com.Invoke(sketchManager, "InsertSketch", true);
        Require(
            Com.Invoke(sketchManager, "CreateCircle", 0d, 0d, 0d, innerDiameter * MmToMeters / 2d, 0d, 0d),
            PartFamilyFailureStages.JacketInnerCutFailed,
            "CreateCircle returned null for the jacket bore.");
        Require(
            Com.InvokeWithArgs(
                featureManager,
                "FeatureCut4",
                FlangeFeatureBuilder.ActiveSketchFeatureCut4Arguments(length * MmToMeters * 2d)),
            PartFamilyFailureStages.JacketInnerCutFailed,
            "FeatureCut4 returned null for the jacket bore.");
        diagnostics.OperationsExecuted.Add("jacket_inner_cut_success");

        logs.Add($"jacket_cylindrical_shell_created: outer={outerDiameter} mm, inner={innerDiameter} mm, length={length} mm");
    }
}
