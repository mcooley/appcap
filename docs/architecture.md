# AppCap Architecture

`appcap` is a command-line tool for driving and capturing desktop applications. This
document describes how the system is structured internally so that captures can run
locally today and, in the future, against **remote targets** (for example, capturing
an Android phone from a Windows machine).

## Three components

The system is divided into three components. All three ship in the **same executable**.
The client and worker always run in separate processes; the worker and a local target can
share a process. Protocol boundaries allow a target to move to another process or machine
without changing the client.

```
  ┌────────┐   client<->worker    ┌────────┐   worker<->target   ┌────────┐
  │ Client │ ───────────────────▶ │ Worker │ ──────────────────▶ │ Target │
  │CLI/MCP │   named pipe         │        │   in-proc /         │ (OS    │
  └────────┘                      └────────┘   remote transport  │  capture)
                                                                  └────────┘
```

### Client frontends

The CLI is AppCap's canonical interface. Every AppCap feature must be accessible
from the CLI, and the CLI remains the primary focus of end-to-end test coverage. New
features are designed and tested through the CLI before other frontends expose them.

The stdio MCP server is a second, complete client frontend. Its tools use the CLI's
naming and terminology and delegate to the same command model and worker services. It
does not own capture, input, recording, or file-writing behavior. Like the CLI, it can
connect to the long-lived worker process shared by other client instances. The MCP
process itself lives only as long as its stdio client connection.

Both frontends are deliberately thin: they parse their respective interfaces, resolve an
attached target, send high-level requests to the worker, return the result, and exit. A
client does **not** perform media encoding, image rendering, or OS capture itself; that
belongs to the worker and target. The MCP screenshot tool may read the PNG produced by
the worker to return the protocol's image content result.

Many client instances can run at once (each CLI or MCP invocation is a client).

### Worker

The **worker** owns the shared application logic: file I/O, media encoding, caption and
cursor rendering, and saving screenshots and recordings. It coordinates one or more
targets to obtain frames and to inject input.

Lifecycle:

- The first `target attach` launches the worker. It is **not** a persistent daemon.
- There is **one worker per machine (per user)**, and that single worker **multiplexes
  multiple targets/recordings concurrently**. A client that needs a worker first pings
  the well-known per-user pipe; if no worker answers it takes a launch lock, starts one
  worker process, and waits for it to become reachable. Subsequent clients reuse the same
  worker. Each attached target owns a continuously running graphics-capture session,
  input-device state, active recording, and latest recording outcome. If an attached
  target is stopped, the worker starts capture when its application is launched later.
- Attachment owns worker lifetime. Recording and input commands never launch or keep the
  worker alive. Detaching a target cancels its recording and removes its devices; the
  worker exits immediately after acknowledging the last detach.
- The Windows target automatically attaches its touch, keyboard, and mouse devices when
  the target is attached.
- Attached and running are independent states. Closing an app ends an active recording
  but leaves its target attached. Operational commands do not relaunch it; a later
  explicit detach ends the session.

### Target

The **target** is the OS-integration layer: it captures frames and injects input for a
specific application/window. A target may be **in-proc** (the common local case, using
Windows Graphics Capture and synthetic input on the same machine) or, in the future,
**remote** and running a different OS with different characteristics.

Because a target can be remote and heterogeneous, the **worker↔target protocol is
documented and versioned** (see below).

## The two protocols

The two boundaries have very different requirements, so they use two different
protocols.

### Client ↔ Worker

- **Transport:** a Windows **named pipe** to the separate machine worker process.
- **Design philosophy:** **internal and optimized for development simplicity.** It is
  **not documented for third parties** and carries **no backwards- or forwards-
  compatibility guarantees** — the client and worker are always built and shipped
  together, so the protocol can change freely.
- **Payloads:** target attachment and high-level operations such as `recording.status`,
  `recording.stop`, `recording.cancel`, and `screenshot`. The worker does the work and
  returns status or a small acknowledgement.
- **Code:** `WorkerProtocol` / `WorkerMethods` under `src/AppCap/Protocol`, driven by the
  client through `RecordingIpc` and served by `WorkerHost` over its pipe.

### Worker ↔ Target

- **Transport:** an in-memory duplex stream for a local target; designed to also run over
  a **remote transport** (TCP/WebSocket/etc.) to reach a target on another machine.
- **Design philosophy:** **documented and versioned.** A target may be implemented by a
  different tool on a different OS, so this protocol has a stable, published contract
  (`TargetProtocol.Version`) and is described in
  [`docs/target-protocol.md`](target-protocol.md).
