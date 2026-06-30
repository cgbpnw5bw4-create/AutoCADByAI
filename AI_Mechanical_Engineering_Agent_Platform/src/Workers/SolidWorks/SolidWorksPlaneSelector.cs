using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace SolidWorksWorker;

public sealed class SolidWorksPlaneSelector
{
    public static IReadOnlyList<string> EnglishPlaneNames { get; } =
    [
        "Top Plane",
        "Front Plane",
        "Right Plane"
    ];

    public static IReadOnlyList<string> LocalizedPlaneNames { get; } =
    [
        "上视基准面",
        "前视基准面",
        "右视基准面",
        "上基准面",
        "前基准面",
        "右基准面",
        "Top",
        "Front",
        "Right"
    ];

    public SolidWorksPlaneSelectionResult SelectStandardPlane(object model)
    {
        var errors = new List<string>();
        var availablePlanes = ScanReferencePlanes(model, selectCandidates: false, errors);

        foreach (var name in EnglishPlaneNames)
        {
            if (TrySelectByName(model, name, errors))
            {
                return Success(name, "named_english", availablePlanes, errors);
            }
        }

        foreach (var name in LocalizedPlaneNames)
        {
            if (TrySelectByName(model, name, errors))
            {
                return Success(name, "named_localized", availablePlanes, errors);
            }
        }

        var selectablePlanes = ScanReferencePlanes(model, selectCandidates: true, errors);
        if (selectablePlanes.Count > 0)
        {
            availablePlanes = selectablePlanes;
        }

        foreach (var plane in Prioritize(selectablePlanes))
        {
            if (plane.SelectSuccess == true)
            {
                return Success(plane.Name, "feature_scan_preferred", selectablePlanes, errors);
            }
        }

        var candidateText = string.Join(", ", EnglishPlaneNames.Concat(LocalizedPlaneNames));
        var scannedText = string.Join(", ", selectablePlanes.Select(plane => $"{plane.Name}:{plane.TypeName}"));
        errors.Add($"select_plane_failed_all_candidates: candidates=[{candidateText}], scanned=[{scannedText}]");
        return new SolidWorksPlaneSelectionResult(
            Success: false,
            SelectedPlaneName: null,
            SelectedPlaneStrategy: null,
            Errors: errors,
            AvailableReferencePlanes: selectablePlanes.Count > 0 ? selectablePlanes : availablePlanes);
    }

    private static SolidWorksPlaneSelectionResult Success(
        string name,
        string strategy,
        IReadOnlyList<SolidWorksReferencePlaneInfo> availablePlanes,
        IReadOnlyList<string> errors) =>
        new(
            Success: true,
            SelectedPlaneName: name,
            SelectedPlaneStrategy: strategy,
            Errors: errors,
            AvailableReferencePlanes: availablePlanes);

