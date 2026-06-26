using System.Diagnostics;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace AppCap.E2ETests;

internal static class E2EHelpers
{
    public static E2EContext CreateContext()
    {
        if (!IsEnabled(Environment.GetEnvironmentVariable("APPCAP_E2E")))
        {
            throw new InvalidOperationException("Set APPCAP_E2E=1 to run end-to-end tests.");
        }

        string? executablePath = Environment.GetEnvironmentVariable("APPCAP_E2E_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Set APPCAP_E2E_EXECUTABLE to a previously-built AppCap.exe path.");
        }

        string fullExecutablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullExecutablePath))
        {
            throw new InvalidOperationException($"APPCAP_E2E_EXECUTABLE does not exist: {fullExecutablePath}");
        }

        string outputDirectory = Path.Combine(Path.GetTempPath(), "appcap-e2e", Guid.NewGuid().ToString("N"));
        return new E2EContext("testapp", fullExecutablePath, outputDirectory);
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

    public static ShellProperties ReadShellProperties(string path)
    {
        Type shellType = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell.Application COM object is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Shell.Application COM object could not be created.");
        dynamic folder = shell.Namespace(Path.GetDirectoryName(path));
        dynamic item = folder.ParseName(Path.GetFileName(path));

        string title = string.Empty;
        string comments = string.Empty;
        for (int index = 0; index <= 320; index++)
        {
            string name = folder.GetDetailsOf(null, index);
            string value = folder.GetDetailsOf(item, index);
            if (string.Equals(name, "Title", StringComparison.OrdinalIgnoreCase))
            {
                title = value;
            }
            else if (string.Equals(name, "Comments", StringComparison.OrdinalIgnoreCase))
            {
                comments = value;
            }
        }

        return new ShellProperties(title, comments);
    }

    public static bool IsEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

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