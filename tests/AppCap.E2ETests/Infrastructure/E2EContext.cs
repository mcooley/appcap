using System.Diagnostics;

namespace AppCap.E2ETests;

public sealed class E2EContext
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(40);
    private static readonly Lazy<E2EContext> CurrentContext = new(E2EHelpers.CreateContext);

    internal E2EContext(string target, string secondaryTarget, string executablePath, string outputDirectory)
    {
        Target = target;
        SecondaryTarget = secondaryTarget;
        ExecutablePath = executablePath;
        OutputDirectory = outputDirectory;
    }

    public static E2EContext Current => CurrentContext.Value;

    public string Target { get; }

    public string SecondaryTarget { get; }

    public string ExecutablePath { get; }

    public string OutputDirectory { get; }

    public string NewOutputPath(string fileName)
    {
        Directory.CreateDirectory(OutputDirectory);
        return Path.Combine(OutputDirectory, fileName);
    }

    internal CommandResult Run(params string[] arguments)
        => RunFor(Target, arguments);

    internal CommandResult RunFor(string target, params string[] arguments)
    {
        List<string> fullArguments = ["--target", target];
        fullArguments.AddRange(arguments);
        return RunCore(fullArguments);
    }

    internal CommandResult RunUnscoped(params string[] arguments) => RunCore(arguments);

    private CommandResult RunCore(IReadOnlyList<string> fullArguments)
    {
        string standardOutputPath = NewOutputPath(Guid.NewGuid().ToString("N") + ".stdout.txt");
        string standardErrorPath = NewOutputPath(Guid.NewGuid().ToString("N") + ".stderr.txt");
        string scriptPath = NewOutputPath(Guid.NewGuid().ToString("N") + ".cmd");
        string commandLine = QuoteForCmd(ExecutablePath) + " " + string.Join(" ", fullArguments.Select(QuoteForCmd)) +
            " 1>" + QuoteForCmd(standardOutputPath) + " 2>" + QuoteForCmd(standardErrorPath);
        File.WriteAllText(scriptPath, "@echo off" + Environment.NewLine + commandLine + Environment.NewLine + "exit /b %ERRORLEVEL%" + Environment.NewLine);

        ProcessStartInfo startInfo = new()
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(scriptPath);

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("appcap process could not be started.");
        if (!process.WaitForExit((int)CommandTimeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"appcap command timed out after {CommandTimeout.TotalSeconds} seconds: {string.Join(' ', fullArguments)}");
        }

        string standardOutput = ReadTextIfExists(standardOutputPath);
        string standardError = ReadTextIfExists(standardErrorPath);
        return new CommandResult(process.ExitCode, standardOutput, standardError);
    }

    private static string QuoteForCmd(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string ReadTextIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}

public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(E2EHelpers.GetExecutablePathEnvironmentVariable()))
        {
            Skip = "Set APPCAP_E2E_EXECUTABLE to a previously-built AppCap.exe path to run end-to-end tests.";
        }
    }
}

internal sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public void AssertSuccess()
    {
        Assert.True(ExitCode == 0, $"Expected exit code 0, got {ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{StandardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{StandardError}");
    }
}

internal sealed record ImageInfo(int Width, int Height, long Length);

internal sealed record VideoInfo(int Width, int Height, TimeSpan Duration);

internal sealed record PixelColor(byte Red, byte Green, byte Blue);

internal sealed class ImagePixels
{
    private readonly byte[] bgraPixels;

    public ImagePixels(int width, int height, byte[] bgraPixels)
    {
        Width = width;
        Height = height;
        this.bgraPixels = bgraPixels;
    }

    public int Width { get; }

    public int Height { get; }

    public PixelColor GetPixel(int x, int y)
    {
        int index = ((Width * y) + x) * 4;
        return new PixelColor(bgraPixels[index + 2], bgraPixels[index + 1], bgraPixels[index]);
    }
}
