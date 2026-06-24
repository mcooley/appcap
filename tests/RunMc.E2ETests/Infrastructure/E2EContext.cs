using System.Diagnostics;

namespace RunMc.E2ETests;

public sealed class E2EContext
{
    private static readonly Lazy<E2EContext> CurrentContext = new(E2EHelpers.CreateContext);

    internal E2EContext(string target, string executablePath, string outputDirectory)
    {
        Target = target;
        ExecutablePath = executablePath;
        OutputDirectory = outputDirectory;
    }

    public static E2EContext Current => CurrentContext.Value;

    public string Target { get; }

    public string ExecutablePath { get; }

    public string OutputDirectory { get; }

    public string NewOutputPath(string fileName)
    {
        Directory.CreateDirectory(OutputDirectory);
        return Path.Combine(OutputDirectory, fileName);
    }

    internal CommandResult Run(params string[] arguments)
    {
        List<string> fullArguments = ["--target", Target];
        fullArguments.AddRange(arguments);

        ProcessStartInfo startInfo = new()
        {
            FileName = ExecutablePath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (string argument in fullArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("runmc process could not be started.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new CommandResult(process.ExitCode, standardOutput, standardError);
    }
}

public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (!E2EHelpers.IsEnabled(Environment.GetEnvironmentVariable("RUNMC_E2E")))
        {
            Skip = "Set RUNMC_E2E=1 to run end-to-end tests.";
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

internal sealed record ShellProperties(string Title, string Comments);
