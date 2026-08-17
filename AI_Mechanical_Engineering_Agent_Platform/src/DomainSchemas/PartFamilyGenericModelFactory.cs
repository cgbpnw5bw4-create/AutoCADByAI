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
                        Dimensional("plate_length", "plate_base_rectangle", "length_mm", length),
                        Dimensional("plate_width", "plate_base_rectangle", "width_mm", width)
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
                        Dimensional("plate_hole_diameter", "plate_hole_pattern", "hole_diameter_mm", holeDiameter)
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
                CircleSketch(
                    "flange_outer_sketch",
                    "flange_outer_circle",
                    "TopPlane",
                    "outer_diameter_mm",
                    outerDiameter,
                    new Dictionary<string, string> { ["profile"] = "outer_circle" },
                    executionOrder: 1),
                CircleSketch(
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
                        Dimensional("flange_bolt_hole_diameter", "flange_bolt_circle", "bolt_hole_diameter_mm", boltHoleDiameter),
                        Dimensional("flange_bolt_circle_diameter", "flange_bolt_circle", "bolt_circle_diameter_mm", boltCircleDiameter)
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
        return Complete(
            source,
            [
                new SketchDefinition(
                    "shaft_profile_sketch",
                    "RightPlane",
                    [
                        new SketchEntity(
                            "shaft_half_profile",
                            SketchEntityTypes.Line,
                            new Dictionary<string, string>
                            {
                                ["profile"] = "closed_half_section",
                                ["diameter_mm"] = diameter,
                                ["length_mm"] = length,
                                ["optional_step_diameters"] = stepDiameters,
                                ["optional_step_lengths"] = stepLengths
                            }),
                        new SketchEntity(
                            "shaft_axis",
                            SketchEntityTypes.ConstructionCenterLine,
                            new Dictionary<string, string>
                            {
                                ["axis"] = "shaft_axis",
                                ["selection_mark"] = "16"
                            },
                            construction: true,
                            executionOrder: 2)
                    ],
                    [
                        new SketchConstraint(
                            "shaft_axis_horizontal",
                            SketchConstraintTypes.Horizontal,
                            ["shaft_axis"]),
                        Dimensional("shaft_diameter", "shaft_half_profile", "diameter_mm", diameter),
                        Dimensional("shaft_length", "shaft_half_profile", "length_mm", length)
                    ],
                    new Dictionary<string, string>
                    {
                        ["diameter_mm"] = diameter,
                        ["length_mm"] = length,
                        ["optional_step_diameters"] = stepDiameters,
                        ["optional_step_lengths"] = stepLengths
                    },
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
                CircleSketch(
                    "jacket_outer_sketch",
                    "jacket_outer_circle",
                    "TopPlane",
                    "outer_diameter_mm",
                    outerDiameter,
                    new Dictionary<string, string> { ["profile"] = "jacket_outer_circle" },
                    executionOrder: 1),
                CircleSketch(
                    "jacket_inner_sketch",
                    "jacket_inner_circle",
                    "TopPlane",
                    "inner_diameter_mm",
                    innerDiameter,
                    new Dictionary<string, string> { ["profile"] = "jacket_inner_circle" },
                    executionOrder: 2)
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
                        ["cut_role"] = "jacket_bore",
                        ["hole_diameter_mm"] = innerDiameter,
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

    private static SketchDefinition CircleSketch(
        string sketchId,
        string entityId,
        string plane,
        string dimensionName,
        string dimensionValue,
        IReadOnlyDictionary<string, string> additions,
        int executionOrder)
    {
        var parameters = new Dictionary<string, string>(additions, StringComparer.OrdinalIgnoreCase)
        {
            [dimensionName] = dimensionValue
        };
        return new SketchDefinition(
            sketchId,
            plane,
            [new SketchEntity(entityId, SketchEntityTypes.Circle, parameters)],
            [Dimensional($"{entityId}_dimension", entityId, dimensionName, dimensionValue)],
            new Dictionary<string, string> { [dimensionName] = dimensionValue },
            executionOrder);
    }

    private static SketchConstraint Dimensional(
        string constraintId,
        string entityId,
        string parameterName,
        string value) =>
        new(
            constraintId,
            SketchConstraintTypes.Dimensional,
            [entityId],
            value,
            new Dictionary<string, string> { ["parameter"] = parameterName });

    private static string Parameter(CADModelSpec source, string name) =>
        source.TryGetParameter(name, out var value) ? value : string.Empty;

    private static string Scale(string value, double factor) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
        double.IsFinite(parsed)
            ? (parsed * factor).ToString("R", CultureInfo.InvariantCulture)
            : string.Empty;
}
