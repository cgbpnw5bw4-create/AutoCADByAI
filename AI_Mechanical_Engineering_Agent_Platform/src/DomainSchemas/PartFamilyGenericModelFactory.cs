using System.Globalization;

namespace DomainSchemas;

/// <summary>
/// Parameterized family templates expressed only through the generic CAD description.
/// The BuildPlan compiler owns all SolidWorks operation generation.
/// </summary>
public static class PartFamilyGenericModelFactory
{
    public static CADModelSpec CreatePlateBasic4Holes(CADModelSpec source)
    {
        var length = Parameter(source, "length_mm");
        var width = Parameter(source, "width_mm");
        var thickness = Parameter(source, "thickness_mm");
        var holeDiameter = Parameter(source, "hole_diameter_mm");
        var holeCount = Parameter(source, "hole_count");
        return Complete(
            source,
            [
                new SketchDefinition(
                    "plate_base_sketch",
                    "TopPlane",
                    [
                        new SketchEntity(
                            "plate_base_rectangle",
                            SketchEntityTypes.Rectangle,
                            new Dictionary<string, string>
                            {
                                ["profile"] = "center_rectangle",
                                ["length_mm"] = length,
                                ["width_mm"] = width
                            })
                    ],
                    [
                        CommonFeatureTemplates.CreateDimensionalConstraint("plate_length", "plate_base_rectangle", "length_mm", length),
                        CommonFeatureTemplates.CreateDimensionalConstraint("plate_width", "plate_base_rectangle", "width_mm", width)
                    ],
                    new Dictionary<string, string>
                    {
                        ["length_mm"] = length,
                        ["width_mm"] = width
                    },
                    executionOrder: 1),
                new SketchDefinition(
                    "plate_hole_sketch",
                    "TopFace",
                    [
                        new SketchEntity(
                            "plate_hole_pattern",
                            SketchEntityTypes.Circle,
                            new Dictionary<string, string>
                            {
                                ["pattern"] = "rectangular",
                                ["hole_count"] = holeCount,
                                ["margin_x_mm"] = "20",
                                ["margin_y_mm"] = "20",
                                ["hole_diameter_mm"] = holeDiameter
                            })
                    ],
                    [
                        CommonFeatureTemplates.CreateDimensionalConstraint("plate_hole_diameter", "plate_hole_pattern", "hole_diameter_mm", holeDiameter)
                    ],
                    new Dictionary<string, string>
                    {
                        ["hole_count"] = holeCount,
                        ["hole_diameter_mm"] = holeDiameter,
                        ["margin_x_mm"] = "20",
                        ["margin_y_mm"] = "20"
                    },
                    executionOrder: 2)
            ],
            [
                new FeatureDefinition(
                    "plate_base_extrude",
                    FeatureTypes.ExtrudeBoss,
                    new Dictionary<string, string>
                    {
                        ["depth_mm"] = thickness,
                        ["direction"] = "mid_plane"
                    },
                    dependencies: [],
                    referencedSketches: ["plate_base_sketch"],
                    referencedFeatures: [],
                    executionOrder: 1),
                new FeatureDefinition(
                    "plate_hole_cut",
                    FeatureTypes.ExtrudeCut,
                    new Dictionary<string, string>
                    {
                        ["hole_diameter_mm"] = holeDiameter,
                        ["through_all"] = "true",
                        ["hole_count"] = holeCount
                    },
                    dependencies: ["plate_base_extrude"],
                    referencedSketches: ["plate_hole_sketch"],
                    referencedFeatures: ["plate_base_extrude"],
                    executionOrder: 2)
            ]);
    }

