namespace DomainSchemas;

/// <summary>
/// V2.0-E 统一建模内核的零件族回归输入。必须覆盖 PartTypeRegistry 中
/// 已注册的每一个零件族，包括真实执行被冻结的族——冻结的是真实 CAD 授权，
/// 不是 CADModelSpec、FeatureGraph 与 BuildPlan 的可编译性。
/// 它们仅描述可重复的领域模型；执行、Handler 匹配和 dry-run 仍由既有 Worker 链完成。
/// </summary>
public sealed record PartFamilyRegressionModel(string PartType, CADModelSpec Spec);

public static class PartFamilyRegressionModels
{
    public static IReadOnlyList<PartFamilyRegressionModel> CreateDefault() =>
    [
        new(
            PlateBasic4HolesDefinition.Type,
            PartFamilyGenericModelFactory.CreatePlateBasic4Holes(new CADModelSpec(
                "regression-plate-basic-4holes",
                PlateBasic4HolesDefinition.Type,
                new Dictionary<string, string>
                {
                    ["length_mm"] = "160",
                    ["width_mm"] = "80",
                    ["thickness_mm"] = "12",
                    ["hole_diameter_mm"] = "10",
                    ["hole_count"] = "4"
                }))),
        new(
            FlangeBasicDefinition.Type,
            PartFamilyGenericModelFactory.CreateFlangeBasic(new CADModelSpec(
                "regression-flange-basic",
                FlangeBasicDefinition.Type,
                new Dictionary<string, string>
                {
                    ["outer_diameter_mm"] = "160",
                    ["inner_diameter_mm"] = "60",
                    ["thickness_mm"] = "18",
                    ["bolt_hole_count"] = "6",
                    ["bolt_hole_diameter_mm"] = "14",
                    ["bolt_circle_diameter_mm"] = "115"
                }))),
        new(
            ShaftBasicDefinition.Type,
            PartFamilyGenericModelFactory.CreateShaftBasic(new CADModelSpec(
                "regression-shaft-basic",
                ShaftBasicDefinition.Type,
                new Dictionary<string, string>
                {
                    ["diameter_mm"] = "40",
                    ["length_mm"] = "180",
                    ["optional_step_diameters"] = "32,24",
                    ["optional_step_lengths"] = "40,30"
                }))),
        new(
            JacketBasicDefinition.Type,
            PartFamilyGenericModelFactory.CreateJacketBasic(new CADModelSpec(
                "regression-jacket-basic",
                JacketBasicDefinition.Type,
                new Dictionary<string, string>
                {
                    ["outer_diameter_mm"] = "140",
                    ["inner_diameter_mm"] = "120",
                    ["length_mm"] = "180"
                })))
    ];
}
