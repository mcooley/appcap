using System.Collections.Concurrent;
using AppCap.Diagnostics;
using global::Windows.Graphics.Capture;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Media.Core;
using global::Windows.Media.MediaProperties;
using global::Windows.Media.Transcoding;
using global::Windows.Storage;
using global::Windows.Storage.Streams;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger? logger;
    private readonly bool includeAudio;
    private readonly BlockingCollection<IRecordingFrame> frames = new(new ConcurrentQueue<IRecordingFrame>());
    private readonly BlockingCollection<RecordingAudioPacket>? audioPackets;
    private readonly Queue<RecordingAudioSample> readyAudioSamples = new();
    private readonly RecordingTimeline timeline = new(ProcessLoopbackAudioCapture.SamplesPerSecond, ProcessLoopbackAudioCapture.BytesPerFrame);
    private readonly TaskCompletionSource firstFrameArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource timelineStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource videoSamplesCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object captionGate = new();
    private Task encodeTask = Task.CompletedTask;
    private CropRectangle? effectiveCrop;
    private int outputWidth;
    private int outputHeight;
    private IRecordingSurface? latestSurface;
    private IRecordingCaption? caption;
    private TimeSpan captionStartTime;
    private TimeSpan lastSampleTime;
    private string? pendingCaption;
    private int captionVersion;
    private int renderedCaptionVersion = -1;
    private int captionTailSamples;
    private TimeSpan videoOrigin;
    private TimeSpan videoEndTime;
    private long audioEndTimeTicks;
    private bool timelineHasOrigin;
    private bool finalVideoSampleWritten;
    private bool audioPaddingWritten;
    private bool started;
    private bool disposed;

    public RecordingWriter(string outputPath, CropRectangle? crop, bool includeAudio = false, ILogger? logger = null)
        : this(outputPath, crop, new MediaRecordingEncoder(), new Direct3DRecordingSurfaceComposer(), includeAudio, logger)
    {
    }

    internal RecordingWriter(string outputPath, CropRectangle? crop, IRecordingEncoder encoder, IRecordingSurfaceComposer composer, bool includeAudio = false, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        this.outputPath = Path.GetFullPath(outputPath);
        this.crop = crop;
        this.encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        this.composer = composer ?? throw new ArgumentNullException(nameof(composer));
        this.logger = logger;
        this.includeAudio = includeAudio;
        if (includeAudio)
        {
            audioPackets = new BlockingCollection<RecordingAudioPacket>(new ConcurrentQueue<RecordingAudioPacket>());
        }
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

    public void AddAudioPacket(RecordingAudioPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (audioPackets is null)
        {
            throw new InvalidOperationException("Audio is not enabled for this recording.");
        }

        try
        {
            if (!packet.TimestampError)
            {
                long packetDurationTicks = checked((long)packet.FrameCount * TimeSpan.TicksPerSecond / ProcessLoopbackAudioCapture.SamplesPerSecond);
                UpdateAudioEndTime(checked(packet.Timestamp.Ticks + packetDurationTicks));
            }

            audioPackets.Add(packet);
        }
        catch (InvalidOperationException)
        {
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
        (int width, int height) = ConfigureOutputSize(sourceWidth, sourceHeight);
        outputWidth = width;
        outputHeight = height;
        if (logger is not null)
        {
            RecordingLog.EncoderStarted(logger, outputPath, sourceWidth, sourceHeight, outputWidth, outputHeight, this.includeAudio);
        }
        encodeTask = encoder.EncodeAsync(outputPath, width, height, GetNextSample, audioPackets is null ? null : GetNextAudioSample, cancellationToken);

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
        CompleteAudio();
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
            if (logger?.IsEnabled(LogLevel.Information) == true)
            {
                long outputLength = new FileInfo(outputPath).Length;
                RecordingLog.OutputFinalized(logger, outputPath, outputLength);
            }
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

    public void CompleteAudio()
    {
        if (audioPackets is null)
        {
            return;
        }

        try
        {
            audioPackets.CompleteAdding();
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
        CompleteAudio();
        caption?.Dispose();
        latestSurface?.Dispose();
        while (frames.TryTake(out IRecordingFrame? frame))
        {
            frame.Dispose();
        }

        frames.Dispose();
        audioPackets?.Dispose();
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
                    if (!timelineHasOrigin)
                    {
                        videoOrigin = frame.Timestamp;
                        timeline.SetOrigin(videoOrigin);
                        timelineHasOrigin = true;
                        timelineStarted.TrySetResult();
                    }

                    TimeSpan timestamp = timeline.NormalizeVideoTimestamp(frame.Timestamp);
                    latestSurface?.Dispose();
                    latestSurface = ComposeFrame(frame.Surface);
                    lastSampleTime = timestamp;
                    ApplyPendingCaption(timestamp, latestSurface.Width, latestSurface.Height);
                    float opacity = GetCaptionOpacity(timestamp);
                    if (caption is not null && opacity == 0)
                    {
                        caption.Dispose();
                        caption = null;
                        captionTailSamples = CaptionTailSampleCount;
                    }

                    return CreateSample(timestamp, opacity);
                }
            }

            if (latestSurface is null)
            {
                timelineStarted.TrySetResult();
                videoSamplesCompleted.TrySetResult();
                return null;
            }

            TimeSpan generatedTimestamp = lastSampleTime + CaptionSampleInterval;
            ApplyPendingCaption(generatedTimestamp, latestSurface.Width, latestSurface.Height);
            if (caption is not null)
            {
                float opacity = GetCaptionOpacity(generatedTimestamp);
                if (opacity == 0)
                {
                    caption.Dispose();
                    caption = null;
                    captionTailSamples = CaptionTailSampleCount;
                }

                lastSampleTime = generatedTimestamp;
                return CreateSample(generatedTimestamp, opacity);
            }

            if (captionTailSamples > 0)
            {
                captionTailSamples--;
                lastSampleTime = generatedTimestamp;
                return CreateSample(generatedTimestamp, 0);
            }

            if (frames.IsCompleted)
            {
                TimeSpan audioEndTime = TimeSpan.FromTicks(Volatile.Read(ref audioEndTimeTicks));
                if (!finalVideoSampleWritten && audioEndTime > videoOrigin + lastSampleTime)
                {
                    finalVideoSampleWritten = true;
                    lastSampleTime = timeline.NormalizeVideoTimestamp(audioEndTime);
                    return CreateSample(lastSampleTime, 0);
                }

                videoEndTime = videoOrigin + lastSampleTime;
                videoSamplesCompleted.TrySetResult();
                return null;
            }
        }
    }

    private RecordingAudioSample? GetNextAudioSample()
    {
        timelineStarted.Task.GetAwaiter().GetResult();
        if (!timelineHasOrigin || audioPackets is null)
        {
            return null;
        }

        while (true)
        {
            if (readyAudioSamples.TryDequeue(out RecordingAudioSample? ready))
            {
                return ready;
            }

            RecordingAudioPacket? packet;
            try
            {
                packet = audioPackets.Take();
            }
            catch (InvalidOperationException)
            {
                videoSamplesCompleted.Task.GetAwaiter().GetResult();
                if (audioPaddingWritten)
                {
                    return null;
                }

                audioPaddingWritten = true;
                return timeline.Complete(videoEndTime);
            }

            foreach (RecordingAudioSample sample in timeline.AddAudioPacket(packet))
            {
                readyAudioSamples.Enqueue(sample);
            }
        }
    }

    private void UpdateAudioEndTime(long endTimeTicks)
    {
        long current = Volatile.Read(ref audioEndTimeTicks);
        while (endTimeTicks > current)
        {
            long observed = Interlocked.CompareExchange(ref audioEndTimeTicks, endTimeTicks, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
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

    private IRecordingSurface ComposeFrame(IRecordingSurface surface)
    {
        IRecordingSurface? cropped = null;
        try
        {
            IRecordingSurface source = surface;
            if (effectiveCrop is { } requestedCrop)
            {
                CropRectangle? availableCrop = IntersectCrop(requestedCrop, surface.Width, surface.Height);
                if (availableCrop is not null)
                {
                    cropped = composer.Crop(surface, availableCrop.Value);
                    source = cropped;
                }
            }

            return source.Width == outputWidth && source.Height == outputHeight
                ? composer.Copy(source)
                : composer.Fit(source, outputWidth, outputHeight);
        }
        finally
        {
            cropped?.Dispose();
        }
    }

    private static CropRectangle? IntersectCrop(CropRectangle crop, int width, int height)
    {
        int right = Math.Min(crop.X + crop.Width, width);
        int bottom = Math.Min(crop.Y + crop.Height, height);
        int croppedWidth = right - crop.X;
        int croppedHeight = bottom - crop.Y;
        return croppedWidth > 0 && croppedHeight > 0
            ? new CropRectangle(crop.X, crop.Y, croppedWidth, croppedHeight)
            : null;
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

    private (int Width, int Height) ConfigureOutputSize(int sourceWidth, int sourceHeight)
    {
        CropRectangle output = crop ?? new CropRectangle(0, 0, sourceWidth, sourceHeight);
        output.ValidateWithin(sourceWidth, sourceHeight);
        int width = output.Width & ~1;
        int height = output.Height & ~1;
        if (width == 0 || height == 0)
        {
            throw new AppCapException("Recording dimensions must be at least 2x2 pixels.");
        }

        effectiveCrop = crop is null && width == sourceWidth && height == sourceHeight
            ? null
            : new CropRectangle(output.X, output.Y, width, height);
        return (width, height);
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

    IRecordingSurface Fit(IRecordingSurface surface, int width, int height);

    IRecordingCaption CreateCaption(int width, int height, string text);
}

internal interface IRecordingEncoder : IDisposable
{
    Task EncodeAsync(
        string outputPath,
        int width,
        int height,
        Func<RecordingSample?> getNextVideoSample,
        Func<RecordingAudioSample?>? getNextAudioSample,
        CancellationToken cancellationToken);

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

    public IRecordingSurface Fit(IRecordingSurface surface, int width, int height)
    {
        Direct3DRecordingSurface direct3D = GetDirect3D(surface);
        return new Direct3DRecordingSurface(CaptionRenderer.Fit(direct3D.NativeSurface, width, height), width, height);
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
    private static readonly TimeSpan VideoSampleDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 30);
    private readonly CancellationTokenSource cancellation = new();

    public async Task EncodeAsync(
        string outputPath,
        int width,
        int height,
        Func<RecordingSample?> getNextVideoSample,
        Func<RecordingAudioSample?>? getNextAudioSample,
        CancellationToken cancellationToken)
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
        VideoStreamDescriptor videoDescriptor = new(videoProperties);
        AudioStreamDescriptor? audioDescriptor = getNextAudioSample is null
            ? null
            : new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(
                ProcessLoopbackAudioCapture.SamplesPerSecond,
                ProcessLoopbackAudioCapture.ChannelCount,
                ProcessLoopbackAudioCapture.BitsPerSample));
        MediaStreamSource source = audioDescriptor is null
            ? new MediaStreamSource(videoDescriptor)
            : new MediaStreamSource(videoDescriptor, audioDescriptor);
        source.BufferTime = TimeSpan.Zero;
        Exception? sampleFailure = null;
        object audioRequestGate = new();
        Task audioRequests = Task.CompletedTask;

        Task ProcessSampleRequestAsync(
            Task previousRequest,
            MediaStreamSourceSampleRequest request,
            MediaStreamSourceSampleRequestDeferral deferral,
            Func<MediaStreamSample?> getSample) => Task.Run(async () =>
            {
                await previousRequest.ConfigureAwait(false);
                try
                {
                    request.Sample = getSample();
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref sampleFailure, exception, null);
                    source.NotifyError(MediaStreamSourceErrorStatus.Other);
                }
                finally
                {
                    deferral.Complete();
                }
            }, CancellationToken.None);

        source.Starting += (_, args) => args.Request.SetActualStartPosition(TimeSpan.Zero);
        source.SampleRequested += (_, args) =>
        {
            MediaStreamSourceSampleRequest request = args.Request;
            if (ReferenceEquals(request.StreamDescriptor, videoDescriptor))
            {
                try
                {
                    request.Sample = CreateVideoSample(getNextVideoSample());
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref sampleFailure, exception, null);
                    source.NotifyError(MediaStreamSourceErrorStatus.Other);
                }

                return;
            }

            MediaStreamSourceSampleRequestDeferral deferral = request.GetDeferral();
            lock (audioRequestGate)
            {
                audioRequests = ProcessSampleRequestAsync(
                    audioRequests,
                    request,
                    deferral,
                    () => CreateAudioSample(getNextAudioSample!));
            }
        };

        MediaEncodingProfile profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Video.Width = (uint)width;
        profile.Video.Height = (uint)height;
        profile.Video.FrameRate.Numerator = 30;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;
        profile.Audio = audioDescriptor is null
            ? null
            : AudioEncodingProperties.CreateAac(
                ProcessLoopbackAudioCapture.SamplesPerSecond,
                ProcessLoopbackAudioCapture.ChannelCount,
                128_000);
        MediaTranscoder transcoder = new() { HardwareAccelerationEnabled = true };
        PrepareTranscodeResult prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, profile).AsTask(linkedCancellation.Token).ConfigureAwait(false);
        if (!prepared.CanTranscode)
        {
            throw new AppCapException("Recording encoding failed.");
        }

        await prepared.TranscodeAsync().AsTask(linkedCancellation.Token).ConfigureAwait(false);
        if (sampleFailure is not null)
        {
            throw sampleFailure;
        }
    }

    private static MediaStreamSample? CreateVideoSample(RecordingSample? sample)
    {
        if (sample is null)
        {
            return null;
        }

        Direct3DRecordingSurface surface = sample.Surface as Direct3DRecordingSurface ?? throw new InvalidOperationException("Expected a Direct3D recording surface.");
        MediaStreamSample mediaSample = MediaStreamSample.CreateFromDirect3D11Surface(surface.NativeSurface, sample.Timestamp);
        mediaSample.Duration = VideoSampleDuration;
        mediaSample.Processed += (_, _) => sample.Surface.Dispose();
        return mediaSample;
    }

    private static MediaStreamSample? CreateAudioSample(Func<RecordingAudioSample?> getNextSample)
    {
        RecordingAudioSample? sample = getNextSample();
        if (sample is null)
        {
            return null;
        }

        using DataWriter writer = new();
        writer.WriteBytes(sample.Data);
        MediaStreamSample mediaSample = MediaStreamSample.CreateFromBuffer(writer.DetachBuffer(), sample.Timestamp);
        mediaSample.Duration = sample.Duration;
        return mediaSample;
    }

    public void Cancel() => cancellation.Cancel();

    public void Dispose() => cancellation.Dispose();
}