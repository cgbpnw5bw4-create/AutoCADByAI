using System.Globalization;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SolidWorksWorker;

public sealed class SolidWorksPlateFeatureBuilder
{
    public void CreateBasePlate(
        object model,
        double lengthMeters,
        double widthMeters,
        double thicknessMeters,
        SolidWorksPlateBuildDiagnostics diagnostics,
        List<string> logs)
    {
        diagnostics.OperationsExecuted.Add("base_sketch_started");
        SelectSketchPlane(model, diagnostics, logs);
        EnterSketch(model);
        RequireComResult(
            Invoke(GetProperty(model, "SketchManager"), "CreateCenterRectangle", 0d, 0d, 0d, lengthMeters / 2, widthMeters / 2, 0d),
            "base_sketch_failed: CreateCenterRectangle returned null.");
        diagnostics.OperationsExecuted.Add("base_sketch_success");

        diagnostics.OperationsExecuted.Add("extrude_started");
        RequireComResult(
            Invoke(
                GetProperty(model, "FeatureManager"),
                "FeatureExtrusion2",
                true, false, false, 0, 0, thicknessMeters, 0d, false, false, false, false,
                0d, 0d, false, false, false, false, true, true, true, 0, 0d, false),
            "extrude_failed: FeatureExtrusion2 returned null.");
        TryDisableContourSelection(model);
        diagnostics.OperationsExecuted.Add("extrude_success");
    }

    public void CreateThroughHoles(
        object model,
        IReadOnlyList<(double X, double Y)> holeCentersMeters,
        double holeRadiusMeters,
        double cutDepthMeters,
        SolidWorksPlateBuildDiagnostics diagnostics,
        List<string> logs)
    {
        diagnostics.OperationsExecuted.Add("hole_sketch_started");
        SelectSketchPlane(model, diagnostics, logs);
        EnterSketch(model);
        var sketchManager = GetProperty(model, "SketchManager");
        foreach (var (x, y) in holeCentersMeters)
        {
            RequireComResult(
                Invoke(sketchManager, "CreateCircle", x, y, 0d, x + holeRadiusMeters, y, 0d),
                "hole_sketch_failed: CreateCircle returned null.");
            logs.Add(string.Format(
                CultureInfo.InvariantCulture,
                "hole_circle_created: center_x={0}, center_y={1}, radius={2}",
                x,
                y,
                holeRadiusMeters));
        }

        var sketchReference = CaptureSketchReference(model, "hole_sketch");
        diagnostics.OperationsExecuted.Add("hole_sketch_success");

        diagnostics.OperationsExecuted.Add("cut_holes_started");
        var featureManager = GetProperty(model, "FeatureManager");
        var activeSketchCandidates = CreateMacroRecordedActiveSketchFeatureCutCandidates(cutDepthMeters);
        foreach (var candidate in activeSketchCandidates)
        {
            diagnostics.OperationsExecuted.Add($"cut_holes_candidate_started:{candidate.Name}");
            try
            {
                var feature = Invoke(featureManager, candidate.MethodName, candidate.Args);
                if (feature is not null)
                {
                    diagnostics.OperationsExecuted.Add($"cut_holes_candidate_success:{candidate.Name}");
                    diagnostics.OperationsExecuted.Add("cut_holes_success");
                    logs.Add($"cut_holes_strategy_used: {candidate.Name}.");
                    TryDisableContourSelection(model);
                    TryInvoke(model, "ForceRebuild3", false);
                    return;
                }

                diagnostics.Warnings.Add($"cut_holes_candidate_failed:{candidate.Name}: {candidate.MethodName} returned null.");
            }
            catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or COMException)
            {
                diagnostics.Warnings.Add($"cut_holes_candidate_failed:{candidate.Name}: {ex.GetBaseException().Message}");
            }
        }