- **Payloads:** frame capture and input-device operations (`target.capture_frame`,
  `target.status`, `target.input_device.*`, and `target.input.*`). Targets own their
  supported device types and attachment state.
- **Code:** `TargetProtocol` / `TargetMethods` and the reference server `TargetServer`
  under `src/AppCap/Protocol`; `WindowsTargetHost` implements the local target.

## Worker-target in-proc reuse and frame handoff

A core goal is to minimize divergence between local and remote target paths. The
**worker↔target** boundary always uses the documented target protocol. In-proc it
  runs over an in-memory duplex stream; a remote target would replace that transport
  while preserving the same messages. The video capture path remains optimized by passing
  GPU surfaces in-process.

However, **performance-sensitive paths use an optimized transport rather than the
generic serialized protocol.** The prime example is **video recording**: the target
produces frames continuously and hands them to the worker for encoding.

- **In-proc target:** frames are handed over as **GPU surfaces**
  (`Direct3D11CaptureFrame` / `IDirect3DSurface`) with no copy or serialization. The
  worker feeds these surfaces straight into the media encoder.
- **Remote target:** frames must cross a process/machine boundary, so the target reads
  back **raw pixels** and serializes them; the worker reconstructs a surface on its side.

Screenshots use the same distinction at a smaller scale: a `CapturedFrame` carries raw
BGRA pixels for the serialized/remote form, while an in-proc target can hand over the
surface directly. Regardless of transport, the **worker** (not the client) renders any
caption and writes the output file.

## Putting it together: request flows

### `appcap screenshot`

1. **Client** selects an attached target and sends a `screenshot` request over the named
  pipe.
2. The **worker** serves the screenshot from the target's attachment-owned capture
  session, whether or not frames are currently being saved to a recording.
3. The worker renders the caption, writes the PNG, and acknowledges the request.

### `appcap record start` / `stop`

1. **Client** selects an attached target, sends `recording.start`, and exits once the
  already-running worker confirms the recording is live.
2. The **worker** creates a `RecordingSession` that subscribes a media writer to the
   target's already-running `AttachedCaptureSession`. The in-proc target hands GPU
  surfaces directly to the writer without a copy. For a local Windows target, the
  worker also starts WASAPI process-loopback capture for the target PID and its child
  processes unless the request disables audio. Audio initialization completes before
  the worker acknowledges that recording is live.
3. A later **client** can stop, cancel, or query status. Stop, cancel, and timeout detach
  and finalize only the writer; graphics capture keeps running until target detach or app
  closure. Graphics frame times and WASAPI packet positions share the system QPC clock.
  The writer uses the first video frame as time zero, trims or pads PCM at the recording
  boundaries, and feeds video plus optional audio through one `MediaStreamSource` into
  the MP4 transcoder. The latest recording outcome remains queryable until another
  recording starts or the target is detached.

## Not yet built (TODO)

These are intentionally deferred; the architecture above accommodates them without
further restructuring.

- **Remote targets end-to-end:** configuring a remote target in the config file,
  discovering/authenticating its endpoint, and a concrete remote transport binding plus a
  `RemoteTarget : ITarget` client. Only the in-proc target and the documented protocol
  (with a reference server and tests) exist today.

## Source map

| Area | Location |
| --- | --- |
| Client (CLI + orchestration) | `src/AppCap/Cli`, `src/AppCap/Core` |
| Client (MCP stdio frontend) | `src/AppCap/Mcp` |
| Shared protocol primitives | `src/AppCap/Protocol` (`JsonRpc`, `JsonRpcCodec`, `DuplexStream`, `InProcDuplexTransport`) |
| Client↔Worker protocol | `src/AppCap/Protocol` (`WorkerProtocol`), `RecordingIpc` |
| Worker↔Target protocol | `src/AppCap/Protocol` (`TargetProtocol`, `TargetServer`, `ITarget`), documented in `docs/target-protocol.md` |
| Worker (capture, encoding, rendering, file I/O) | `src/AppCap/Platform/Windows/Capture` (`WorkerHost`, `AttachedCaptureSession`, `RecordingSession`, `ScreenshotWriter`, `CaptionRenderer`) |
| Target (OS capture + input) | `src/AppCap/Platform/Windows/Capture` (`WindowCaptureTarget`, `RecordingCaptureTarget`, Graphics Capture / D3D helpers), `src/AppCap/Platform/Windows/Input` |
