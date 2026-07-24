using System.Collections.Concurrent;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Media.Core;
using global::Windows.Media.MediaProperties;
using global::Windows.Media.Transcoding;
using global::Windows.Storage;
using global::Windows.Storage.Streams;

namespace AppCap.Windows;

internal sealed class RecordingWriter : IDisposable
{
    internal static readonly TimeSpan CaptionSampleInterval = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan CaptionVisibleDuration = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan CaptionFadeDuration = TimeSpan.FromMilliseconds(500);
    private const int CaptionTailSampleCount = 2;

    private readonly string outputPath;
    private readonly CropRectangle? crop;
    private readonly IRecordingEncoder encoder;
    private readonly IRecordingSurfaceComposer composer;
    private readonly BlockingCollection<IRecordingFrame> frames = new(new ConcurrentQueue<IRecordingFrame>());
    private readonly TaskCompletionSource firstFrameArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object captionGate = new();
    private Task encodeTask = Task.CompletedTask;
    private IRecordingSurface? latestSurface;
    private IRecordingCaption? caption;
    private TimeSpan captionStartTime;
    private TimeSpan lastSampleTime;
    private string? pendingCaption;
    private int captionVersion;
    private int renderedCaptionVersion = -1;
    private int captionTailSamples;
    private bool started;
    private bool disposed;

    public RecordingWriter(string outputPath, CropRectangle? crop)
        : this(outputPath, crop, new MediaRecordingEncoder(), new Direct3DRecordingSurfaceComposer())
    {
    }

    internal RecordingWriter(string outputPath, CropRectangle? crop, IRecordingEncoder encoder, IRecordingSurfaceComposer composer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        this.outputPath = Path.GetFullPath(outputPath);
        this.crop = crop;
        this.encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        this.composer = composer ?? throw new ArgumentNullException(nameof(composer));
    }

    public Task Completion => encodeTask;

    public void AddFrame(IRecordingFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        try
        {
            frames.Add(frame);
            firstFrameArrived.TrySetResult();
        }
        catch (InvalidOperationException)
        {
            frame.Dispose();
        }
    }

    public void AddCaption(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        lock (captionGate)
        {
            pendingCaption = text;
            captionVersion++;
        }
    }

    public async Task StartAsync(int sourceWidth, int sourceHeight, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started)
        {
            throw new InvalidOperationException("The recording writer has already started.");
        }

        started = true;
        (int width, int height) = GetOutputSize(sourceWidth, sourceHeight);
        encodeTask = encoder.EncodeAsync(outputPath, width, height, GetNextSample, cancellationToken);

