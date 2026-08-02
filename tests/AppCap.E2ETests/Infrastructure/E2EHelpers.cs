using System.Diagnostics;
using System.Text;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
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
        return new E2EContext("testapp", "testapp-secondary", deployedExecutablePath, outputDirectory);
    }

    internal static string? GetExecutablePathEnvironmentVariable() =>
        Environment.GetEnvironmentVariable("APPCAP_E2E_EXECUTABLE");

    internal static string GetRequiredExecutablePath()
    {
        string? executablePath = GetExecutablePathEnvironmentVariable();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Set APPCAP_E2E_EXECUTABLE to a previously-built appcap.exe path to run end-to-end tests.");
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
        IRandomAccessStream thumbnail;
        try
        {
            thumbnail = await composition.GetThumbnailAsync(position, info.Width, info.Height, VideoFramePrecision.NearestFrame).AsTask().ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"Could not read video frame at {position}; clip duration is {clip.OriginalDuration}, composition duration is {composition.Duration}, and file video duration is {info.Duration}.",
                exception);
        }

        using IRandomAccessStream stream = thumbnail;
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

    public static async Task<AudioInfo> ReadAudioInfoAsync(string path)
    {
        StorageFile source = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
        MediaClip clip = await MediaClip.CreateFromFileAsync(source).AsTask().ConfigureAwait(false);
        if (clip.EmbeddedAudioTracks.Count == 0)
        {
            return new AudioInfo(false, TimeSpan.Zero, 0, 0);
        }

        string decodeDirectory = Path.Combine(Path.GetTempPath(), "appcap-e2e-audio");
        Directory.CreateDirectory(decodeDirectory);
        string decodedPath = Path.Combine(decodeDirectory, Guid.NewGuid().ToString("N") + ".wav");
        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(decodeDirectory).AsTask().ConfigureAwait(false);
        StorageFile destination = await folder.CreateFileAsync(Path.GetFileName(decodedPath), CreationCollisionOption.ReplaceExisting).AsTask().ConfigureAwait(false);
        try
        {
            MediaTranscoder transcoder = new();
            MediaEncodingProfile profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
            PrepareTranscodeResult prepared = await transcoder.PrepareFileTranscodeAsync(source, destination, profile).AsTask().ConfigureAwait(false);
            if (!prepared.CanTranscode)
            {
                throw new InvalidOperationException($"Could not decode recording audio: {prepared.FailureReason}.");
            }

            await prepared.TranscodeAsync().AsTask().ConfigureAwait(false);
            return AnalyzePcmWave(decodedPath);
        }
        finally
        {
            File.Delete(decodedPath);
        }
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

    public static IReadOnlyList<string> WaitForTestAppWindowTitles(int expectedCount, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string[] titles = [];
        while (stopwatch.Elapsed < timeout)
        {
            titles = Process.GetProcessesByName("AppCap.TestApp")
                .Select(process =>
                {
                    using (process)
                    {
                        return process.MainWindowTitle;
                    }
                })
                .Where(title => !string.IsNullOrEmpty(title))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (titles.Length == expectedCount)
            {
                return titles;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"Expected {expectedCount} E2E test app windows within {timeout.TotalSeconds} seconds. Last titles: '{string.Join("', '", titles)}'.");
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

    private static AudioInfo AnalyzePcmWave(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new(stream, Encoding.ASCII);
        if (ReadFourCc(reader) != "RIFF" || reader.ReadUInt32() > stream.Length || ReadFourCc(reader) != "WAVE")
        {
            throw new InvalidDataException("Decoded audio is not a valid RIFF WAVE file.");
        }

        ushort formatTag = 0;
        ushort bitsPerSample = 0;
        uint averageBytesPerSecond = 0;
        byte[]? audioData = null;
        while (stream.Position + 8 <= stream.Length)
        {
            string chunkId = ReadFourCc(reader);
            uint chunkSize = reader.ReadUInt32();
            long nextChunk = checked(stream.Position + chunkSize + (chunkSize & 1));
            if (nextChunk > stream.Length)
            {
                throw new InvalidDataException("Decoded audio contains an invalid WAVE chunk.");
            }

            if (chunkId == "fmt ")
            {
                formatTag = reader.ReadUInt16();
                _ = reader.ReadUInt16();
                _ = reader.ReadUInt32();
                averageBytesPerSecond = reader.ReadUInt32();
                _ = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
            }
            else if (chunkId == "data")
            {
                audioData = reader.ReadBytes(checked((int)chunkSize));
            }

            stream.Position = nextChunk;
        }

        if (formatTag != 1 || bitsPerSample != 16 || averageBytesPerSecond == 0 || audioData is null)
        {
            throw new InvalidDataException($"Expected decoded 16-bit PCM audio, found format {formatTag} with {bitsPerSample} bits per sample.");
        }

        double squaredSum = 0;
        double peak = 0;
        int sampleCount = audioData.Length / sizeof(short);
        for (int index = 0; index < sampleCount; index++)
        {
            double sample = BitConverter.ToInt16(audioData, index * sizeof(short)) / 32768.0;
            squaredSum += sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }

        double rootMeanSquare = sampleCount == 0 ? 0 : Math.Sqrt(squaredSum / sampleCount);
        TimeSpan duration = TimeSpan.FromSeconds((double)audioData.Length / averageBytesPerSecond);
        return new AudioInfo(true, duration, rootMeanSquare, peak);
    }

    private static string ReadFourCc(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));
}