    public static CADModelSpec CreateFlangeBasic(CADModelSpec source)
    {
        var outerDiameter = Parameter(source, "outer_diameter_mm");
        var innerDiameter = Parameter(source, "inner_diameter_mm");
        var thickness = Parameter(source, "thickness_mm");
        var boltHoleCount = Parameter(source, "bolt_hole_count");
        var boltHoleDiameter = Parameter(source, "bolt_hole_diameter_mm");
        var boltCircleDiameter = Parameter(source, "bolt_circle_diameter_mm");
        return Complete(
            source,
            [
                CommonFeatureTemplates.CreateCircleSketch(
                    "flange_outer_sketch",
                    "flange_outer_circle",
                    "TopPlane",
                    "outer_diameter_mm",
                    outerDiameter,
                    new Dictionary<string, string> { ["profile"] = "outer_circle" },
                    executionOrder: 1),
                CommonFeatureTemplates.CreateCircleSketch(
                    "flange_inner_sketch",
                    "flange_inner_circle",
                    "TopFace",
                    "inner_diameter_mm",
                    innerDiameter,
                    new Dictionary<string, string> { ["profile"] = "center_hole_circle" },
                    executionOrder: 2),
                new SketchDefinition(
                    "flange_bolt_sketch",
                    "TopFace",
                    [
                        new SketchEntity(
                            "flange_bolt_circle",
                            SketchEntityTypes.Circle,
                            new Dictionary<string, string>
                            {
                                ["pattern"] = "bolt_circle",
                                ["bolt_hole_count"] = boltHoleCount,
                                ["bolt_hole_diameter_mm"] = boltHoleDiameter,
                                ["bolt_circle_diameter_mm"] = boltCircleDiameter
                            })
                    ],
                    [
                        CommonFeatureTemplates.CreateDimensionalConstraint("flange_bolt_hole_diameter", "flange_bolt_circle", "bolt_hole_diameter_mm", boltHoleDiameter),
                        CommonFeatureTemplates.CreateDimensionalConstraint("flange_bolt_circle_diameter", "flange_bolt_circle", "bolt_circle_diameter_mm", boltCircleDiameter)
                    ],
                    new Dictionary<string, string>
                    {
                        ["bolt_hole_count"] = boltHoleCount,
                        ["bolt_hole_diameter_mm"] = boltHoleDiameter,
                        ["bolt_circle_diameter_mm"] = boltCircleDiameter
                    },
                    executionOrder: 3)
            ],
            [
                new FeatureDefinition(
                    "flange_body_extrude",
                    FeatureTypes.ExtrudeBoss,
                    new Dictionary<string, string>
                    {
                        ["depth_mm"] = thickness,
                        ["direction"] = "mid_plane"
                    },
                    dependencies: [],
                    referencedSketches: ["flange_outer_sketch"],
                    referencedFeatures: [],
                    executionOrder: 1),
                new FeatureDefinition(
                    "flange_inner_cut",
                    FeatureTypes.ExtrudeCut,
                    new Dictionary<string, string>
                    {
                        ["cut_role"] = "center_hole",
                        ["hole_diameter_mm"] = innerDiameter,
                        ["through_all"] = "true"
                    },
                    dependencies: ["flange_body_extrude"],
                    referencedSketches: ["flange_inner_sketch"],
                    referencedFeatures: ["flange_body_extrude"],
                    executionOrder: 2),
                new FeatureDefinition(
                    "flange_bolt_holes",
                    FeatureTypes.ExtrudeCut,
                    new Dictionary<string, string>
                    {
                        ["cut_role"] = "bolt_holes",
                        ["hole_diameter_mm"] = boltHoleDiameter,
                        ["hole_count"] = boltHoleCount,
                        ["through_all"] = "true"
                    },
                    dependencies: ["flange_inner_cut"],
                    referencedSketches: ["flange_bolt_sketch"],
                    referencedFeatures: ["flange_inner_cut"],
                    executionOrder: 3)
            ]);
    }

