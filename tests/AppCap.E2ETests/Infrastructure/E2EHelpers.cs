using System.Diagnostics;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Storage.FileProperties;
using Windows.Storage;
using Windows.Storage.Streams;

namespace AppCap.E2ETests;

internal static class E2EHelpers
{
    public static E2EContext CreateContext()
    {
        string executablePath = GetRequiredExecutablePath();

        string fullExecutablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullExecutablePath))
        {
            throw new InvalidOperationException($"APPCAP_E2E_EXECUTABLE does not exist: {fullExecutablePath}");
        }

        string deployedExecutablePath = DeployExecutable(fullExecutablePath);

        string outputDirectory = Path.Combine(Path.GetTempPath(), "appcap-e2e", Guid.NewGuid().ToString("N"));
        return new E2EContext("testapp", deployedExecutablePath, outputDirectory);
    }

    internal static string? GetExecutablePathEnvironmentVariable() =>
        Environment.GetEnvironmentVariable("APPCAP_E2E_EXECUTABLE");

    internal static string GetRequiredExecutablePath()
    {
        string? executablePath = GetExecutablePathEnvironmentVariable();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Set APPCAP_E2E_EXECUTABLE to a previously-built AppCap.exe path to run end-to-end tests.");
        }

        return executablePath;
    }

    private static string DeployExecutable(string executablePath)
    {
        string? sourceDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrEmpty(sourceDirectory))
        {
            throw new InvalidOperationException($"Could not determine the directory for APPCAP_E2E_EXECUTABLE: {executablePath}");
        }

        string configSource = Path.Combine(AppContext.BaseDirectory, "appcap.config.json");
        if (!File.Exists(configSource))
        {
            throw new InvalidOperationException($"E2E configuration file was not found: {configSource}");
        }

        string deployDirectory = Path.Combine(Path.GetTempPath(), "appcap-e2e-bin", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(deployDirectory);

        foreach (string filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(filePath, Path.Combine(deployDirectory, Path.GetFileName(filePath)), overwrite: true);
        }

        File.Copy(configSource, Path.Combine(deployDirectory, "appcap.config.json"), overwrite: true);

        return Path.Combine(deployDirectory, Path.GetFileName(executablePath));
    }

    public static async Task<ImageInfo> ReadImageInfoAsync(string path)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
        using IRandomAccessStream stream = await file.OpenReadAsync().AsTask().ConfigureAwait(false);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
        return new ImageInfo((int)decoder.PixelWidth, (int)decoder.PixelHeight, new FileInfo(path).Length);
    }

    public static async Task<PixelColor> ReadPixelAsync(string path, int x, int y)
    {
        ImagePixels image = await ReadPixelsAsync(path).ConfigureAwait(false);
        return image.GetPixel(x, y);
    }

    public static async Task<PixelColor> ReadVideoPixelAsync(string path, TimeSpan position, int x, int y)
    {
        ImagePixels pixels = await ReadVideoPixelsAsync(path, position).ConfigureAwait(false);
        return pixels.GetPixel(x, y);
    }

    public static async Task<ImagePixels> ReadVideoPixelsAsync(string path, TimeSpan position)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
        MediaClip clip = await MediaClip.CreateFromFileAsync(file).AsTask().ConfigureAwait(false);
        MediaComposition composition = new();
        composition.Clips.Add(clip);
        VideoInfo info = await ReadVideoInfoAsync(path).ConfigureAwait(false);
        using IRandomAccessStream stream = await composition.GetThumbnailAsync(position, info.Width, info.Height, VideoFramePrecision.NearestFrame).AsTask().ConfigureAwait(false);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
        PixelDataProvider data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask().ConfigureAwait(false);
        return new ImagePixels((int)decoder.PixelWidth, (int)decoder.PixelHeight, data.DetachPixelData());
    }

    public static async Task<VideoInfo> ReadVideoInfoAsync(string path)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
        VideoProperties properties = await file.Properties.GetVideoPropertiesAsync().AsTask().ConfigureAwait(false);
        return new VideoInfo((int)properties.Width, (int)properties.Height, properties.Duration);
    }

    public static async Task<TimeSpan> ReadVideoDurationAsync(string path)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
        MediaClip clip = await MediaClip.CreateFromFileAsync(file).AsTask().ConfigureAwait(false);
        MediaComposition composition = new();
        composition.Clips.Add(clip);
        return composition.Duration;
    }

    public static async Task<ImagePixels> ReadPixelsAsync(string path)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
        using IRandomAccessStream stream = await file.OpenReadAsync().AsTask().ConfigureAwait(false);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
        PixelDataProvider data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask().ConfigureAwait(false);
        return new ImagePixels((int)decoder.PixelWidth, (int)decoder.PixelHeight, data.DetachPixelData());
    }

    public static void CloseTestAppProcesses()
    {
        foreach (Process process in Process.GetProcessesByName("AppCap.TestApp"))
        {
            using (process)
            {
                CloseTestAppProcess(process);
            }
        }
    }

    public static string WaitForTestAppWindowTitle(TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string title = string.Empty;
        while (stopwatch.Elapsed < timeout)
        {
            foreach (Process process in Process.GetProcessesByName("AppCap.TestApp"))
            {
                using (process)
                {
                    title = process.MainWindowTitle;
                    if (title.Contains(" | typed:", StringComparison.Ordinal))
                    {
                        return title;
                    }
                }
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"The E2E test app did not report typed text within {timeout.TotalSeconds} seconds. Last window title: '{title}'.");
    }

    public static void CloseAppCapProcesses()
    {
        foreach (Process process in Process.GetProcessesByName("AppCap"))
        {
            using (process)
            {
                CloseTestAppProcess(process);
            }
        }
    }

    private static void CloseTestAppProcess(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            if (process.CloseMainWindow() && process.WaitForExit(2_000))
            {
                return;
            }

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _ = process.WaitForExit(5_000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}