        Task confirmed = await Task.WhenAny(firstFrameArrived.Task, encodeTask).ConfigureAwait(false);
        if (confirmed == encodeTask)
        {
            await encodeTask.ConfigureAwait(false);
            throw new AppCapException("Recording did not capture any frames.");
        }
    }

    public async Task StopAsync(bool discard, CancellationToken cancellationToken)
    {
        if (discard)
        {
            encoder.Cancel();
        }

        CompleteFrames();
        try
        {
            await encodeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (discard)
        {
            DeleteOutputFile();
            return;
        }
        catch
        {
            DeleteOutputFile();
            throw;
        }

        if (discard)
        {
            DeleteOutputFile();
        }
        else
        {
            EnsureOutputFileExists();
        }
    }

    public void CompleteFrames()
    {
        try
        {
            frames.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CompleteFrames();
        caption?.Dispose();
        latestSurface?.Dispose();
        while (frames.TryTake(out IRecordingFrame? frame))
        {
            frame.Dispose();
        }

        frames.Dispose();
        encoder.Dispose();
    }

    private RecordingSample? GetNextSample()
    {
        while (true)
        {
            IRecordingFrame? frame = TakeNextFrame(latestSurface is null ? null : CaptionSampleInterval);
            if (frame is not null)
            {
                using (frame)
                {
                    latestSurface?.Dispose();
                    latestSurface = crop is { } cropRectangle
                        ? composer.Crop(frame.Surface, cropRectangle)
                        : composer.Copy(frame.Surface);
                    lastSampleTime = frame.Timestamp;
                    ApplyPendingCaption(frame.Timestamp, latestSurface.Width, latestSurface.Height);
                    float opacity = GetCaptionOpacity(frame.Timestamp);
                    if (caption is not null && opacity == 0)
                    {
                        caption.Dispose();
                        caption = null;
                        captionTailSamples = CaptionTailSampleCount;
                    }

                    return CreateSample(frame.Timestamp, opacity);
                }
            }

            if (latestSurface is null)
            {
                return null;
            }

            TimeSpan timestamp = lastSampleTime + CaptionSampleInterval;
            ApplyPendingCaption(timestamp, latestSurface.Width, latestSurface.Height);
            if (caption is not null)
            {
                float opacity = GetCaptionOpacity(timestamp);
                if (opacity == 0)
                {
                    caption.Dispose();
                    caption = null;
                    captionTailSamples = CaptionTailSampleCount;
                }

                lastSampleTime = timestamp;
                return CreateSample(timestamp, opacity);
            }

            if (captionTailSamples > 0)
            {
                captionTailSamples--;
                lastSampleTime = timestamp;
                return CreateSample(timestamp, 0);
            }

            if (frames.IsCompleted)
            {
                return null;
            }
        }
    }

    private IRecordingFrame? TakeNextFrame(TimeSpan? timeout)
    {
        try
        {
            return timeout is { } value
                ? frames.TryTake(out IRecordingFrame? frame, value) ? frame : null
                : frames.Take();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private RecordingSample CreateSample(TimeSpan timestamp, float captionOpacity)
    {
        if (latestSurface is null)
        {
            throw new InvalidOperationException("A recording sample requires a captured surface.");
        }

        IRecordingSurface surface = caption is null
            ? composer.Copy(latestSurface)
            : caption.Render(latestSurface, captionOpacity);
        return new RecordingSample(surface, timestamp, captionOpacity);
    }

    private void ApplyPendingCaption(TimeSpan frameTime, int width, int height)
    {
        string? text;
        int version;
        lock (captionGate)
        {
            text = pendingCaption;
            version = captionVersion;
        }

        if (version == renderedCaptionVersion)
        {
            return;
        }

        caption?.Dispose();
        caption = string.IsNullOrWhiteSpace(text) ? null : composer.CreateCaption(width, height, text);
        renderedCaptionVersion = version;
        captionTailSamples = 0;
        captionStartTime = frameTime;
    }

    private float GetCaptionOpacity(TimeSpan frameTime)
    {
        if (caption is null)
        {
            return 0;
        }

        TimeSpan elapsed = frameTime - captionStartTime;
        if (elapsed <= CaptionVisibleDuration)
        {
            return 1;
        }

        if (elapsed >= CaptionVisibleDuration + CaptionFadeDuration)
        {
            return 0;
        }

        return (float)((CaptionVisibleDuration + CaptionFadeDuration - elapsed).TotalMilliseconds / CaptionFadeDuration.TotalMilliseconds);
    }

    private (int Width, int Height) GetOutputSize(int sourceWidth, int sourceHeight)
    {
        if (crop is not { } cropRectangle)
        {
            return (sourceWidth, sourceHeight);
        }

        cropRectangle.ValidateWithin(sourceWidth, sourceHeight);
        return (cropRectangle.Width, cropRectangle.Height);
    }

    private void EnsureOutputFileExists()
    {
        FileInfo file = new(outputPath);
        if (!file.Exists || file.Length == 0)
        {
            throw new AppCapException($"Recording did not produce an output file at '{outputPath}'.");
        }
    }

    private void DeleteOutputFile()
    {
        try
        {
            File.Delete(outputPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal interface IRecordingFrame : IDisposable
{
    IRecordingSurface Surface { get; }

    TimeSpan Timestamp { get; }
}

internal interface IRecordingSurface : IDisposable
{
    int Width { get; }

    int Height { get; }
}

internal interface IRecordingCaption : IDisposable
{
    IRecordingSurface Render(IRecordingSurface surface, float opacity);
}

internal interface IRecordingSurfaceComposer
{
    IRecordingSurface Copy(IRecordingSurface surface);

    IRecordingSurface Crop(IRecordingSurface surface, CropRectangle crop);

    IRecordingCaption CreateCaption(int width, int height, string text);
}

internal interface IRecordingEncoder : IDisposable
{
    Task EncodeAsync(string outputPath, int width, int height, Func<RecordingSample?> getNextSample, CancellationToken cancellationToken);

    void Cancel();
}

internal sealed record RecordingSample(IRecordingSurface Surface, TimeSpan Timestamp, float CaptionOpacity);

internal sealed class Direct3DRecordingFrame(Direct3D11CaptureFrame frame) : IRecordingFrame
{
    public IRecordingSurface Surface { get; } = new Direct3DRecordingSurface(frame.Surface, frame.ContentSize.Width, frame.ContentSize.Height, ownsSurface: false);

    public TimeSpan Timestamp { get; } = frame.SystemRelativeTime;

    public void Dispose() => frame.Dispose();
}

internal sealed class Direct3DRecordingSurface(IDirect3DSurface surface, int width, int height, bool ownsSurface = true) : IRecordingSurface
{
    public IDirect3DSurface NativeSurface { get; } = surface;

    public int Width { get; } = width;

    public int Height { get; } = height;

    public void Dispose()
    {
        if (ownsSurface)
        {
            (NativeSurface as IDisposable)?.Dispose();
        }
    }
}

internal sealed class Direct3DRecordingSurfaceComposer : IRecordingSurfaceComposer
{
    public IRecordingSurface Copy(IRecordingSurface surface)
    {
        Direct3DRecordingSurface direct3D = GetDirect3D(surface);
        return new Direct3DRecordingSurface(CaptionRenderer.Copy(direct3D.NativeSurface), surface.Width, surface.Height);
    }

    public IRecordingSurface Crop(IRecordingSurface surface, CropRectangle crop)
    {
        Direct3DRecordingSurface direct3D = GetDirect3D(surface);
        return new Direct3DRecordingSurface(CaptionRenderer.Crop(direct3D.NativeSurface, crop), crop.Width, crop.Height);
    }

    public IRecordingCaption CreateCaption(int width, int height, string text) => new Direct3DRecordingCaption(width, height, text);

    private static Direct3DRecordingSurface GetDirect3D(IRecordingSurface surface) =>
        surface as Direct3DRecordingSurface ?? throw new ArgumentException("Expected a Direct3D recording surface.", nameof(surface));
}

internal sealed class Direct3DRecordingCaption(int width, int height, string text) : IRecordingCaption
{
    private readonly CaptionRenderer renderer = new((uint)width, (uint)height, text);

    public IRecordingSurface Render(IRecordingSurface surface, float opacity)
    {
        Direct3DRecordingSurface direct3D = surface as Direct3DRecordingSurface ?? throw new ArgumentException("Expected a Direct3D recording surface.", nameof(surface));
        return new Direct3DRecordingSurface(renderer.Render(direct3D.NativeSurface, opacity), surface.Width, surface.Height);
    }

    public void Dispose() => renderer.Dispose();
}

internal sealed class MediaRecordingEncoder : IRecordingEncoder
{
    private readonly CancellationTokenSource cancellation = new();

    public async Task EncodeAsync(string outputPath, int width, int height, Func<RecordingSample?> getNextSample, CancellationToken cancellationToken)
    {
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, cancellationToken);
        string directory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(
            outputPath,
            FileAccessMode.ReadWrite,
            StorageOpenOptions.None,
            FileOpenDisposition.CreateAlways).AsTask(linkedCancellation.Token).ConfigureAwait(false);

        VideoEncodingProperties videoProperties = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)width, (uint)height);
        VideoStreamDescriptor descriptor = new(videoProperties);
        MediaStreamSource source = new(descriptor) { BufferTime = TimeSpan.Zero };
        RecordingSample? firstSample = null;
        source.Starting += (_, args) =>
        {
            firstSample = getNextSample();
            args.Request.SetActualStartPosition(firstSample?.Timestamp ?? TimeSpan.Zero);
        };
        source.SampleRequested += (_, args) =>
        {
            RecordingSample? sample = firstSample ?? getNextSample();
            firstSample = null;
            if (sample is null)
            {
                args.Request.Sample = null;
                return;
            }

            Direct3DRecordingSurface surface = sample.Surface as Direct3DRecordingSurface ?? throw new InvalidOperationException("Expected a Direct3D recording surface.");
            MediaStreamSample mediaSample = MediaStreamSample.CreateFromDirect3D11Surface(surface.NativeSurface, sample.Timestamp);
            mediaSample.Processed += (_, _) => sample.Surface.Dispose();
            args.Request.Sample = mediaSample;
        };

        MediaEncodingProfile profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Video.Width = (uint)width;
        profile.Video.Height = (uint)height;
        profile.Video.FrameRate.Numerator = 30;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;
        MediaTranscoder transcoder = new() { HardwareAccelerationEnabled = true };
        PrepareTranscodeResult prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, profile).AsTask(linkedCancellation.Token).ConfigureAwait(false);
        if (!prepared.CanTranscode)
        {
            throw new AppCapException("Recording encoding failed.");
        }

        await prepared.TranscodeAsync().AsTask(linkedCancellation.Token).ConfigureAwait(false);
    }

    public void Cancel() => cancellation.Cancel();

    public void Dispose() => cancellation.Dispose();
}