using System.Globalization;
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
        ExitSketch(model);
        diagnostics.OperationsExecuted.Add("base_sketch_success");

        diagnostics.OperationsExecuted.Add("extrude_started");
        RequireComResult(
            Invoke(
                GetProperty(model, "FeatureManager"),
                "FeatureExtrusion2",
                true, false, false, 0, 0, thicknessMeters, 0d, false, false, false, false,
                0d, 0d, false, false, false, false, true, true, true, 0, 0d, false),
            "extrude_failed: FeatureExtrusion2 returned null.");
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
                Invoke(sketchManager, "CreateCircleByRadius", x, y, 0d, holeRadiusMeters),
                "hole_sketch_failed: CreateCircleByRadius returned null.");
            logs.Add(string.Format(
                CultureInfo.InvariantCulture,
                "hole_circle_created: center_x={0}, center_y={1}, radius={2}",
                x,
                y,
                holeRadiusMeters));
        }

        var activeSketch = TryInvoke(model, "GetActiveSketch2");
        ExitSketch(model);
        diagnostics.OperationsExecuted.Add("hole_sketch_success");

        diagnostics.OperationsExecuted.Add("cut_holes_started");
        TryInvoke(model, "ClearSelection2", true);
        if (activeSketch is not null && !TryInvokeBool(activeSketch, "Select2", false, 0))
        {
            diagnostics.Warnings.Add("cut_holes_sketch_select_warning: active sketch object Select2 returned false.");
        }

        var featureManager = GetProperty(model, "FeatureManager");
        var candidates = CreateFeatureCut4Candidates(cutDepthMeters);
        foreach (var candidate in candidates)
        {
            diagnostics.OperationsExecuted.Add($"cut_holes_candidate_started:{candidate.Name}");
            try
            {
                var feature = Invoke(featureManager, "FeatureCut4", candidate.Args);
                if (feature is not null)
                {
                    diagnostics.OperationsExecuted.Add($"cut_holes_candidate_success:{candidate.Name}");
                    diagnostics.OperationsExecuted.Add("cut_holes_success");
                    logs.Add($"cut_holes_strategy_used: {candidate.Name}.");
                    TryInvoke(model, "ForceRebuild3", false);
                    return;
                }

                diagnostics.Warnings.Add($"cut_holes_candidate_failed:{candidate.Name}: FeatureCut4 returned null.");
            }
            catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or COMException)
            {
                diagnostics.Warnings.Add($"cut_holes_candidate_failed:{candidate.Name}: {ex.GetBaseException().Message}");
            }
        }

        diagnostics.Issues.Add("cut_holes_failed: all FeatureCut4 candidates failed.");
        throw new InvalidOperationException("cut_holes_failed: all FeatureCut4 candidates failed.");
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

    private static IReadOnlyList<FeatureCutCandidate> CreateFeatureCut4Candidates(double cutDepthMeters)
    {
        const int swEndCondThroughAll = 1;
        return new[]
        {
            new FeatureCutCandidate(
                "featurecut4_reference_direction_true_flip_false",
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
            new FeatureCutCandidate(
                "featurecut4_reference_direction_true_flip_true",
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
            new FeatureCutCandidate(
                "featurecut4_reference_direction_false_flip_false",
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

    private static void RequireComResult(object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record FeatureCutCandidate(string Name, object?[] Args);
}
