using System.Text.Json;
using SolidWorksWorker;
using SolidWorksWorker.Features;

if (!OperatingSystem.IsWindows() || args.Length != 2) return 1;
var com = new LateBoundSolidWorksComFacade();
var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (File.Exists(output)) throw new IOException("拒绝覆盖隔离诊断。");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var app = Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application")!)!;
object? model = null;
object Snapshot(object? doc) => new
{
    path = doc is null ? null : com.TryInvoke(doc, "GetPathName"),
    title = doc is null ? null : com.TryInvoke(doc, "GetTitle"),
    geometry = doc is null ? null : new RealSolidWorksGeometryReader(com).Read(doc).Geometry
};
try
{
    model = com.Invoke(app, "OpenDoc", input, 1);
    if (model is null || !string.Equals(com.TryInvoke(model, "GetPathName") as string, input, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("隔离诊断原模型身份错误。");
    var before = Snapshot(model);
    var activeBefore = Snapshot(com.TryGetProperty(app, "ActiveDoc"));
    object?[] activation = [Path.GetFileName(input), false, 1, -1];
    var activated = com.InvokeWithArgs(app, "ActivateDoc3", activation);
    var returned = Snapshot(activated);
    var activeAfter = Snapshot(com.TryGetProperty(app, "ActiveDoc"));
    com.Invoke(model, "ClearSelection2", true);
    var errors = new List<string>();
    var warnings = new List<string>();
    var saved = com.TryExtensionSaveAs(model, output, new System.Runtime.InteropServices.DispatchWrapper(null), errors, warnings);
    Console.WriteLine(JsonSerializer.Serialize(new { before, activeBefore, activationError=activation[3], returned, activeAfter,
        saved, errors, warnings, output, after=Snapshot(model) }, new JsonSerializerOptions { WriteIndented=true }));
    return saved && errors.Count==0 ? 0 : 2;
}
finally
{
    if (model is not null)
    {
        var title = com.TryInvoke(model, "GetTitle") as string;
        if (!string.IsNullOrWhiteSpace(title)) com.TryInvoke(app, "CloseDoc", title);
    }
}