    public static CADModelSpec CreateShaftBasic(CADModelSpec source)
    {
        var diameter = Parameter(source, "diameter_mm");
        var length = Parameter(source, "length_mm");
        var stepDiameters = Parameter(source, "optional_step_diameters");
        var stepLengths = Parameter(source, "optional_step_lengths");
        var diameters = ParseList(stepDiameters).Prepend(double.Parse(diameter, CultureInfo.InvariantCulture)).ToArray();
        var lengths = ParseList(stepLengths);
        var totalLength = double.Parse(length, CultureInfo.InvariantCulture);
        var sections = lengths.Prepend(totalLength - lengths.Sum()).ToArray();
        var entities = new List<SketchEntity>();
        var x = 0d;
        var radius = diameters[0] / 2d;
        void Line(double x1, double y1, double x2, double y2) => entities.Add(new(
            $"shaft_profile_line_{entities.Count + 1}",
            SketchEntityTypes.Line,
            new Dictionary<string, string>
            {
                ["x1_mm"] = Number(x1), ["y1_mm"] = Number(y1),
                ["x2_mm"] = Number(x2), ["y2_mm"] = Number(y2)
            }));
        Line(0, 0, 0, radius);
        // 保留旧编译计划的语义元数据；执行几何完全来自下方明确坐标。
        foreach (var pair in new Dictionary<string, string>
        {
            ["profile"] = "closed_half_section", ["diameter_mm"] = diameter, ["length_mm"] = length,
            ["optional_step_diameters"] = stepDiameters, ["optional_step_lengths"] = stepLengths
        })
            ((Dictionary<string, string>)entities[0].Parameters)[pair.Key] = pair.Value;
        for (var index = 0; index < sections.Length; index++)
        {
            Line(x, radius, x + sections[index], radius);
            x += sections[index];
            if (index + 1 < diameters.Length)
            {
                var nextRadius = diameters[index + 1] / 2d;
                if (nextRadius != radius) Line(x, radius, x, nextRadius);
                radius = nextRadius;
            }
        }
        Line(totalLength, radius, totalLength, 0);
        Line(totalLength, 0, 0, 0);
        entities.Add(new SketchEntity("shaft_axis", SketchEntityTypes.ConstructionCenterLine,
            new Dictionary<string, string>
            {
                ["axis"] = "shaft_axis", ["selection_mark"] = "16",
                ["x1_mm"] = "0", ["y1_mm"] = "0", ["x2_mm"] = length, ["y2_mm"] = "0"
            }, construction: true, executionOrder: entities.Count + 1));
        return Complete(
            source,
            [
                new SketchDefinition(
                    "shaft_profile_sketch",
                    "RightPlane",
                    entities,
                    [],
                    new Dictionary<string, string>(),
                    executionOrder: 1)
            ],
            [
                new FeatureDefinition(
                    "shaft_revolve",
                    FeatureTypes.RevolveBoss,
                    new Dictionary<string, string>
                    {
                        ["feature_api"] = "FeatureRevolve2",
                        ["angle_degrees"] = "360",
                        ["profile_selection_mark"] = "0",
                        ["axis_selection_mark"] = "16"
                    },
                    dependencies: [],
                    referencedSketches: ["shaft_profile_sketch"],
                    referencedFeatures: [],
                    executionOrder: 1)
            ]);
    }

    public static CADModelSpec CreateJacketBasic(CADModelSpec source)
    {
        var outerDiameter = Parameter(source, "outer_diameter_mm");
        var innerDiameter = Parameter(source, "inner_diameter_mm");
        var length = Parameter(source, "length_mm");
        var boreCutDepth = Scale(length, 2d);
        return Complete(
            source,
            [
                Circle("jacket_outer_sketch", "jacket_outer_circle", outerDiameter, 1),
                Circle("jacket_inner_sketch", "jacket_inner_circle", innerDiameter, 2)
            ],
            [
                new FeatureDefinition(
                    "jacket_body_extrude",
                    FeatureTypes.ExtrudeBoss,
                    new Dictionary<string, string>
                    {
                        ["depth_mm"] = length,
                        ["direction"] = "blind"
                    },
                    dependencies: [],
                    referencedSketches: ["jacket_outer_sketch"],
                    referencedFeatures: [],
                    executionOrder: 1),
                new FeatureDefinition(
                    "jacket_inner_cut",
                    FeatureTypes.ExtrudeCut,
                    new Dictionary<string, string>
                    {
                        ["depth_mm"] = boreCutDepth,
                        ["direction"] = "blind",
                        ["through_all"] = "false"
                    },
                    dependencies: ["jacket_body_extrude"],
                    referencedSketches: ["jacket_inner_sketch"],
                    referencedFeatures: ["jacket_body_extrude"],
                    executionOrder: 2)
            ]);
    }

    private static SketchDefinition Circle(string sketchId, string entityId, string diameter, int order) =>
        new(sketchId, "TopPlane",
            [new SketchEntity(entityId, SketchEntityTypes.Circle, new Dictionary<string, string>
            {
                ["center_x_mm"] = "0", ["center_y_mm"] = "0", ["radius_mm"] = Scale(diameter, 0.5d)
            })], [], new Dictionary<string, string>(), executionOrder: order);

    private static double[] ParseList(string values) => string.IsNullOrWhiteSpace(values) ? []
        : values.Split(',').Select(value => double.Parse(value.Trim(), CultureInfo.InvariantCulture)).ToArray();

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static CADModelSpec Complete(
        CADModelSpec source,
        IReadOnlyList<SketchDefinition> sketches,
        IReadOnlyList<FeatureDefinition> features) =>
        source with
        {
            Unit = "mm",
            ReferenceGeometry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["top_plane"] = "TopPlane",
                ["right_plane"] = "RightPlane",
                ["top_face"] = "TopFace"
            },
            Sketches = sketches,
            Features = features
        };

    private static string Parameter(CADModelSpec source, string name) =>
        source.TryGetParameter(name, out var value) ? value : string.Empty;

    private static string Scale(string value, double factor) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
        double.IsFinite(parsed)
            ? (parsed * factor).ToString("R", CultureInfo.InvariantCulture)
            : string.Empty;
}