        ExitSketch(model);
        var candidates = CreateFeatureCut4SelectionFallbackCandidates(cutDepthMeters);
        foreach (var candidate in candidates)
        {
            diagnostics.OperationsExecuted.Add($"cut_holes_candidate_started:{candidate.Name}");
            try
            {
                if (!SelectSketchForFeatureCut(model, sketchReference, diagnostics, logs, candidate.Name))
                {
                    diagnostics.Warnings.Add($"cut_holes_candidate_failed:{candidate.Name}: sketch selection failed.");
                    continue;
                }

                var feature = Invoke(featureManager, candidate.MethodName, candidate.Args);
                if (feature is not null)
                {
                    diagnostics.OperationsExecuted.Add($"cut_holes_candidate_success:{candidate.Name}");
                    diagnostics.OperationsExecuted.Add("cut_holes_success");
                    logs.Add($"cut_holes_strategy_used: {candidate.Name}.");
                    TryInvoke(model, "ForceRebuild3", false);
                    return;
                }

                diagnostics.Warnings.Add($"cut_holes_candidate_failed:{candidate.Name}: {candidate.MethodName} returned null.");
            }
            catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or COMException)
            {
                diagnostics.Warnings.Add($"cut_holes_candidate_failed:{candidate.Name}: {ex.GetBaseException().Message}");
            }
        }

        diagnostics.Issues.Add("cut_holes_failed: all macro-recorded active-sketch FeatureCut4 and fallback FeatureCut4 candidates failed.");
        diagnostics.Issues.Add("api_evidence_insufficient: FeatureCut4 still failed after applying the recorded macro order; compare exact macro arguments if this persists.");
        throw new InvalidOperationException("cut_holes_failed: all macro-recorded FeatureCut4 candidates failed.");
    }

    public void SavePart(
        object model,
        string partPath,
        SolidWorksPlateBuildDiagnostics diagnostics,
        List<string> logs,
        Func<object, string, List<string>, List<string>, bool> tryExtensionSaveAs,
        Func<object, string, object?[], bool> tryInvokeBool)
    {
        diagnostics.OperationsExecuted.Add("save_sldprt_started");
        diagnostics.SldprtSaveAttempted = true;
        diagnostics.SldprtPath = Path.GetFullPath(partPath);
        Directory.CreateDirectory(Path.GetDirectoryName(partPath)!);

        var saved = tryExtensionSaveAs(model, partPath, diagnostics.SldprtSaveErrors, diagnostics.SldprtSaveWarnings) ||
                    tryInvokeBool(model, "SaveAs3", [partPath, 0, 1]) ||
                    tryInvokeBool(model, "SaveAs", [partPath]);

        if (!saved)
        {
            diagnostics.SldprtSaveErrors.Add("sldprt_save_failed: SaveAs returned false.");
        }

        if (!File.Exists(partPath) || new FileInfo(partPath).Length <= 0)
        {
            diagnostics.SldprtSaveSuccess = false;
            diagnostics.SldprtSaveErrors.Add($"sldprt_save_failed: {partPath}");
            diagnostics.Issues.Add("sldprt_save_failed: SLDPRT file was not created or is empty.");
            throw new IOException($"sldprt_save_failed: {partPath}");
        }

        diagnostics.SldprtSaveSuccess = true;
        diagnostics.SldprtSizeBytes = new FileInfo(partPath).Length;
        diagnostics.OperationsExecuted.Add("save_sldprt_success");
        logs.Add($"Saved SolidWorks part: {partPath}.");
    }

    public void ExportStep()
    {
    }

    private static IReadOnlyList<CutFeatureCandidate> CreateMacroRecordedActiveSketchFeatureCutCandidates(double cutDepthMeters)
    {
        const int swEndCondBlind = 0;
        const double oneDegreeRadians = Math.PI / 180d;
        return new[]
        {
            new CutFeatureCandidate(
                "featurecut4_macro_active_sketch_blind_direction_true",
                "FeatureCut4",
                [
                    true, false, true,
                    swEndCondBlind, swEndCondBlind,
                    cutDepthMeters, cutDepthMeters,
                    false, false, false, false,
                    oneDegreeRadians, oneDegreeRadians,
                    false, false, false, false,
                    false, true, true,
                    true, false, false,
                    0, 0d, false, false
                ]),
            new CutFeatureCandidate(
                "featurecut4_macro_active_sketch_blind_flip_true",
                "FeatureCut4",
                [
                    true, true, true,
                    swEndCondBlind, swEndCondBlind,
                    cutDepthMeters, cutDepthMeters,
                    false, false, false, false,
                    oneDegreeRadians, oneDegreeRadians,
                    false, false, false, false,
                    false, true, true,
                    true, false, false,
                    0, 0d, false, false
                ])
        };
    }

    private static IReadOnlyList<CutFeatureCandidate> CreateFeatureCut4SelectionFallbackCandidates(double cutDepthMeters)
    {
        const int swEndCondThroughAll = 1;
        return new[]
        {
            new CutFeatureCandidate(
                "featurecut4_reference_direction_true_flip_false",
                "FeatureCut4",
                [
                    true, false, false,
                    swEndCondThroughAll, 0,
                    cutDepthMeters, 0d,
                    false, false, false, false,
                    0d, 0d,
                    false, false, false, false, false,
                    true, true, true, true,
                    false, 0, 0d, false, false
                ]),
            new CutFeatureCandidate(
                "featurecut4_reference_direction_true_flip_true",
                "FeatureCut4",
                [
                    true, true, false,
                    swEndCondThroughAll, 0,
                    cutDepthMeters, 0d,
                    false, false, false, false,
                    0d, 0d,
                    false, false, false, false, false,
                    true, true, true, true,
                    false, 0, 0d, false, false
                ]),
            new CutFeatureCandidate(
                "featurecut4_reference_direction_false_flip_false",
                "FeatureCut4",
                [
                    false, false, false,
                    swEndCondThroughAll, 0,
                    cutDepthMeters, 0d,
                    false, false, false, false,
                    0d, 0d,
                    false, false, false, false, false,
                    true, true, true, true,
                    false, 0, 0d, false, false
                ])
        };
    }

    private static void SelectSketchPlane(object model, SolidWorksPlateBuildDiagnostics diagnostics, List<string> logs)
    {
        diagnostics.OperationsExecuted.Add("plane_selection_started");
        diagnostics.PlaneSelectionAttempted = true;

        var result = new SolidWorksPlaneSelector().SelectStandardPlane(model);
        diagnostics.PlaneSelectionSuccess = result.Success;
        diagnostics.SelectedPlaneName = result.SelectedPlaneName;
        diagnostics.SelectedPlaneStrategy = result.SelectedPlaneStrategy;
        diagnostics.PlaneSelectionErrors.Clear();
        diagnostics.PlaneSelectionErrors.AddRange(result.Errors);
        diagnostics.AvailableReferencePlanes.Clear();
        diagnostics.AvailableReferencePlanes.AddRange(result.AvailableReferencePlanes);

        if (!result.Success)
        {
            diagnostics.Issues.Add("plane_selection_failed: select_plane_failed_all_candidates.");
            var error = result.Errors.LastOrDefault() ?? "select_plane_failed_all_candidates";
            throw new InvalidOperationException($"sketch_failed: {error}");
        }

        diagnostics.OperationsExecuted.Add("plane_selection_success");
        logs.Add($"Selected sketch plane: {result.SelectedPlaneName} using {result.SelectedPlaneStrategy}.");
    }

    private static SketchSelectionReference CaptureSketchReference(object model, string fallbackName)
    {
        var sketch = TryInvoke(model, "GetActiveSketch2") ?? TryGetProperty(GetProperty(model, "SketchManager"), "ActiveSketch");
        var name = ReadComName(sketch) ?? fallbackName;
        return new SketchSelectionReference(
            name,
            sketch,
            sketch is null ? null : TryInvoke(sketch, "GetFeature"),
            sketch is null ? null : TryInvoke(sketch, "GetSketchContours"),
            sketch is null ? null : TryInvoke(sketch, "GetSketchRegions"),
            sketch is null ? null : TryInvoke(sketch, "GetSketchSegments"));
    }

    private static bool SelectSketchForFeatureCut(
        object model,
        SketchSelectionReference sketchReference,
        SolidWorksPlateBuildDiagnostics diagnostics,
        List<string> logs,
        string candidateName)
    {
        diagnostics.OperationsExecuted.Add($"cut_holes_sketch_selection_started:{candidateName}");
        TryInvoke(model, "ClearSelection2", true);

        var refreshed = sketchReference.Refresh();
        var selectionAttempts = new (string Strategy, object? Objects)[]
        {
            ("sketch_feature_object", refreshed.Feature),
            ("sketch_object", refreshed.Sketch),
            ("sketch_contours", refreshed.Contours),
            ("sketch_regions", refreshed.Regions),
            ("sketch_segments", refreshed.Segments)
        };

        foreach (var (strategy, objects) in selectionAttempts)
        {
            if (TrySelectAnyComObject(objects, append: false, mark: 0))
            {
                diagnostics.OperationsExecuted.Add($"cut_holes_sketch_selection_success:{strategy}");
                logs.Add($"cut_holes_sketch_selected: candidate={candidateName}, strategy={strategy}, sketch={refreshed.Name}.");
                return true;
            }

            TryInvoke(model, "ClearSelection2", true);
        }

        var selectedFeatureName = TrySelectSketchFeatureByName(model, refreshed.Name);
        if (!string.IsNullOrWhiteSpace(selectedFeatureName))
        {
            diagnostics.OperationsExecuted.Add("cut_holes_sketch_selection_success:feature_by_name");
            logs.Add($"cut_holes_sketch_selected: candidate={candidateName}, strategy=feature_by_name, sketch={selectedFeatureName}.");
            return true;
        }

        if (TrySelectSketchById(model, refreshed.Name))
        {
            diagnostics.OperationsExecuted.Add("cut_holes_sketch_selection_success:select_by_id_sketch");
            logs.Add($"cut_holes_sketch_selected: candidate={candidateName}, strategy=select_by_id_sketch, sketch={refreshed.Name}.");
            return true;
        }

        diagnostics.Warnings.Add($"cut_holes_sketch_selection_failed:{candidateName}: unable to select sketch {refreshed.Name} by object, feature, contour, region, segment, FeatureByName, or SelectByID2.");
        return false;
    }

    private static string? TrySelectSketchFeatureByName(object model, string? sketchName)
    {
        foreach (var name in SketchNameCandidates(sketchName))
        {
            var feature = TryInvoke(model, "FeatureByName", name);
            if (TrySelectComObject(feature, append: false, mark: 0))
            {
                return name;
            }
        }

        return null;
    }

    private static bool TrySelectSketchById(object model, string? sketchName)
    {
        var extension = TryGetProperty(model, "Extension");
        if (extension is null)
        {
            return false;
        }

        foreach (var name in SketchNameCandidates(sketchName))
        {
            if (TryInvokeBool(extension, "SelectByID2", name, "SKETCH", 0d, 0d, 0d, false, 0, null!, 0))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SketchNameCandidates(string? sketchName)
    {
        if (string.IsNullOrWhiteSpace(sketchName))
        {
            yield break;
        }

        yield return sketchName;

        if (sketchName.StartsWith("Sketch", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(sketchName["Sketch".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var englishIndex))
        {
            yield return $"草图{englishIndex}";
        }
        else if (sketchName.StartsWith("草图", StringComparison.OrdinalIgnoreCase) &&
                 int.TryParse(sketchName["草图".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var localizedIndex))
        {
            yield return $"Sketch{localizedIndex}";
        }
    }

    private static bool TrySelectAnyComObject(object? objects, bool append, int mark)
    {
        var selected = false;
        foreach (var obj in ExpandComObjects(objects))
        {
            if (TrySelectComObject(obj, append || selected, mark))
            {
                selected = true;
            }
        }

        return selected;
    }

    private static IEnumerable<object?> ExpandComObjects(object? objects)
    {
        if (objects is null)
        {
            yield break;
        }

        if (objects is string)
        {
            yield return objects;
            yield break;
        }

        if (objects is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                yield return item;
            }

            yield break;
        }

        yield return objects;
    }

    private static bool TrySelectComObject(object? obj, bool append, int mark) =>
        obj is not null &&
        (TryInvokeBool(obj, "Select2", append, mark) || TryInvokeBool(obj, "Select4", append, null!));

    private static string? ReadComName(object? obj) =>
        TryGetProperty(obj, "Name")?.ToString() ??
        (obj is null ? null : TryInvoke(obj, "GetName")?.ToString());

    private static void EnterSketch(object model) =>
        Invoke(GetProperty(model, "SketchManager"), "InsertSketch", true);

    private static void ExitSketch(object model) =>
        Invoke(GetProperty(model, "SketchManager"), "InsertSketch", true);

    private static object GetProperty(object target, string name) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty,
            binder: null,
            target,
            Array.Empty<object>())
        ?? throw new InvalidOperationException($"solidworks_property_missing: {name}.");

    private static object? TryGetProperty(object? target, string name)
    {
        if (target is null)
        {
            return null;
        }

        try
        {
            return target.GetType().InvokeMember(
                name,
                BindingFlags.GetProperty,
                binder: null,
                target,
                Array.Empty<object>());
        }
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or COMException)
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
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or COMException)
        {
            return null;
        }
    }

    private static bool TryInvokeBool(object target, string name, params object?[] args)
    {
        var value = TryInvoke(target, name, args);
        return value is bool boolean && boolean;
    }

    private static void TryDisableContourSelection(object model)
    {
        var selectionManager = TryGetProperty(model, "SelectionManager");
        if (selectionManager is null)
        {
            return;
        }

        TrySetProperty(selectionManager, "EnableContourSelection", false);
    }

    private static bool TrySetProperty(object target, string name, object? value)
    {
        try
        {
            target.GetType().InvokeMember(
                name,
                BindingFlags.SetProperty,
                binder: null,
                target,
                new[] { value });
            return true;
        }
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or COMException)
        {
            return false;
        }
    }

    private static void RequireComResult(object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record CutFeatureCandidate(
        string Name,
        string MethodName,
        object?[] Args);

    private sealed record SketchSelectionReference(
        string Name,
        object? Sketch,
        object? Feature,
        object? Contours,
        object? Regions,
        object? Segments)
    {
        public SketchSelectionReference Refresh() =>
            Sketch is null
                ? this
                : this with
                {
                    Feature = TryInvoke(Sketch, "GetFeature") ?? Feature,
                    Contours = TryInvoke(Sketch, "GetSketchContours") ?? Contours,
                    Regions = TryInvoke(Sketch, "GetSketchRegions") ?? Regions,
                    Segments = TryInvoke(Sketch, "GetSketchSegments") ?? Segments
                };
    }
}
