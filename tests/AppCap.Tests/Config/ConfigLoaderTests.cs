namespace AppCap.Tests;

public sealed class ConfigLoaderTests : IDisposable
{
    private readonly string directory;

    public ConfigLoaderTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "appcap-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadReadsApplicationId()
    {
        WriteConfig("""
            {
                "targets": {
                    "calculator": {
                        "id": "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"
                    }
                }
            }
            """);

        TargetCatalog catalog = ConfigLoader.Load(directory);

        Assert.True(catalog.TryParse("calculator", out TargetApplication application));
        Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", application.Id);
    }

    [Fact]
    public void DefaultIsFirstConfiguredTarget()
    {
        WriteConfig("""
            {
                "targets": {
                    "one": { "id": "First_pfn!App" },
                    "two": { "id": "Second_pfn!App" }
                }
            }
            """);

        TargetCatalog catalog = ConfigLoader.Load(directory);

        Assert.Equal("one", catalog.Default.Name);
    }

    [Fact]
    public void MissingFileThrowsFriendlyError()
    {
        AppCapException exception = Assert.Throws<AppCapException>(() => ConfigLoader.Load(directory));

        Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
        Assert.Contains("was not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedJsonThrowsFriendlyError()
    {
        WriteConfig("{ not valid json");

        AppCapException exception = Assert.Throws<AppCapException>(() => ConfigLoader.Load(directory));

        Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
        Assert.Contains("is not valid JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyTargetsThrowsFriendlyError()
    {
        WriteConfig("""{ "targets": {} }""");

        AppCapException exception = Assert.Throws<AppCapException>(() => ConfigLoader.Load(directory));

        Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
        Assert.Contains("does not define any targets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingIdThrowsFriendlyError()
    {
        WriteConfig("""{ "targets": { "broken": {} } }""");

        AppCapException exception = Assert.Throws<AppCapException>(() => ConfigLoader.Load(directory));

        Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
        Assert.Contains("is missing an \"id\" value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidIdThrowsFriendlyError()
    {
        WriteConfig("""{ "targets": { "broken": { "id": "NoBang" } } }""");

        AppCapException exception = Assert.Throws<AppCapException>(() => ConfigLoader.Load(directory));

        Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
        Assert.Contains("invalid AUMID", exception.Message, StringComparison.Ordinal);
    }

    private void WriteConfig(string contents) =>
        File.WriteAllText(Path.Combine(directory, ConfigLoader.FileName), contents);
}
