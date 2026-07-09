using AppCap;
using AppCap.Protocol.Worker;
using System.Collections.Concurrent;

namespace AppCap.Tests;

// A controllable IWorkerHost used by protocol tests to drive the worker-protocol dispatch
// without any real capture or file I/O. It tracks which targets are "recording", records
// the last screenshot/start requests, and can be configured to fail or to block a stop, so
// tests can verify the acknowledgement, error, not-recording, and concurrency paths.
internal sealed class FakeWorkerHost : IWorkerHost
{
    private readonly ConcurrentDictionary<string, byte> recordings = new(StringComparer.Ordinal);

    public FakeWorkerHost(IEnumerable<string>? recording = null)
    {
        if (recording is not null)
        {
            foreach (string target in recording)
            {
                recordings[target] = 0;
            }
        }
    }

    public ScreenshotRequest? LastScreenshot { get; private set; }

    public RecordingStartRequest? LastStart { get; private set; }

    public bool? LastStopDiscard { get; private set; }

    public CaptionRequest? LastCaption { get; private set; }

    public string? StartFailWith { get; set; }

    public string? StopFailWith { get; set; }

    public string? BlockStopForTarget { get; set; }

    public TaskCompletionSource StopBlock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Ping() => true;

    public Task StartRecordingAsync(RecordingStartRequest request, CancellationToken cancellationToken)
    {
        LastStart = request;
        if (StartFailWith is not null)
        {
            throw new AppCapException(StartFailWith);
        }

        if (!recordings.TryAdd(request.TargetName, 0))
        {
            throw new AppCapException($"A recording is already running for target '{request.TargetName}'.");
        }

        return Task.CompletedTask;
    }

    public async Task<bool> StopRecordingAsync(string targetName, bool discard, CancellationToken cancellationToken)
    {
        if (!recordings.ContainsKey(targetName))
        {
            return false;
        }

        if (string.Equals(BlockStopForTarget, targetName, StringComparison.Ordinal))
        {
            await StopBlock.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        LastStopDiscard = discard;
        if (StopFailWith is not null)
        {
            throw new AppCapException(StopFailWith);
        }

        return recordings.TryRemove(targetName, out _);
    }

    public bool IsRecording(string targetName) => recordings.ContainsKey(targetName);

    public Task<bool> AddCaptionAsync(string targetName, string caption, CancellationToken cancellationToken)
    {
        LastCaption = new CaptionRequest { TargetName = targetName, Caption = caption };
        return Task.FromResult(recordings.ContainsKey(targetName));
    }

    public Task<bool> CaptureScreenshotAsync(ScreenshotRequest request, CancellationToken cancellationToken)
    {
        LastScreenshot = request;
        return Task.FromResult(recordings.ContainsKey(request.TargetName));
    }
}
