using AppCap.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AppCap.Tests.Diagnostics;

public sealed class WorkerLoggingTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"appcap-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ProviderWritesStructuredEntryWhenDisposed()
    {
        string path;
        using (RollingFileLoggerProvider provider = new(directory))
        {
            ILogger logger = provider.CreateLogger("AppCap.Tests");
            WorkerLog.Started(logger, 42, "test-path");
            path = provider.ActiveLogPath;
        }

        string entry = File.ReadAllText(path);

        Assert.Contains("level=Information", entry, StringComparison.Ordinal);
        Assert.Contains("eventId=1000", entry, StringComparison.Ordinal);
        Assert.Contains("category=AppCap.Tests", entry, StringComparison.Ordinal);
        Assert.Contains("message=Worker started. PID: 42; log: test-path", entry, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}