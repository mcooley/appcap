using AppCap.Windows;

namespace AppCap.Tests;

public sealed class RecordingTimelineTests
{
    private static readonly TimeSpan Origin = TimeSpan.FromSeconds(10);

    [Fact]
    public void TrimsAudioBeforeFirstVideoFrame()
    {
        RecordingTimeline timeline = CreateTimeline();
        timeline.SetOrigin(Origin);
        byte[] data = Enumerable.Range(0, 882 * ProcessLoopbackAudioCapture.BytesPerFrame).Select(value => (byte)(value % 251)).ToArray();

        IReadOnlyList<RecordingAudioSample> samples = timeline.AddAudioPacket(Packet(data, 882, Origin - TimeSpan.FromMilliseconds(10)));

        RecordingAudioSample sample = Assert.Single(samples);
        Assert.Equal(TimeSpan.Zero, sample.Timestamp);
        Assert.Equal(441u, sample.FrameCount);
        Assert.Equal(data.AsSpan(441 * ProcessLoopbackAudioCapture.BytesPerFrame).ToArray(), sample.Data);
    }

    [Fact]
    public void InsertsSilenceBeforeLatePacket()
    {
        RecordingTimeline timeline = CreateTimeline();
        timeline.SetOrigin(Origin);

        IReadOnlyList<RecordingAudioSample> samples = timeline.AddAudioPacket(Packet(new byte[441 * ProcessLoopbackAudioCapture.BytesPerFrame], 441, Origin + TimeSpan.FromMilliseconds(10)));

        Assert.Equal(2, samples.Count);
        Assert.Equal(TimeSpan.Zero, samples[0].Timestamp);
        Assert.Equal(441u, samples[0].FrameCount);
        Assert.All(samples[0].Data, value => Assert.Equal(0, value));
        Assert.Equal(TimeSpan.FromMilliseconds(10), samples[1].Timestamp);
    }

    [Fact]
    public void TrimsOverlappingPackets()
    {
        RecordingTimeline timeline = CreateTimeline();
        timeline.SetOrigin(Origin);
        _ = timeline.AddAudioPacket(Packet(new byte[441 * ProcessLoopbackAudioCapture.BytesPerFrame], 441, Origin));

        IReadOnlyList<RecordingAudioSample> samples = timeline.AddAudioPacket(Packet(new byte[441 * ProcessLoopbackAudioCapture.BytesPerFrame], 441, Origin + TimeSpan.FromMilliseconds(5)));

        RecordingAudioSample sample = Assert.Single(samples);
        Assert.Equal(TimeSpan.FromMilliseconds(10), sample.Timestamp);
        Assert.Equal(220u, sample.FrameCount);
    }

    [Fact]
    public void TimestampErrorContinuesFromPreviousPacket()
    {
        RecordingTimeline timeline = CreateTimeline();
        timeline.SetOrigin(Origin);
        _ = timeline.AddAudioPacket(Packet(new byte[441 * ProcessLoopbackAudioCapture.BytesPerFrame], 441, Origin));

        IReadOnlyList<RecordingAudioSample> samples = timeline.AddAudioPacket(
            Packet(new byte[441 * ProcessLoopbackAudioCapture.BytesPerFrame], 441, TimeSpan.Zero) with { TimestampError = true });

        RecordingAudioSample sample = Assert.Single(samples);
        Assert.Equal(TimeSpan.FromMilliseconds(10), sample.Timestamp);
        Assert.Equal(441u, sample.FrameCount);
    }

    [Fact]
    public void TrimsPacketAtVideoEndAndPadsMissingTail()
    {
        RecordingTimeline timeline = CreateTimeline();
        timeline.SetOrigin(Origin);
        TimeSpan end = Origin + TimeSpan.FromMilliseconds(15);

        IReadOnlyList<RecordingAudioSample> samples = timeline.AddAudioPacket(
            Packet(new byte[882 * ProcessLoopbackAudioCapture.BytesPerFrame], 882, Origin + TimeSpan.FromMilliseconds(10)),
            end);
        RecordingAudioSample? padding = timeline.Complete(end);

        Assert.Equal(2, samples.Count);
        Assert.Equal(441u, samples[0].FrameCount);
        Assert.Equal(220u, samples[1].FrameCount);
        Assert.Null(padding);
    }

    [Fact]
    public void CompletePadsAudioToVideoEnd()
    {
        RecordingTimeline timeline = CreateTimeline();
        timeline.SetOrigin(Origin);
        _ = timeline.AddAudioPacket(Packet(new byte[441 * ProcessLoopbackAudioCapture.BytesPerFrame], 441, Origin));

        RecordingAudioSample? padding = timeline.Complete(Origin + TimeSpan.FromMilliseconds(25));

        Assert.NotNull(padding);
        Assert.Equal(TimeSpan.FromMilliseconds(10), padding.Timestamp);
        Assert.Equal(661u, padding.FrameCount);
        Assert.All(padding.Data, value => Assert.Equal(0, value));
    }

    private static RecordingTimeline CreateTimeline() =>
        new(ProcessLoopbackAudioCapture.SamplesPerSecond, ProcessLoopbackAudioCapture.BytesPerFrame);

    private static RecordingAudioPacket Packet(byte[] data, uint frames, TimeSpan timestamp) =>
        new(data, frames, timestamp, Discontinuous: false, TimestampError: false);
}