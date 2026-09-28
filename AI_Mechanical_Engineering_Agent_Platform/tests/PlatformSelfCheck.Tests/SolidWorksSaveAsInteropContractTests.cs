using System.Runtime.InteropServices;
using SolidWorksWorker;

namespace PlatformSelfCheck.Tests;

public sealed class SolidWorksSaveAsInteropContractTests
{
    [Fact]
    public void NullExportDataUsesDispatchOnWindowsAndPreservesOutErrorsAndWarnings()
    {
        var model = new ManagedModel();
        var errors = new List<string>();
        var warnings = new List<string>();

        var result = new LateBoundSolidWorksComFacade().TryExtensionSaveAs(model, "test.STEP", null, errors, warnings);

        Assert.True(result);
        if (OperatingSystem.IsWindows())
        {
            Assert.Null(Assert.IsType<DispatchWrapper>(model.Extension.ExportData).WrappedObject);
        }
        else
        {
            Assert.Null(model.Extension.ExportData);
        }
        Assert.Equal(["save_as_errors: 8"], errors);
        Assert.Equal(["save_as_warnings: 4"], warnings);
    }

    [Fact]
    public void NonNullExportConfigurationRemainsTheOriginalObject()
    {
        var model = new ManagedModel();
        var exportData = new object();

        Assert.True(new LateBoundSolidWorksComFacade().TryExtensionSaveAs(model, "test.STEP", exportData, [], []));

        Assert.Same(exportData, model.Extension.ExportData);
    }

    // 托管替身只验证参数类型及 out 回写合同，不创建 COM 对象或 CAD 文件。
    public sealed class ManagedModel
    {
        public ManagedExtension Extension { get; } = new();
    }

    public sealed class ManagedExtension
    {
        public object? ExportData { get; private set; }

        public bool SaveAs(string path, int version, int options, object? exportData, out int errors, out int warnings)
        {
            ExportData = exportData;
            errors = 8;
            warnings = 4;
            return true;
        }
    }
}
