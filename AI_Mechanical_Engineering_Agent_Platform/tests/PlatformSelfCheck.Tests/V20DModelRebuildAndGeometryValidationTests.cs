using DomainSchemas;
using PlatformCore.Modules.CADModeling;
using SolidWorksWorker.Features;
using System.Text.Json;

namespace PlatformSelfCheck.Tests;

public sealed class V20DModelRebuildAndGeometryValidationTests
{
    [Fact]
    public void ModelUpdateServiceRebuildsPlateParametersWithoutChangingFeatureGraph()
    {
        var initial = PlateSpec();
        var oldParameters = new Dictionary<string, string>(initial.Parameters, StringComparer.OrdinalIgnoreCase);

        var result = new ModelUpdateService().Prepare(
            "v20-d-parameter-update",
            initial,
            new ModelParameterUpdateRequest(
                new Dictionary<string, string>
                {
                    ["length_mm"] = "200",
                    ["width_mm"] = "100",
                    ["thickness_mm"] = "15"
                },
                oldParameters));

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Issues));
        Assert.True(result.FeatureGraphPreserved);
        Assert.Equal(oldParameters, result.OldParameters);
        Assert.Equal("200", result.NewParameters["length_mm"]);
        Assert.Equal("100", result.NewParameters["width_mm"]);
        Assert.Equal("15", result.NewParameters["thickness_mm"]);
        Assert.Equal(
            new[] { "length_mm", "thickness_mm", "width_mm" },
            result.ChangedParameters);
        Assert.Equal(
            new[] { "plate_boss", "plate_cut", "plate_hole" },
            result.ChangedFeatures);

        var updated = Assert.IsType<CADModelSpec>(result.UpdatedModelSpec);
        Assert.Equal("200", updated.Parameters["length_mm"]);
        Assert.Equal("100", updated.Parameters["width_mm"]);
        Assert.Equal("15", updated.Parameters["thickness_mm"]);
        Assert.Equal(initial.Sketches.Select(item => item.SketchId), updated.Sketches.Select(item => item.SketchId));
        Assert.Equal(initial.Features.Select(item => item.FeatureId), updated.Features.Select(item => item.FeatureId));
        Assert.Equal(
            initial.Features.Select(item => item.Dependencies),
            updated.Features.Select(item => item.Dependencies));

        var profile = Assert.Single(updated.Sketches, item => item.SketchId == "plate_profile");
        var rectangle = Assert.Single(profile.Entities);
        Assert.Equal("200", rectangle.Parameters["length_mm"]);
        Assert.Equal("100", rectangle.Parameters["width_mm"]);
        Assert.Equal("100", rectangle.Parameters["height_mm"]);
        Assert.Equal("15", updated.Features.Single(item => item.FeatureId == "plate_boss").Parameters["depth_mm"]);
        Assert.Equal("30", updated.Features.Single(item => item.FeatureId == "plate_cut").Parameters["depth_mm"]);
        Assert.Equal("30", updated.Features.Single(item => item.FeatureId == "plate_hole").Parameters["depth_mm"]);
        Assert.NotNull(result.BuildPlan);
        Assert.Equal(SolidWorksBuildExecutionStrategies.FeatureHandlerGraph, result.BuildPlan!.ExecutionStrategy);

        // The source is immutable evidence for the first 160 x 80 x 12 run.
        Assert.Equal("160", initial.Parameters["length_mm"]);
        Assert.Equal("80", initial.Parameters["width_mm"]);
        Assert.Equal("12", initial.Parameters["thickness_mm"]);
    }

    [Fact]
    public void ModelUpdateServiceRebindsExistingHoleDiameterWithoutAddingFeatures()
    {
        var initial = PlateSpec();
        var result = new ModelUpdateService().Prepare(
            "v20-d-hole-diameter-update",
            initial,
            new ModelParameterUpdateRequest(
                new Dictionary<string, string>
                {
                    ["hole_diameter_mm"] = "12"
                }));

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Issues));
        Assert.True(result.FeatureGraphPreserved);
        Assert.Equal(new[] { "hole_diameter_mm" }, result.ChangedParameters);
        Assert.Equal(new[] { "plate_cut", "plate_hole" }, result.ChangedFeatures);

        var updated = Assert.IsType<CADModelSpec>(result.UpdatedModelSpec);
        Assert.Equal("12", updated.Parameters["hole_diameter_mm"]);
        Assert.All(
            updated.Sketches
                .Where(sketch => sketch.SketchId is "cut_profile" or "hole_profile")
                .SelectMany(sketch => sketch.Entities),
            entity =>
            {
                Assert.Equal("6", entity.Parameters["radius_mm"]);
                Assert.Equal("12", entity.Parameters["diameter_mm"]);
            });
        Assert.Equal("12", updated.Features.Single(feature => feature.FeatureId == "plate_hole").Parameters["diameter_mm"]);
    }

    [Fact]
    public void V20DThreeCircleEvidenceProfileAcceptsTheInitialAndUpdatedFeatureGraphs()
    {
        var initial = PlateSpec() with { RequiresFeatureHandlerPipeline = true };
        var initialPlan = new BuildPlanCompiler().Compile("v20-d-three-circle-initial", initial).BuildPlan;
        Assert.NotNull(initialPlan);
        Assert.True(
            V20DThreeCircleCutEvidencePolicy.ValidatePlanProfile(initialPlan!).IsPassed,
            string.Join(Environment.NewLine, V20DThreeCircleCutEvidencePolicy.ValidatePlanProfile(initialPlan).Issues));

        var update = new ModelUpdateService().Prepare(
            "v20-d-three-circle-update",
            initial,
            new ModelParameterUpdateRequest(
                new Dictionary<string, string>
                {
                    ["length_mm"] = "200",
                    ["width_mm"] = "100",
                    ["thickness_mm"] = "15"
                }));
        Assert.True(update.IsSuccess, string.Join(Environment.NewLine, update.Issues));
        Assert.NotNull(update.BuildPlan);

        var updatedEvidence = V20DThreeCircleCutEvidencePolicy.ValidatePlanProfile(update.BuildPlan!);
        Assert.True(updatedEvidence.IsPassed, string.Join(Environment.NewLine, updatedEvidence.Issues));
    }

    [Fact]
    public void V20DThreeCircleEvidenceProfileRejectsACutSketchWithOnlyTwoCircles()
    {
        var initial = PlateSpec() with { RequiresFeatureHandlerPipeline = true };
        var plan = Assert.IsType<SolidWorksBuildPlan>(
            new BuildPlanCompiler().Compile("v20-d-three-circle-reject", initial).BuildPlan);
        var operations = plan.Operations.ToArray();
        var cutSketchIndex = Array.FindIndex(
            operations,
            operation => operation.OperationType == "CreateSketch" &&
                         operation.Parameters.GetValueOrDefault("sketch_id") == "cut_profile");
        Assert.True(cutSketchIndex >= 0);
        var cutSketch = operations[cutSketchIndex];
        var parameters = new Dictionary<string, string>(cutSketch.Parameters, StringComparer.OrdinalIgnoreCase);
        var entities = Assert.IsType<SketchEntity[]>(JsonSerializer.Deserialize<SketchEntity[]>(parameters["entities"]));
        parameters["entities"] = JsonSerializer.Serialize(entities.Take(2).ToArray());
        parameters["entity_count"] = "2";
        operations[cutSketchIndex] = cutSketch with { Parameters = parameters };

        var result = V20DThreeCircleCutEvidencePolicy.ValidatePlanProfile(plan with { Operations = operations });

        Assert.False(result.IsPassed);
        Assert.Equal(PartFamilyFailureStages.FeatureApiUnverified, result.FailureStage);
        Assert.Contains(result.Issues, issue => issue.Contains("three exact cut_profile circles", StringComparison.Ordinal));
    }

    [Fact]
    public void GeometryValidatorAcceptsMeasuredUpdatedPlateGeometry()
    {
        var report = new GeometryValidator().Validate(
            UpdatedPlateSpec(),
            UpdatedPlateMeasurement(),
            ["sketch", FeatureTypes.ExtrudeBoss, FeatureTypes.ExtrudeCut, FeatureTypes.Hole]);

        Assert.Equal("Passed", report.FinalStatus);
        Assert.Null(report.FailureStage);
        Assert.Empty(report.FailedChecks);
        Assert.Contains("bounding_box", report.PassedChecks);
        Assert.Contains("volume_expected_geometry", report.PassedChecks);
        Assert.Contains("parameter_geometry:length_mm", report.PassedChecks);
        Assert.Contains("parameter_geometry:diameter_mm", report.PassedChecks);
        Assert.Contains("feature_result:hole", report.PassedChecks);
    }

    [Fact]
    public void GeometryValidatorFailsClosedWhenSolidWorksRebuildFails()
    {
        var report = new GeometryValidator().Validate(
            UpdatedPlateSpec(),
            UpdatedPlateMeasurement() with { RebuildPassed = false },
            ["sketch", FeatureTypes.ExtrudeBoss, FeatureTypes.ExtrudeCut, FeatureTypes.Hole]);

        Assert.Equal("Failed", report.FinalStatus);
        Assert.Equal(PartFamilyFailureStages.RebuildFailed, report.FailureStage);
        Assert.Contains(report.FailedChecks, check => check.StartsWith("solidworks_rebuild:", StringComparison.Ordinal));
    }

    [Fact]
    public void GeometryValidatorRejectsStaleMeasuredDimensionsAfterParameterUpdate()
    {
        var staleMeasurement = UpdatedPlateMeasurement() with
        {
            ExactExtents = Box(160d, 80d, 12d)
        };

        var report = new GeometryValidator().Validate(
            UpdatedPlateSpec(),
            staleMeasurement,
            ["sketch", FeatureTypes.ExtrudeBoss, FeatureTypes.ExtrudeCut, FeatureTypes.Hole]);

        Assert.Equal("Failed", report.FinalStatus);
        Assert.Equal(PartFamilyFailureStages.ParameterGeometryMismatch, report.FailureStage);
        Assert.Contains(report.FailedChecks, check => check.StartsWith("parameter_geometry:length_mm:", StringComparison.Ordinal));
    }

    [Fact]
    public void GeometryValidatorRejectsUnrelatedCylindricalFacesAsHoleEvidence()
    {
        var measurement = UpdatedPlateMeasurement() with
        {
            Cylinders =
            [
                new MeasuredCylinder(10d, 0d, 0d, 0d, 1d, 0d, 0d),
                new MeasuredCylinder(10d, 0d, 0d, 0d, 1d, 0d, 0d),
                new MeasuredCylinder(10d, 0d, 0d, 0d, 1d, 0d, 0d),
                new MeasuredCylinder(10d, 0d, 0d, 0d, 1d, 0d, 0d)
            ]
        };

        var report = new GeometryValidator().Validate(
            UpdatedPlateSpec(),
            measurement,
            ["sketch", FeatureTypes.ExtrudeBoss, FeatureTypes.ExtrudeCut, FeatureTypes.Hole]);

        Assert.Equal("Failed", report.FinalStatus);
        Assert.Equal(PartFamilyFailureStages.FeatureMissingAfterRebuild, report.FailureStage);
        Assert.Contains(report.FailedChecks, check => check.StartsWith("feature_result:hole:", StringComparison.Ordinal));
    }

    private static CADModelSpec UpdatedPlateSpec() => PlateSpec("200", "100", "15");

    private static CADModelSpec PlateSpec(
        string length = "160",
        string width = "80",
        string thickness = "12") =>
        new(
            "plate-v20-d",
            PlateBasic4HolesDefinition.Type,
            "mm",
            parameters: new Dictionary<string, string>
            {
                ["length_mm"] = length,
                ["width_mm"] = width,
                ["thickness_mm"] = thickness,
                ["hole_count"] = "4",
                ["hole_diameter_mm"] = "10"
            },
            referenceGeometry: new Dictionary<string, string>
            {
                ["primary_plane"] = "TopPlane"
            },
            sketches:
            [
                new SketchDefinition(
                    "plate_profile",
                    "TopPlane",
                    [
                        new SketchEntity(
                            "plate_rectangle",
                            SketchEntityTypes.Rectangle,
                            new Dictionary<string, string>
                            {
                                ["center_x_mm"] = "0",
                                ["center_y_mm"] = "0",
                                ["length_mm"] = length,
                                ["width_mm"] = width,
                                ["height_mm"] = width
                            })
                    ],
                    constraints: [],
                    dimensions: new Dictionary<string, string>(),
                    executionOrder: 1),
                new SketchDefinition(
                    "cut_profile",
                    "TopPlane",
                    [
                        Circle("cut_hole_1", "-60", "-20"),
                        Circle("cut_hole_2", "60", "-20"),
                        Circle("cut_hole_3", "-60", "20")
                    ],
                    constraints: [],
                    dimensions: new Dictionary<string, string>(),
                    executionOrder: 2),
                new SketchDefinition(
                    "hole_profile",
                    "TopPlane",
                    [
                        Circle("hole_4", "60", "20")
                    ],
                    constraints: [],
                    dimensions: new Dictionary<string, string>(),
                    executionOrder: 3)
            ],
            features:
            [
                new FeatureDefinition(
                    "plate_boss",
                    FeatureTypes.ExtrudeBoss,
                    new Dictionary<string, string>
                    {
                        ["depth_mm"] = thickness,
                        ["direction"] = "blind"
                    },
                    dependencies: [],
                    referencedSketches: ["plate_profile"],
                    referencedFeatures: [],
                    executionOrder: 1),
                new FeatureDefinition(
                    "plate_cut",
                    FeatureTypes.ExtrudeCut,
                    new Dictionary<string, string>
                    {
                        ["depth_mm"] = DoubleThickness(thickness),
                        ["through_all"] = "false"
                    },
                    dependencies: ["plate_boss"],
                    referencedSketches: ["cut_profile"],
                    referencedFeatures: ["plate_boss"],
                    executionOrder: 2),
                new FeatureDefinition(
                    "plate_hole",
                    FeatureTypes.Hole,
                    new Dictionary<string, string>
                    {
                        ["diameter_mm"] = "10",
                        ["depth_mm"] = DoubleThickness(thickness),
                        ["strategy"] = "simple_circular_cut_blind"
                    },
                    dependencies: ["plate_cut"],
                    referencedSketches: ["hole_profile"],
                    referencedFeatures: ["plate_cut"],
                    executionOrder: 3)
            ]);

    private static SketchEntity Circle(string id, string x, string y) =>
        new(
            id,
            SketchEntityTypes.Circle,
            new Dictionary<string, string>
            {
                ["center_x_mm"] = x,
                ["center_y_mm"] = y,
                ["radius_mm"] = "5",
                ["diameter_mm"] = "10"
            });

    private static MeasuredGeometry UpdatedPlateMeasurement()
    {
        var volume = 200d * 100d * 15d - 4d * Math.PI * Math.Pow(5d, 2d) * 15d;
        return new MeasuredGeometry(
            RebuildPassed: true,
            BoundingBox: Box(200d, 100d, 15d),
            ExactExtents: Box(200d, 100d, 15d),
            BodyCount: 1,
            VolumeCubicMillimeters: volume,
            MassKilograms: 2.3d,
            MassPropertyVolumeCubicMillimeters: volume,
            // These are the real GetTypeName2 values recorded by the
            // SolidWorks 33.5.0 plate workflow, not display names.
            FeatureTypes: ["ProfileFeature", "Extrusion", "ICE"],
            Features:
            [
                new MeasuredFeature("Sketch1", "ProfileFeature"),
                new MeasuredFeature("Boss-Extrude1", "Extrusion"),
                new MeasuredFeature("Cut-Extrude1", "ICE")
            ],
            CylindricalDiametersMm: [10d, 10d, 10d, 10d],
            Cylinders:
            [
                new MeasuredCylinder(10d, -80d, -30d, 0d, 0d, 0d, 1d),
                new MeasuredCylinder(10d, 80d, -30d, 0d, 0d, 0d, 1d),
                new MeasuredCylinder(10d, -80d, 30d, 0d, 0d, 0d, 1d),
                new MeasuredCylinder(10d, 80d, 30d, 0d, 0d, 0d, 1d)
            ],
            ReadIssues: [],
            SolidWorksVersion: "33.5.0",
            GeometryEvidenceSourceRevision: "unit-test");
    }

    private static GeometryBoundingBox Box(double length, double width, double thickness) =>
        new(0d, 0d, 0d, length, width, thickness);

    private static string DoubleThickness(string thickness) =>
        (double.Parse(thickness, System.Globalization.CultureInfo.InvariantCulture) * 2d)
        .ToString(System.Globalization.CultureInfo.InvariantCulture);
}