    private static bool TrySelectByName(object model, string name, List<string> errors)
    {
        try
        {
            var extension = GetProperty(model, "Extension");
            var selected = TryInvokeBool(extension, "SelectByID2", name, "PLANE", 0d, 0d, 0d, false, 0, null!, 0);
            if (!selected)
            {
                errors.Add($"select_by_name_failed: {name}");
            }

            return selected;
        }
        catch (Exception ex) when (IsComReflectionException(ex))
        {
            errors.Add($"select_by_name_exception: {name}: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private static List<SolidWorksReferencePlaneInfo> ScanReferencePlanes(
        object model,
        bool selectCandidates,
        List<string> errors)
    {
        var planes = new List<SolidWorksReferencePlaneInfo>();
        object? feature;
        try
        {
            feature = TryInvoke(model, "FirstFeature");
        }
        catch (Exception ex) when (IsComReflectionException(ex))
        {
            errors.Add($"feature_scan_failed: FirstFeature: {ex.GetBaseException().Message}");
            return planes;
        }

        var visited = 0;
        while (feature is not null && visited++ < 256)
        {
            var name = TryGetProperty(feature, "Name")?.ToString() ?? string.Empty;
            var typeName =
                TryInvoke(feature, "GetTypeName2")?.ToString() ??
                TryInvoke(feature, "GetTypeName")?.ToString() ??
                string.Empty;

            if (IsReferencePlane(name, typeName))
            {
                bool? selectSuccess = null;
                if (selectCandidates)
                {
                    selectSuccess = TryInvokeBool(feature, "Select2", false, 0);
                }

                planes.Add(new SolidWorksReferencePlaneInfo(name, typeName, selectSuccess));
            }

            try
            {
                feature = TryInvoke(feature, "GetNextFeature");
            }
            catch (Exception ex) when (IsComReflectionException(ex))
            {
                errors.Add($"feature_scan_failed: GetNextFeature: {ex.GetBaseException().Message}");
                break;
            }
        }

        return planes;
    }

    private static IEnumerable<SolidWorksReferencePlaneInfo> Prioritize(IEnumerable<SolidWorksReferencePlaneInfo> planes) =>
        planes.OrderBy(PlanePriority);

    private static int PlanePriority(SolidWorksReferencePlaneInfo plane)
    {
        var name = plane.Name;
        if (name.Contains("Top", StringComparison.OrdinalIgnoreCase) || name.Contains('上'))
        {
            return 0;
        }

        if (name.Contains("Front", StringComparison.OrdinalIgnoreCase) || name.Contains('前'))
        {
            return 1;
        }

        if (name.Contains("Right", StringComparison.OrdinalIgnoreCase) || name.Contains('右'))
        {
            return 2;
        }

        return 3;
    }

    private static bool IsReferencePlane(string name, string typeName) =>
        typeName.Contains("RefPlane", StringComparison.OrdinalIgnoreCase) ||
        typeName.Contains("Reference", StringComparison.OrdinalIgnoreCase) ||
        typeName.Contains("Plane", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Plane", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("基准面", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Top", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Front", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Right", StringComparison.OrdinalIgnoreCase) ||
        name.Contains('上') ||
        name.Contains('前') ||
        name.Contains('右');

    private static object GetProperty(object target, string name) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty,
            binder: null,
            target,
            Array.Empty<object>())
        ?? throw new InvalidOperationException($"solidworks_property_missing: {name}.");

    private static object? TryGetProperty(object target, string name)
    {
        try
        {
            return GetProperty(target, name);
        }
        catch (Exception ex) when (IsComReflectionException(ex))
        {
            return null;
        }
    }

    private static object? Invoke(object target, string name, params object?[] args) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.InvokeMethod,
            binder: null,
            target,
            args);

    private static object? TryInvoke(object target, string name, params object?[] args)
    {
        try
        {
            return Invoke(target, name, args);
        }
        catch (Exception ex) when (IsComReflectionException(ex))
        {
            return null;
        }
    }

    private static bool TryInvokeBool(object target, string name, params object?[] args)
    {
        var value = TryInvoke(target, name, args);
        return value is bool boolean && boolean;
    }

    private static bool IsComReflectionException(Exception ex) =>
        ex is MissingMethodException or TargetInvocationException or COMException or InvalidOperationException;
}

public sealed record SolidWorksPlaneSelectionResult(
    bool Success,
    string? SelectedPlaneName,
    string? SelectedPlaneStrategy,
    IReadOnlyList<string> Errors,
    IReadOnlyList<SolidWorksReferencePlaneInfo> AvailableReferencePlanes);

public sealed class SolidWorksReferencePlaneInfo
{
    public SolidWorksReferencePlaneInfo(string name, string? typeName, bool? selectSuccess = null)
    {
        Name = name;
        TypeName = typeName;
        SelectSuccess = selectSuccess;
    }

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("type_name")]
    public string? TypeName { get; }

    [JsonPropertyName("select_success")]
    public bool? SelectSuccess { get; }
}
