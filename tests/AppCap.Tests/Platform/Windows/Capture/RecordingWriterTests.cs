using AppCap.Windows;

namespace AppCap.Tests;

public sealed class RecordingWriterTests
{
    [Fact]
    public async Task WritesCroppedFramesWithCaptureTimestamps()
    {
        string path = NewOutputPath();
        FakeRecordingEncoder encoder = new(writeOutput: true);
        FakeSurfaceComposer composer = new();
        using RecordingWriter writer = new(path, new CropRectangle(10, 20, 320, 240), encoder, composer);
        writer.AddFrame(new FakeFrame(640, 480, TimeSpan.FromMilliseconds(250)));
        writer.AddFrame(new FakeFrame(640, 480, TimeSpan.FromMilliseconds(750)));
        writer.CompleteFrames();

        await writer.StartAsync(640, 480, CancellationToken.None);
        await writer.StopAsync(discard: false, CancellationToken.None);

        Assert.Equal((320, 240), encoder.OutputSize);
        Assert.Equal(2, composer.Crops.Count);
        Assert.All(composer.Crops, crop => Assert.Equal(new CropRectangle(10, 20, 320, 240), crop));
        Assert.Equal([TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(750)], encoder.Samples.Select(sample => sample.Timestamp));
    }

    [Fact]
    public async Task FinishesCaptionFadeAndWritesBlankTailAfterCaptureCompletes()
    {
        string path = NewOutputPath();
        FakeRecordingEncoder encoder = new(writeOutput: true);
        FakeSurfaceComposer composer = new();
        using RecordingWriter writer = new(path, crop: null, encoder, composer);
        writer.AddCaption("Caption");
        writer.AddFrame(new FakeFrame(640, 480, TimeSpan.Zero));
        writer.CompleteFrames();

        await writer.StartAsync(640, 480, CancellationToken.None);
        await writer.StopAsync(discard: false, CancellationToken.None);

        Assert.Single(composer.Captions);
        Assert.Equal(1, encoder.Samples[0].CaptionOpacity);
        Assert.Equal(1, encoder.Samples.Single(sample => sample.Timestamp == TimeSpan.FromSeconds(3)).CaptionOpacity);
        Assert.Equal(0.8f, encoder.Samples.Single(sample => sample.Timestamp == TimeSpan.FromSeconds(3.1)).CaptionOpacity, precision: 3);
        Assert.Equal(0, encoder.Samples.Single(sample => sample.Timestamp == TimeSpan.FromSeconds(3.5)).CaptionOpacity);
        Assert.Equal(
            [TimeSpan.FromSeconds(3.5), TimeSpan.FromSeconds(3.6), TimeSpan.FromSeconds(3.7)],
            encoder.Samples.TakeLast(3).Select(sample => sample.Timestamp));
        Assert.All(encoder.Samples.TakeLast(3), sample => Assert.Equal(0, sample.CaptionOpacity));
    }

    [Fact]
    public async Task StopFailsWhenEncoderDoesNotWriteOutput()
    {
        string path = NewOutputPath();
        FakeRecordingEncoder encoder = new(writeOutput: false);
        using RecordingWriter writer = new(path, crop: null, encoder, new FakeSurfaceComposer());
        writer.AddFrame(new FakeFrame(640, 480, TimeSpan.Zero));
        writer.CompleteFrames();
        await writer.StartAsync(640, 480, CancellationToken.None);

        AppCapException exception = await Assert.ThrowsAsync<AppCapException>(
            () => writer.StopAsync(discard: false, CancellationToken.None));

        Assert.Contains("did not produce an output file", exception.Message, StringComparison.Ordinal);
    }

    private static string NewOutputPath() => Path.Combine(Path.GetTempPath(), "appcap-writer-tests", Guid.NewGuid().ToString("N"), "recording.mp4");

    private sealed class FakeRecordingEncoder(bool writeOutput) : IRecordingEncoder
    {
        public List<RecordedSample> Samples { get; } = [];

        public (int Width, int Height) OutputSize { get; private set; }

        public Task EncodeAsync(string outputPath, int width, int height, Func<RecordingSample?> getNextSample, CancellationToken cancellationToken) => Task.Run(() =>
        {
            OutputSize = (width, height);
            RecordingSample? sample;
            while ((sample = getNextSample()) is not null)
            {
                using (sample.Surface)
                {
                    Samples.Add(new RecordedSample(sample.Timestamp, sample.CaptionOpacity));
                }
            }

            if (writeOutput)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, "recording");
            }
        }, cancellationToken);

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeSurfaceComposer : IRecordingSurfaceComposer
    {
        public List<CropRectangle> Crops { get; } = [];

        public List<string> Captions { get; } = [];

        public IRecordingSurface Copy(IRecordingSurface surface) => new FakeSurface(surface.Width, surface.Height);

        public IRecordingSurface Crop(IRecordingSurface surface, CropRectangle crop)
        {
            Crops.Add(crop);
            return new FakeSurface(crop.Width, crop.Height);
        }

        public IRecordingCaption CreateCaption(int width, int height, string text)
        {
            Captions.Add(text);
            return new FakeCaption(width, height);
        }
    }

    private sealed class FakeCaption(int width, int height) : IRecordingCaption
    {
        public IRecordingSurface Render(IRecordingSurface surface, float opacity) => new FakeSurface(width, height);

        public void Dispose()
        {
        }
    }

    private sealed class FakeFrame(int width, int height, TimeSpan timestamp) : IRecordingFrame
    {
        public IRecordingSurface Surface { get; } = new FakeSurface(width, height);

        public TimeSpan Timestamp { get; } = timestamp;

        public void Dispose() => Surface.Dispose();
    }

    private sealed class FakeSurface(int width, int height) : IRecordingSurface
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public void Dispose()
        {
        }
    }

    private sealed record RecordedSample(TimeSpan Timestamp, float CaptionOpacity);
}