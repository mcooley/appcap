# TODO

This is the developer backlog for `runmc`. Keep this file focused on implementation work, design decisions, and validation gaps.

## Target Configuration

- Make it possible to use a JSON config file to define targets. Add validation and clear errors for malformed target applications
- Include a notion of "device" in the target configuration, to support targeting applications on remote devices
- Add the ability to target unpackaged executables--match by executable path or PID
- Add graceful handling for game bootstrapper windows and child sign-in/pop-up windows. Capture any child windows separately and composite them into screenshots and recordings. Perhaps add a command to center/resize child windows as needed so they fully overlap the parent window.

## Input

- Add mouse wheel or scroll support.
- Add drag support.
- Add gamepad input support.
- Add broader key-name coverage for `runmc type` if real workflows need it.
- Add optional timing controls for type and pointer input.

## Video Capture

- Ensure that only one recording is happening at a time
  - But it is acceptable to have multiple recordings targeting different applications
- Ensure that you can take a screenshot while a recording is running, and doing so does not initiate a new capture session (may require delegating screenshot to the same background process that is recording)
- Gracefully handle resize while recording is running
- Limit recordings to 30 minutes (and have the recording background process gracefully save and exit), but add an "extend" flag which can be used if a longer recording is needed
- Add recording pause/resume commands--while paused, keep capturing but drop the frames
- Encode recordings as MP4 using built-in Windows media APIs.
- Reuse screenshot caption rendering infrastructure for video captions.
- Add caption timing for recordings--fade out captions after 3 seconds.
- Add "captured from" metadata to mp4 files, similar to existing screenshot implementation

### Recording reliability (code review follow-ups)

High priority (correctness / data loss):

- Fix use-after-dispose of in-flight capture frames: `StoreLastFrame` disposes the previous frame while a `MediaStreamSample` built from its `Surface` may still be in flight in the transcoder. Gate disposal on the sample's `Processed` event instead of disposing on the next sample request. (`RecordingWorker.OnMediaStreamSourceSampleRequested`/`StoreLastFrame`)
- Don't swallow encode failures after `stop` is acknowledged: if `EncodeAsync` throws after `OK` was written to the stop client, the worker currently exits `Success` and skips `EnsureOutputFileExists`, so the user is told a recording succeeded that is missing/corrupt. Validate the output before acknowledging, or report the failure back. (`RecordingWorker.RunAsync`)
- Wire up the dead `WaitForFirstFrameAsync` guard (or remove it): the 2s "did not capture any frames" check is never called, so a target that never produces a frame hangs instead of failing fast. (`RecordingWorker`)
- Kill the worker process on startup failure: if `WaitForWorkerAsync` throws (or the user cancels), the spawned worker is orphaned because the `Process` handle is disposed immediately and no PID is retained. Track the process and terminate it on any start failure. (`RecordingController.StartAsync`)

Error reporting:

- Surface real worker errors to the caller: the controller redirects stdout/stderr but never reads them, and never checks the worker exit code, so genuine failures (e.g. capture unsupported) are reported only as the generic "Recording worker did not start" timeout. Read the worker's stderr / propagate a structured failure. (`RecordingController.WaitForWorkerAsync`, `RecordingWorker.RunAsync`)
- Use a meaningful exit code for runtime failures instead of `UsageError`. (`RecordingWorker.RunAsync`)

Cleanup / resource leaks:

- Cancel and dispose the abandoned `waitForStop` task and its `NamedPipeServerStream` when `EncodeAsync` completes first (e.g. window closed); today the task is left unobserved and the pipe is leaked. (`RecordingWorker.RunAsync`)
- Delete partial/zero-length output files on the error path; `CreateOutputStreamAsync` uses `ReplaceExisting` and leaves corrupt files behind on failure. (`RecordingWorker`)
- Add parent-process death detection (e.g. a job object) so the worker stops recording if the parent `runmc` process dies.

Concurrency:

- Close the TOCTOU race in `StartAsync`: the "already recording" check and worker launch are not atomic, so two concurrent starts for the same target can both pass and spawn competing workers on the same pipe.
- Synchronize `firstSampleTime`/`lastSampleTime`, which are written in `OnMediaStreamSourceStarting` and read/written in `OnMediaStreamSourceSampleRequested` on potentially different threads.
- Replace the blocking `Thread.Sleep(33)` in `OnMediaStreamSourceSampleRequested`, which stalls the MediaStreamSource encoder thread.

Security:

- Restrict the recording named pipe with an explicit `PipeSecurity` ACL (current user only). The pipe name is a non-secret hash of the target name and the server uses default security, so a local process could squat the name or spoof `status`/`stop` responses. (`RecordingIpc`, `RecordingWorker.WaitForStopAsync`)

## Platform Support

- Investigate whether CsWin32's "friendly overloads" can be used to simplify code while preserving NativeAOT compatibility
- Generalize to cover other platforms, like MacOS

## Testing

- Expand the purpose-built E2E test app with multiple package identities, so targeting behavior can be tested across multiple installed apps
