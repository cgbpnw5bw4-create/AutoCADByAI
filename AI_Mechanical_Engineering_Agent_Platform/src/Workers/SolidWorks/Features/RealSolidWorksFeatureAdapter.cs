using System.Globalization;
using System.Text.Json;
using DomainSchemas;

namespace SolidWorksWorker.Features;

/// <summary>
/// The exclusive COM boundary for generic feature handlers. Handlers pass only
/// domain definitions and never receive ModelDoc2, FeatureManager, SketchManager,
/// ISolidWorksComFacade, dynamic objects, or raw feature RCWs.
/// </summary>
public sealed class RealSolidWorksFeatureAdapter : ISolidWorksFeatureAdapter
{
    public const string AdapterIdentifier = "solidworks.real-feature-adapter";
    public const string CurrentAdapterVersion = "2.0-c.2";

    private const double MmToMeters = 0.001d;
    private static readonly double OneDegreeInRadians = Math.PI / 180d;

    private static readonly IReadOnlyDictionary<string, string[]> PlaneAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["FrontPlane"] = ["Front Plane", "FrontPlane", "前视基准面"],
            ["TopPlane"] = ["Top Plane", "TopPlane", "上视基准面"],
            ["RightPlane"] = ["Right Plane", "RightPlane", "右视基准面"]
        };

    private readonly object _model;
    private readonly ISolidWorksComFacade _com;
    private readonly Dictionary<string, SketchComArtifact> _sketches =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<object> _ownedReferences = [];
    private readonly HashSet<object> _ownedReferenceSet =
        new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public RealSolidWorksFeatureAdapter(object model, ISolidWorksComFacade com)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _com = com ?? throw new ArgumentNullException(nameof(com));
    }

    public string AdapterId => AdapterIdentifier;

    public string AdapterVersion => CurrentAdapterVersion;

    public Task<FeatureHandlerExecutionResult> ExecuteSketchAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guard(
            PartFamilyFailureStages.SketchExecutionFailed,
            () => CreateSketch(feature, operation)));
    }

    public Task<FeatureHandlerExecutionResult> ExecuteExtrudeBossAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guard(
            PartFamilyFailureStages.ExtrudeExecutionFailed,
            () =>
            {
                RequireBlind(feature.Parameters);
                var depth = RequiredPositive(feature.Parameters, "depth_mm");
                var volumeBefore = MeasureSolidVolume();
                SelectDependencySketch(operation);
                var manager = Own(_com.TryGetProperty(_model, "FeatureManager"))
                    ?? throw Stage(PartFamilyFailureStages.ExtrudeExecutionFailed, "FeatureManager is unavailable.");
                var featureObject = Own(_com.InvokeWithArgs(
                    manager,
                    "FeatureExtrusion2",
                    [
                        true, false, false, 0, 0, depth * MmToMeters, 0d,
                        false, false, false, false, 0d, 0d, false, false,
                        false, false, true, true, true, 0, 0d, false
                    ]));
                return ValidateFeatureResult(
                    feature,
                    operation,
                    featureObject,
                    volumeBefore,
                    VolumeChangeExpectation.Increase);
            }));
    }

    public Task<FeatureHandlerExecutionResult> ExecuteExtrudeCutAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guard(
            PartFamilyFailureStages.CutExecutionFailed,
            () =>
            {
                RequireBlind(feature.Parameters);
                var depth = RequiredPositive(feature.Parameters, "depth_mm");
                var volumeBefore = MeasureSolidVolume();
                var sketchManager = ActivateSketchForFeatureCut(FindDependencySketch(operation));
                var manager = Own(_com.TryGetProperty(_model, "FeatureManager"))
                    ?? throw Stage(PartFamilyFailureStages.CutExecutionFailed, "FeatureManager is unavailable.");
                try
                {
                    var featureObject = Own(_com.InvokeWithArgs(
                        manager,
                        "FeatureCut4",
                        BlindCutArguments(depth * MmToMeters)));
                    return ValidateFeatureResult(
                        feature,
                        operation,
                        featureObject,
                        volumeBefore,
                        VolumeChangeExpectation.Decrease);
                }
                finally
                {
                    CloseActiveSketchIfNeeded(sketchManager);
                }
            }));
    }

    public Task<FeatureHandlerExecutionResult> ExecuteHoleAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guard(
            PartFamilyFailureStages.HoleExecutionFailed,
            () =>
            {
                RequireBlind(feature.Parameters);
                var requestedDiameter = RequiredPositiveAlias(
                    feature.Parameters,
                    "hole_diameter_mm",
                    "diameter_mm");
                var depth = RequiredPositive(feature.Parameters, "depth_mm");
                var volumeBefore = MeasureSolidVolume();
                var sketch = FindDependencySketch(operation);
                if (sketch.EntityTypes.Count == 0 ||
                    sketch.EntityTypes.Any(type =>
                        !type.Equals(SketchEntityTypes.Circle, StringComparison.OrdinalIgnoreCase)))
                {
                    throw Stage(
                        PartFamilyFailureStages.HoleExecutionFailed,
                        "Simple hole requires a dependency sketch containing circles only.");
                }

                if (sketch.CircleDiametersMm.Count == 0 ||
                    sketch.CircleDiametersMm.Any(diameter =>
                        Math.Abs(diameter - requestedDiameter) > 1e-6))
                {
                    throw Stage(
                        PartFamilyFailureStages.HoleExecutionFailed,
                        "Simple-hole diameter does not match its dependency circle geometry.");
                }

                var sketchManager = ActivateSketchForFeatureCut(sketch);
                var manager = Own(_com.TryGetProperty(_model, "FeatureManager"))
                    ?? throw Stage(PartFamilyFailureStages.HoleExecutionFailed, "FeatureManager is unavailable.");
                try
                {
                    var featureObject = Own(_com.InvokeWithArgs(
                        manager,
                        "FeatureCut4",
                        BlindCutArguments(depth * MmToMeters)));
                    return ValidateFeatureResult(
                        feature,
                        operation,
                        featureObject,
                        volumeBefore,
                        VolumeChangeExpectation.Decrease);
                }
                finally
                {
                    CloseActiveSketchIfNeeded(sketchManager);
                }
            }));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var index = _ownedReferences.Count - 1; index >= 0; index--)
        {
            try
            {
                _com.ReleaseComObject(_ownedReferences[index]);
            }
            catch
            {
                // Reference cleanup must continue even when one RCW is stale.
            }
        }

        _ownedReferences.Clear();
        _ownedReferenceSet.Clear();
        _sketches.Clear();
    }

    private FeatureHandlerExecutionResult CreateSketch(
        FeatureDefinition feature,
        SolidWorksOperation operation)
    {
        if (!feature.Parameters.TryGetValue("entities", out var entitiesJson))
        {
            throw Stage(
                PartFamilyFailureStages.SketchGeometryCreateFailed,
                "Serialized sketch entities are missing.");
        }

        SketchEntity[] entities;
        try
        {
            entities = JsonSerializer.Deserialize<SketchEntity[]>(entitiesJson) ?? [];
        }
        catch (JsonException ex)
        {
            throw Stage(
                PartFamilyFailureStages.SketchGeometryCreateFailed,
                $"Serialized sketch entities are invalid: {ex.Message}");
        }

        RequireEmptyCollection(feature.Parameters, "constraints");
        RequireEmptyCollection(feature.Parameters, "dimensions");
        SelectExactPlane(feature.TargetReference ?? operation.SketchPlane);

        var sketchManager = Own(_com.TryGetProperty(_model, "SketchManager"))
            ?? throw Stage(PartFamilyFailureStages.SketchExecutionFailed, "SketchManager is unavailable.");
        var entered = false;
        object? sketchFeature = null;
        try
        {
            _com.Invoke(sketchManager, "InsertSketch", true);
            entered = true;
            var activeSketch = Own(
                    _com.TryInvoke(_model, "GetActiveSketch2") ??
                    _com.TryGetProperty(sketchManager, "ActiveSketch"))
                ?? throw Stage(PartFamilyFailureStages.SketchExecutionFailed, "InsertSketch did not activate a sketch.");

            foreach (var entity in entities)
            {
                CreateSketchEntity(sketchManager, entity);
            }

            // Some supported SolidWorks dispatches expose ISketch.GetFeature as null even
            // though GetActiveSketch2 returns a valid, selectable ISketch. The acceptance
            // contract for a sketch is the non-null sketch object plus non-null geometry.
            sketchFeature = Own(_com.TryInvoke(activeSketch, "GetFeature")) ?? activeSketch;

            _com.Invoke(sketchManager, "InsertSketch", true);
            entered = false;
        }
        finally
        {
            if (entered)
            {
                _com.TryInvoke(sketchManager, "InsertSketch", true);
            }
        }

        EnsureModelRebuild();
        var artifact = new SketchComArtifact(
            sketchFeature,
            entities.Select(entity => entity.EntityType).ToHashSet(StringComparer.OrdinalIgnoreCase),
            entities
                .Where(entity =>
                    entity.EntityType.Equals(
                        SketchEntityTypes.Circle,
                        StringComparison.OrdinalIgnoreCase))
                .Select(entity => CircleRadius(entity.Parameters) * 2d)
                .ToArray());
        _sketches[operation.OperationId] = artifact;
        _sketches[feature.FeatureId] = artifact;
        if (feature.Parameters.TryGetValue("sketch_id", out var sketchId) &&
            !string.IsNullOrWhiteSpace(sketchId))
        {
            _sketches[sketchId] = artifact;
        }

        return Passed(
            feature,
            operation,
            geometryChangeValidated: true);
    }

    private void CreateSketchEntity(object sketchManager, SketchEntity entity)
    {
        object? created;
        if (entity.EntityType.Equals(SketchEntityTypes.Line, StringComparison.OrdinalIgnoreCase))
        {
            created = _com.Invoke(
                sketchManager,
                "CreateLine",
                Coordinate(entity.Parameters, "x1_mm"),
                Coordinate(entity.Parameters, "y1_mm"),
                0d,
                Coordinate(entity.Parameters, "x2_mm"),
                Coordinate(entity.Parameters, "y2_mm"),
                0d);
        }
        else if (entity.EntityType.Equals(SketchEntityTypes.Rectangle, StringComparison.OrdinalIgnoreCase))
        {
            var centerX = OptionalCoordinate(entity.Parameters, "center_x_mm");
            var centerY = OptionalCoordinate(entity.Parameters, "center_y_mm");
            var horizontal = entity.Parameters.ContainsKey("length_mm")
                ? RequiredPositive(entity.Parameters, "length_mm")
                : RequiredPositive(entity.Parameters, "width_mm");
            var vertical = entity.Parameters.ContainsKey("height_mm")
                ? RequiredPositive(entity.Parameters, "height_mm")
                : RequiredPositive(entity.Parameters, "width_mm");
            created = _com.Invoke(
                sketchManager,
                "CreateCenterRectangle",
                centerX,
                centerY,
                0d,
                centerX + horizontal * MmToMeters / 2d,
                centerY + vertical * MmToMeters / 2d,
                0d);
        }
        else if (entity.EntityType.Equals(SketchEntityTypes.Circle, StringComparison.OrdinalIgnoreCase))
        {
            if (entity.Parameters.ContainsKey("pattern"))
            {
                throw Stage(
                    PartFamilyFailureStages.SketchGeometryCreateFailed,
                    $"Sketch entity {entity.EntityId} requests an unsupported circle pattern.");
            }

            var centerX = OptionalCoordinate(entity.Parameters, "center_x_mm");
            var centerY = OptionalCoordinate(entity.Parameters, "center_y_mm");
            var radius = CircleRadius(entity.Parameters) * MmToMeters;
            created = _com.Invoke(
                sketchManager,
                "CreateCircle",
                centerX,
                centerY,
                0d,
                centerX + radius,
                centerY,
                0d);
        }
        else
        {
            throw Stage(
                PartFamilyFailureStages.SketchGeometryCreateFailed,
                $"Sketch entity type {entity.EntityType} is not authorized.");
        }

        if (!OwnReturned(created))
        {
            throw Stage(
                PartFamilyFailureStages.SketchGeometryCreateFailed,
                $"Sketch entity {entity.EntityId} returned no geometry object.");
        }
    }

    private FeatureHandlerExecutionResult ValidateFeatureResult(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        object? featureObject,
        double volumeBefore,
        VolumeChangeExpectation volumeChangeExpectation)
    {
        if (featureObject is null)
        {
            throw Stage(PartFamilyFailureStages.FeatureResultInvalid, "Feature API returned null.");
        }

        EnsureRebuildAndErrorCode(featureObject);
        var volumeAfter = MeasureSolidVolume();
        var tolerance = Math.Max(1e-12d, Math.Abs(volumeBefore) * 1e-9d);
        var geometryChanged = volumeChangeExpectation switch
        {
            VolumeChangeExpectation.Increase => volumeAfter > volumeBefore + tolerance,
            VolumeChangeExpectation.Decrease => volumeBefore > 0d && volumeAfter < volumeBefore - tolerance,
            _ => false
        };
        if (!geometryChanged)
        {
            throw Stage(
                PartFamilyFailureStages.FeatureResultInvalid,
                $"Solid-body volume did not {volumeChangeExpectation.ToString().ToLowerInvariant()}: " +
                $"before={volumeBefore:R}, after={volumeAfter:R} cubic metres.");
        }

        return Passed(
            feature,
            operation,
            geometryChangeValidated: true,
            volumeBeforeCubicMeters: volumeBefore,
            volumeAfterCubicMeters: volumeAfter);
    }

    private double MeasureSolidVolume()
    {
        const int swSolidBody = 0;
        var bodiesValue = _com.TryInvoke(_model, "GetBodies2", swSolidBody, false);
        if (bodiesValue is null)
        {
            return 0d;
        }

        var bodies = bodiesValue is Array array
            ? array.Cast<object?>().Where(item => item is not null).Cast<object>().ToArray()
            : [bodiesValue];
        var volume = 0d;
        foreach (var body in bodies)
        {
            Own(body);
            var properties = _com.TryInvoke(body, "GetMassProperties", 1d);
            if (properties is not Array values || values.Length <= 3)
            {
                throw Stage(
                    PartFamilyFailureStages.FeatureResultInvalid,
                    "Solid-body mass properties are unavailable for geometry validation.");
            }

            var bodyVolume = Convert.ToDouble(values.GetValue(3), CultureInfo.InvariantCulture);
            if (!double.IsFinite(bodyVolume) || bodyVolume <= 0d)
            {
                throw Stage(
                    PartFamilyFailureStages.FeatureResultInvalid,
                    $"Solid-body volume is invalid: {bodyVolume:R}.");
            }

            volume += bodyVolume;
        }

        return volume;
    }

    private void EnsureRebuildAndErrorCode(object featureObject)
    {
        EnsureModelRebuild();

        var errorArguments = new object?[] { false };
        var errorCode = _com.TryInvokeWithArgs(featureObject, "GetErrorCode2", errorArguments);
        if (errorCode is null)
        {
            // Some late-bound RCWs do not marshal the GetErrorCode2(out bool)
            // argument. SOLIDWORKS keeps GetErrorCode as the documented,
            // superseded no-by-ref compatibility member.
            errorCode = _com.TryInvoke(featureObject, "GetErrorCode");
            if (errorCode is null)
            {
                throw Stage(
                    PartFamilyFailureStages.FeatureResultInvalid,
                    "Feature GetErrorCode2 and GetErrorCode returned null.");
            }
        }

        var error = Convert.ToInt32(errorCode, CultureInfo.InvariantCulture);
        var warning =
            errorArguments[0] is bool value &&
            value;
        if (error != 0 || warning)
        {
            throw Stage(
                PartFamilyFailureStages.FeatureResultInvalid,
                $"Feature GetErrorCode2 returned error={error}, warning={warning}.");
        }
    }

    private void EnsureModelRebuild()
    {
        if (!_com.TryInvokeBool(_model, "ForceRebuild3", false))
        {
            throw Stage(PartFamilyFailureStages.FeatureResultInvalid, "ForceRebuild3(false) did not return true.");
        }
    }

    private void SelectDependencySketch(SolidWorksOperation operation) =>
        SelectSketch(FindDependencySketch(operation));

    private SketchComArtifact FindDependencySketch(SolidWorksOperation operation)
    {
        foreach (var dependency in operation.DependsOn.Reverse())
        {
            if (_sketches.TryGetValue(dependency, out var sketch))
            {
                return sketch;
            }
        }

        if (operation.Parameters.TryGetValue("sketch_id", out var sketchId) &&
            _sketches.TryGetValue(sketchId, out var referencedSketch))
        {
            return referencedSketch;
        }

        throw Stage(
            PartFamilyFailureStages.FeatureArtifactMissing,
            $"No dependency sketch artifact is available for operation {operation.OperationId}.");
    }

    private void SelectSketch(SketchComArtifact sketch)
    {
        ClearSelection();
        if (!_com.TryInvokeBool(sketch.Feature, "Select2", false, 0) ||
            !HasVerifiedSelection())
        {
            throw Stage(PartFamilyFailureStages.FeatureArtifactMissing, "Dependency sketch selection failed.");
        }
    }

    private object ActivateSketchForFeatureCut(SketchComArtifact sketch)
    {
        SelectSketch(sketch);
        var sketchManager = Own(_com.TryGetProperty(_model, "SketchManager"))
            ?? throw Stage(
                PartFamilyFailureStages.FeatureArtifactMissing,
                "SketchManager is unavailable while activating a cut sketch.");
        _com.Invoke(sketchManager, "InsertSketch", true);
        var activeSketch =
            _com.TryInvoke(_model, "GetActiveSketch2") ??
            _com.TryGetProperty(sketchManager, "ActiveSketch");
        if (activeSketch is null)
        {
            throw Stage(
                PartFamilyFailureStages.FeatureArtifactMissing,
                "Selected dependency sketch did not become active for FeatureCut4.");
        }

        Own(activeSketch);
        return sketchManager;
    }

    private void CloseActiveSketchIfNeeded(object sketchManager)
    {
        var activeSketch =
            _com.TryInvoke(_model, "GetActiveSketch2") ??
            _com.TryGetProperty(sketchManager, "ActiveSketch");
        if (activeSketch is not null)
        {
            _com.TryInvoke(sketchManager, "InsertSketch", true);
        }
    }

    private void SelectExactPlane(string requestedPlane)
    {
        if (!PlaneAliases.TryGetValue(requestedPlane, out var aliases))
        {
            throw Stage(
                PartFamilyFailureStages.SketchExecutionFailed,
                $"Reference {requestedPlane} is not an authorized standard plane.");
        }

        var extension = Own(_com.TryGetProperty(_model, "Extension"));
        if (extension is not null)
        {
            foreach (var alias in aliases)
            {
                ClearSelection();
                if (_com.TryInvokeBool(
                        extension,
                        "SelectByID2",
                        alias,
                        "PLANE",
                        0d,
                        0d,
                        0d,
                        false,
                        0,
                        null,
                        0) &&
                    HasVerifiedSelection())
                {
                    return;
                }
            }
        }

        object? candidate = null;
        var current = Own(_com.TryInvoke(_model, "FirstFeature"));
        while (current is not null)
        {
            var name = _com.TryGetProperty(current, "Name")?.ToString();
            var typeName = _com.TryInvoke(current, "GetTypeName2")?.ToString();
            if (aliases.Contains(name ?? string.Empty, StringComparer.OrdinalIgnoreCase) &&
                string.Equals(typeName, "RefPlane", StringComparison.OrdinalIgnoreCase))
            {
                candidate = current;
                break;
            }

            current = Own(_com.TryInvoke(current, "GetNextFeature"));
        }

        ClearSelection();
        if (candidate is null ||
            !_com.TryInvokeBool(candidate, "Select2", false, 0) ||
            !HasVerifiedSelection())
        {
            throw Stage(
                PartFamilyFailureStages.SketchExecutionFailed,
                $"Exact standard plane {requestedPlane} could not be selected.");
        }
    }

    private void ClearSelection()
    {
        try
        {
            _com.Invoke(_model, "ClearSelection2", true);
        }
        catch (Exception ex)
        {
            throw Stage(
                PartFamilyFailureStages.SketchExecutionFailed,
                $"ClearSelection2(true) failed: {ex.GetBaseException().Message}");
        }
    }

    private bool HasVerifiedSelection()
    {
        var selectionManager = Own(_com.TryGetProperty(_model, "SelectionManager"));
        if (selectionManager is null)
        {
            return false;
        }

        var countValue = _com.TryInvoke(selectionManager, "GetSelectedObjectCount2", -1);
        if (countValue is null ||
            Convert.ToInt32(countValue, CultureInfo.InvariantCulture) < 1)
        {
            return false;
        }

        return Own(_com.TryInvoke(selectionManager, "GetSelectedObject6", 1, -1)) is not null;
    }

    private FeatureHandlerExecutionResult Guard(
        string defaultStage,
        Func<FeatureHandlerExecutionResult> action)
    {
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return action();
        }
        catch (FeatureAdapterException ex)
        {
            return FeatureHandlerExecutionResult.Failed(ex.Stage, $"{ex.Stage}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return FeatureHandlerExecutionResult.Failed(
                defaultStage,
                $"{defaultStage}: {ex.GetBaseException().Message}");
        }
    }

    private FeatureHandlerExecutionResult Passed(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        bool geometryChangeValidated,
        double? volumeBeforeCubicMeters = null,
        double? volumeAfterCubicMeters = null) =>
        FeatureHandlerExecutionResult.Passed(
            [
                $"feature_adapter_success:{AdapterId}@{AdapterVersion}:{feature.FeatureId}",
                "feature_result_non_null:true",
                "feature_rebuild_passed:true"
            ],
            new FeatureAdapterArtifact(
                operation.OperationId,
                feature.FeatureId,
                feature.FeatureType,
                AdapterId,
                AdapterVersion,
                ResultObjectValidated: true,
                RebuildPassed: true,
                geometryChangeValidated,
                volumeBeforeCubicMeters,
                volumeAfterCubicMeters));

    private object? Own(object? value)
    {
        if (value is not null && _ownedReferenceSet.Add(value))
        {
            _ownedReferences.Add(value);
        }

        return value;
    }

    private bool OwnReturned(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is Array array)
        {
            var found = false;
            foreach (var item in array)
            {
                found |= Own(item) is not null;
            }

            return found;
        }

        Own(value);
        return true;
    }

    private static object?[] BlindCutArguments(double depthMeters) =>
    [
        true, false, true, 0, 0, depthMeters, depthMeters,
        false, false, false, false, OneDegreeInRadians, OneDegreeInRadians,
        false, false, false, false, false, true, true, true, false, false,
        0, 0, false, false
    ];

    private static void RequireEmptyCollection(
        IReadOnlyDictionary<string, string> values,
        string name)
    {
        if (!values.TryGetValue(name, out var json) || string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var empty = root.ValueKind switch
        {
            JsonValueKind.Array => root.GetArrayLength() == 0,
            JsonValueKind.Object => !root.EnumerateObject().Any(),
            JsonValueKind.Null => true,
            _ => false
        };
        if (!empty)
        {
            throw Stage(
                PartFamilyFailureStages.SketchGeometryCreateFailed,
                $"Sketch {name} are outside the authorized adapter profile.");
        }
    }

    private static double Coordinate(
        IReadOnlyDictionary<string, string> values,
        string name)
    {
        if (!values.TryGetValue(name, out var text) ||
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value))
        {
            throw Stage(
                PartFamilyFailureStages.SketchGeometryCreateFailed,
                $"{name} must be a finite number.");
        }

        return value * MmToMeters;
    }

    private static double OptionalCoordinate(
        IReadOnlyDictionary<string, string> values,
        string name) =>
        values.ContainsKey(name) ? Coordinate(values, name) : 0d;

    private static double CircleRadius(IReadOnlyDictionary<string, string> values)
    {
        if (values.ContainsKey("radius_mm"))
        {
            return RequiredPositive(values, "radius_mm");
        }

        return RequiredPositiveAlias(
                   values,
                   "diameter_mm",
                   "hole_diameter_mm",
                   "outer_diameter_mm",
                   "inner_diameter_mm",
                   "bolt_hole_diameter_mm") /
               2d;
    }

    private static double RequiredPositive(
        IReadOnlyDictionary<string, string> values,
        string name) =>
        RequiredPositiveAlias(values, name);

    private static void RequireBlind(IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue("direction", out var direction) &&
            !direction.Equals("blind", StringComparison.OrdinalIgnoreCase))
        {
            throw Stage(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"direction={direction} is not authorized; only blind is supported.");
        }

        if (values.TryGetValue("through_all", out var throughAllText) &&
            bool.TryParse(throughAllText, out var throughAll) &&
            throughAll)
        {
            throw Stage(
                PartFamilyFailureStages.InvalidFeatureParameter,
                "through_all=true is not authorized; only a positive blind depth is supported.");
        }
    }

    private static double RequiredPositiveAlias(
        IReadOnlyDictionary<string, string> values,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var text) &&
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                double.IsFinite(value) &&
                value > 0)
            {
                return value;
            }
        }

        throw Stage(
            PartFamilyFailureStages.InvalidFeatureParameter,
            $"{string.Join("/", names)} must contain a finite positive number.");
    }

    private static FeatureAdapterException Stage(string stage, string message) =>
        new(stage, message);

    private sealed record SketchComArtifact(
        object Feature,
        IReadOnlySet<string> EntityTypes,
        IReadOnlyList<double> CircleDiametersMm);

    private enum VolumeChangeExpectation
    {
        Increase,
        Decrease
    }

    private sealed class FeatureAdapterException(string stage, string message) : Exception(message)
    {
        public string Stage { get; } = stage;
    }
}
