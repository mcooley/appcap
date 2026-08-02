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
        Assert.Equal([TimeSpan.Zero, TimeSpan.FromMilliseconds(500)], encoder.Samples.Select(sample => sample.Timestamp));
    }

    [Fact]
    public async Task TrimsOddWindowDimensionsForMp4Encoding()
    {
        string path = NewOutputPath();
        FakeRecordingEncoder encoder = new(writeOutput: true);
        FakeSurfaceComposer composer = new();
        using RecordingWriter writer = new(path, crop: null, encoder, composer);
        writer.AddFrame(new FakeFrame(1044, 671, TimeSpan.Zero));
        writer.CompleteFrames();

        await writer.StartAsync(1044, 671, CancellationToken.None);
        await writer.StopAsync(discard: false, CancellationToken.None);

        Assert.Equal((1044, 670), encoder.OutputSize);
        Assert.Equal([new CropRectangle(0, 0, 1044, 670)], composer.Crops);
    }

    [Fact]
    public async Task FitsResizedFramesIntoOriginalVideoDimensions()
    {
        string path = NewOutputPath();
        FakeRecordingEncoder encoder = new(writeOutput: true);
        FakeSurfaceComposer composer = new();
        using RecordingWriter writer = new(path, crop: null, encoder, composer);
        writer.AddFrame(new FakeFrame(640, 480, TimeSpan.Zero));
        writer.AddFrame(new FakeFrame(480, 640, TimeSpan.FromMilliseconds(500)));
        writer.AddFrame(new FakeFrame(1280, 360, TimeSpan.FromSeconds(1)));
        writer.CompleteFrames();

        await writer.StartAsync(640, 480, CancellationToken.None);
        await writer.StopAsync(discard: false, CancellationToken.None);

        Assert.Equal((640, 480), encoder.OutputSize);
        Assert.Equal([(480, 640, 640, 480), (1280, 360, 640, 480)], composer.Fits);
        Assert.All(encoder.Samples, sample => Assert.Equal((640, 480), sample.Size));
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

    [Fact]
    public async Task AlignsAudioToFirstVideoFrameAndPadsToVideoEnd()
    {
        string path = NewOutputPath();
        FakeRecordingEncoder encoder = new(writeOutput: true);
        using RecordingWriter writer = new(path, crop: null, encoder, new FakeSurfaceComposer(), includeAudio: true);
        TimeSpan origin = TimeSpan.FromSeconds(10);
        writer.AddAudioPacket(new RecordingAudioPacket(
            Enumerable.Repeat((byte)7, 882 * ProcessLoopbackAudioCapture.BytesPerFrame).ToArray(),
            882,
            origin - TimeSpan.FromMilliseconds(10),
            Discontinuous: false,
            TimestampError: false));
        writer.AddFrame(new FakeFrame(640, 480, origin));
        writer.AddFrame(new FakeFrame(640, 480, origin + TimeSpan.FromMilliseconds(20)));
        writer.CompleteFrames();
        writer.CompleteAudio();

        await writer.StartAsync(640, 480, CancellationToken.None);
        await writer.StopAsync(discard: false, CancellationToken.None);

        Assert.Equal([TimeSpan.Zero, TimeSpan.FromMilliseconds(20)], encoder.Samples.Select(sample => sample.Timestamp));
        Assert.Equal(2, encoder.AudioSamples.Count);
        Assert.Equal(TimeSpan.Zero, encoder.AudioSamples[0].Timestamp);
        Assert.Equal(441u, encoder.AudioSamples[0].FrameCount);
        Assert.All(encoder.AudioSamples[0].Data, value => Assert.Equal(7, value));
        Assert.Equal(TimeSpan.FromMilliseconds(10), encoder.AudioSamples[1].Timestamp);
        Assert.Equal(441u, encoder.AudioSamples[1].FrameCount);
        Assert.All(encoder.AudioSamples[1].Data, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task ExtendsStaticVideoToAudioEnd()
    {
        string path = NewOutputPath();
        FakeRecordingEncoder encoder = new(writeOutput: true);
        using RecordingWriter writer = new(path, crop: null, encoder, new FakeSurfaceComposer(), includeAudio: true);
        TimeSpan origin = TimeSpan.FromSeconds(10);
        writer.AddFrame(new FakeFrame(640, 480, origin));
        writer.AddAudioPacket(new RecordingAudioPacket(
            new byte[882 * ProcessLoopbackAudioCapture.BytesPerFrame],
            882,
            origin,
            Discontinuous: false,
            TimestampError: false));
        writer.CompleteFrames();
        writer.CompleteAudio();

        await writer.StartAsync(640, 480, CancellationToken.None);
        await writer.StopAsync(discard: false, CancellationToken.None);

        Assert.Equal([TimeSpan.Zero, TimeSpan.FromMilliseconds(20)], encoder.Samples.Select(sample => sample.Timestamp));
        RecordingAudioSample audio = Assert.Single(encoder.AudioSamples);
        Assert.Equal(TimeSpan.Zero, audio.Timestamp);
        Assert.Equal(TimeSpan.FromMilliseconds(20), audio.Duration);
    }

    private static string NewOutputPath() => Path.Combine(Path.GetTempPath(), "appcap-writer-tests", Guid.NewGuid().ToString("N"), "recording.mp4");

    private sealed class FakeRecordingEncoder(bool writeOutput) : IRecordingEncoder
    {
        public List<RecordedSample> Samples { get; } = [];

        public List<RecordingAudioSample> AudioSamples { get; } = [];

        public (int Width, int Height) OutputSize { get; private set; }

        public Task EncodeAsync(
            string outputPath,
            int width,
            int height,
            Func<RecordingSample?> getNextVideoSample,
            Func<RecordingAudioSample?>? getNextAudioSample,
            CancellationToken cancellationToken) => Task.Run(() =>
        {
            OutputSize = (width, height);
            RecordingSample? sample;
            while ((sample = getNextVideoSample()) is not null)
            {
                using (sample.Surface)
                {
                    Samples.Add(new RecordedSample(sample.Timestamp, sample.CaptionOpacity, (sample.Surface.Width, sample.Surface.Height)));
                }
            }

            if (getNextAudioSample is not null)
            {
                RecordingAudioSample? audioSample;
                while ((audioSample = getNextAudioSample()) is not null)
                {
                    AudioSamples.Add(audioSample);
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

        public List<(int SourceWidth, int SourceHeight, int Width, int Height)> Fits { get; } = [];

        public List<string> Captions { get; } = [];

        public IRecordingSurface Copy(IRecordingSurface surface) => new FakeSurface(surface.Width, surface.Height);

        public IRecordingSurface Crop(IRecordingSurface surface, CropRectangle crop)
        {
            Crops.Add(crop);
            return new FakeSurface(crop.Width, crop.Height);
        }

        public IRecordingSurface Fit(IRecordingSurface surface, int width, int height)
        {
            Fits.Add((surface.Width, surface.Height, width, height));
            return new FakeSurface(width, height);
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

    private sealed record RecordedSample(TimeSpan Timestamp, float CaptionOpacity, (int Width, int Height) Size);
}