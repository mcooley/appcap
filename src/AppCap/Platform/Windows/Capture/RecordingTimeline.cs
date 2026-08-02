namespace AppCap.Windows;

internal sealed record RecordingAudioSample(byte[] Data, TimeSpan Timestamp, TimeSpan Duration, uint FrameCount);

internal sealed class RecordingTimeline(uint samplesPerSecond, ushort bytesPerFrame)
{
    private TimeSpan? origin;
    private long nextAudioFrame;

    public void SetOrigin(TimeSpan timestamp)
    {
        if (origin is not null)
        {
            return;
        }

        origin = timestamp;
    }

    public TimeSpan NormalizeVideoTimestamp(TimeSpan timestamp) => timestamp - GetOrigin();

    public IReadOnlyList<RecordingAudioSample> AddAudioPacket(RecordingAudioPacket packet, TimeSpan? absoluteEndTime = null)
    {
        ArgumentNullException.ThrowIfNull(packet);
        TimeSpan timelineOrigin = GetOrigin();
        long packetStartFrame = packet.TimestampError
            ? nextAudioFrame
            : TicksToFrames((packet.Timestamp - timelineOrigin).Ticks);
        long packetFrameCount = packet.FrameCount;
        int byteOffset = 0;

        if (packetStartFrame < 0)
        {
            long trimmedFrames = Math.Min(-packetStartFrame, packetFrameCount);
            packetStartFrame += trimmedFrames;
            packetFrameCount -= trimmedFrames;
            byteOffset = checked((int)(trimmedFrames * bytesPerFrame));
        }

        long endFrame = absoluteEndTime is { } endTime
            ? Math.Max(0, TicksToFrames((endTime - timelineOrigin).Ticks))
            : long.MaxValue;
        if (packetStartFrame >= endFrame || packetFrameCount == 0)
        {
            return [];
        }

        List<RecordingAudioSample> samples = [];
        if (packetStartFrame > nextAudioFrame)
        {
            long silenceFrames = Math.Min(packetStartFrame, endFrame) - nextAudioFrame;
            if (silenceFrames > 0)
            {
                samples.Add(CreateSilence(nextAudioFrame, silenceFrames));
                nextAudioFrame += silenceFrames;
            }
        }
        else if (packetStartFrame < nextAudioFrame)
        {
            long overlapFrames = Math.Min(nextAudioFrame - packetStartFrame, packetFrameCount);
            packetStartFrame += overlapFrames;
            packetFrameCount -= overlapFrames;
            byteOffset = checked(byteOffset + (int)(overlapFrames * bytesPerFrame));
        }

        packetFrameCount = Math.Min(packetFrameCount, endFrame - packetStartFrame);
        if (packetFrameCount <= 0)
        {
            return samples;
        }

        int byteCount = checked((int)(packetFrameCount * bytesPerFrame));
        byte[] data = packet.Data.AsSpan(byteOffset, byteCount).ToArray();
        samples.Add(CreateSample(data, packetStartFrame, packetFrameCount));
        nextAudioFrame = packetStartFrame + packetFrameCount;
        return samples;
    }

    public RecordingAudioSample? Complete(TimeSpan absoluteEndTime)
    {
        long endFrame = Math.Max(0, TicksToFrames((absoluteEndTime - GetOrigin()).Ticks));
        if (endFrame <= nextAudioFrame)
        {
            return null;
        }

        RecordingAudioSample padding = CreateSilence(nextAudioFrame, endFrame - nextAudioFrame);
        nextAudioFrame = endFrame;
        return padding;
    }

    private RecordingAudioSample CreateSilence(long startFrame, long frameCount) =>
        CreateSample(new byte[checked((int)(frameCount * bytesPerFrame))], startFrame, frameCount);

    private RecordingAudioSample CreateSample(byte[] data, long startFrame, long frameCount) =>
        new(data, FramesToTime(startFrame), FramesToTime(frameCount), checked((uint)frameCount));

    private long TicksToFrames(long ticks) => checked(ticks * samplesPerSecond / TimeSpan.TicksPerSecond);

    private TimeSpan FramesToTime(long frames) => TimeSpan.FromTicks(checked(frames * TimeSpan.TicksPerSecond / samplesPerSecond));

    private TimeSpan GetOrigin() => origin ?? throw new InvalidOperationException("The recording timeline has no video origin.");
}