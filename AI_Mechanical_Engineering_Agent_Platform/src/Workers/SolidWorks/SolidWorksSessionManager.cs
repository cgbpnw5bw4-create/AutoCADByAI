using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DomainSchemas;

namespace SolidWorksWorker;

public interface ISolidWorksSessionManager
{
    Task<SolidWorksSessionConnectionResult> ConnectAsync(
        SolidWorksRuntimeOptions options,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

public sealed record SolidWorksSessionConnectionResult(
    bool Connected,
    string? SolidWorksVersion,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Logs);

public interface ISolidWorksComActivator
{
    object CreateApplication(CancellationToken cancellationToken);

    void SetVisible(object application, bool visible, CancellationToken cancellationToken);

    string? ReadVersion(object application, CancellationToken cancellationToken);

    void Release(object application);
}

public sealed class SolidWorksSessionManager : ISolidWorksSessionManager
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ISolidWorksComActivator _comActivator;
    private object? _application;

    public SolidWorksSessionManager()
        : this(new LateBoundSolidWorksComActivator())
    {
    }

    public SolidWorksSessionManager(ISolidWorksComActivator comActivator)
    {
        _comActivator = comActivator;
    }

    public async Task<SolidWorksSessionConnectionResult> ConnectAsync(
        SolidWorksRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Failed("solidworks_com_not_available: SolidWorks COM automation is only available on Windows.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.ConnectTimeoutSeconds));

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            ReleaseCurrentApplication();
            return await Task.Run(() => ConnectCore(options, timeout.Token), timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ReleaseCurrentApplication();
            return Failed("solidworks_connection_timeout: SolidWorks connection timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (COMException ex)
        {
            ReleaseCurrentApplication();
            return Failed($"solidworks_com_exception: {ex.Message}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            ReleaseCurrentApplication();
            return Failed($"solidworks_connection_failed: {ex.Message}");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            ReleaseCurrentApplication();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void ReleaseCurrentApplication()
    {
        if (_application is not null)
        {
            try
            {
                _comActivator.Release(_application);
            }
            finally
            {
                _application = null;
            }
        }
    }

    private SolidWorksSessionConnectionResult ConnectCore(
        SolidWorksRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        object? application = null;
        try
        {
            application = _comActivator.CreateApplication(cancellationToken);
            _comActivator.SetVisible(application, options.Visible, cancellationToken);
            var version = _comActivator.ReadVersion(application, cancellationToken);
            _application = application;
            application = null;

            return new SolidWorksSessionConnectionResult(
                Connected: true,
                version,
                Array.Empty<string>(),
                new[] { "SolidWorks COM session connected for smoke test only. No CAD build command was executed." });
        }
        finally
        {
            if (application is not null)
            {
                _comActivator.Release(application);
            }
        }
    }

    private static SolidWorksSessionConnectionResult Failed(string issue) =>
        new(
            Connected: false,
            SolidWorksVersion: null,
            new[] { issue },
            new[] { "SolidWorks COM session was not connected." });
}

public sealed class LateBoundSolidWorksComActivator : ISolidWorksComActivator
{
    public object CreateApplication(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

#pragma warning disable CA1416
        var applicationType = Type.GetTypeFromProgID("SldWorks.Application");
#pragma warning restore CA1416
        if (applicationType is null)
        {
            throw new InvalidOperationException("solidworks_com_not_available: SldWorks.Application ProgID was not found.");
        }

        return Activator.CreateInstance(applicationType)
            ?? throw new InvalidOperationException("SldWorks.Application could not be created.");
    }

    public void SetVisible(object application, bool visible, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            application.GetType().InvokeMember(
                "Visible",
                System.Reflection.BindingFlags.SetProperty,
                binder: null,
                target: application,
                args: new object[] { visible });
        }
        catch (Exception ex) when (ex is MissingMethodException or COMException)
        {
            // Some COM environments may restrict the Visible property; connection remains usable for smoke status.
        }
    }

    public string? ReadVersion(object application, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = application.GetType().InvokeMember(
                "RevisionNumber",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: application,
                args: Array.Empty<object>());
            return value?.ToString();
        }
        catch (Exception ex) when (ex is MissingMethodException or COMException)
        {
            return null;
        }
    }

    public void Release(object application)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ReleaseComObject(application);
    }

    [SupportedOSPlatform("windows")]
    private static void ReleaseComObject(object application)
    {
        if (Marshal.IsComObject(application))
        {
            Marshal.FinalReleaseComObject(application);
        }
    }
}
