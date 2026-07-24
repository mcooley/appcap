# AppCap Architecture

`appcap` is a command-line tool for driving and capturing desktop applications. This
document describes how the system is structured internally so that captures can run
locally today and, in the future, against **remote targets** (for example, capturing
an Android phone from a Windows machine).

## Three components

The system is divided into three components. All three ship in the **same executable**
and can run in the **same process**, but they communicate over protocols that can be
**remoted**, so any component can be moved to another process or another machine without
changing the others.

```
  ┌────────┐   client<->worker    ┌────────┐   worker<->target   ┌────────┐
  │ Client │ ───────────────────▶ │ Worker │ ──────────────────▶ │ Target │
  │  (CLI) │   named pipe /       │        │   in-proc /         │ (OS    │
  └────────┘   in-proc duplex     └────────┘   remote transport  │  capture)
                                                                  └────────┘
```

### Client

The **client** is the CLI you invoke. It is deliberately **thin and short-lived**: it
parses arguments, resolves the target, sends one or more high-level requests to a
worker, prints the result, and exits. The client does **not** perform file I/O, media
encoding, image rendering, or OS capture itself — that all belongs to the worker and
the target.

Many client instances can run at once (each `appcap` invocation is a client).

### Worker

The **worker** owns the shared application logic: file I/O, media encoding, caption and
cursor rendering, and saving screenshots and recordings. It coordinates one or more
targets to obtain frames and to inject input.

Lifecycle:

- The worker is **launched just-in-time** by a client when one is needed. It is **not**
  a persistent daemon.
- There is **one worker per machine (per user)**, and that single worker **multiplexes
  multiple targets/recordings concurrently**. A client that needs a worker first pings
  the well-known per-user pipe; if no worker answers it takes a launch lock, starts one
  worker process, and waits for it to become reachable. Subsequent clients reuse the same
  worker. Each recording runs as an independent `RecordingSession` keyed by target name.
- The worker **self-terminates when idle**. When it has no active recordings or attached
  target input devices for an idle interval it exits, so a worker never runs indefinitely
  even if clients go away without stopping their recordings.

When no long-running background work is required (for example, taking a single
screenshot while nothing is recording), the client **hosts the worker in its own
process** and talks to it over an in-proc transport instead of launching a separate
process. The code path is otherwise identical.

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

- **Transport:** a Windows **named pipe** when the worker is a separate process, or an
  **in-proc duplex stream** when the client hosts the worker in-process. The same
  request/response code runs over either transport.
- **Design philosophy:** **internal and optimized for development simplicity.** It is
  **not documented for third parties** and carries **no backwards- or forwards-
  compatibility guarantees** — the client and worker are always built and shipped
  together, so the protocol can change freely.
- **Payloads:** high-level operations — `recording.status`, `recording.stop`,
  `recording.cancel`, and `screenshot` (capture a frame, render an optional caption, and
  save the file). The worker does the work and returns a small acknowledgement.
- **Code:** `WorkerProtocol` / `WorkerMethods` under `src/AppCap/Protocol`, driven by the
  client through `RecordingIpc` and served by the worker (`WorkerHost` over its pipe, or
  the in-proc `InProcScreenshotHost`).

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

## In-proc reuse and the frame-handoff optimization

A core goal is to **minimize divergence between the in-proc and remote code paths** by
reusing the remoting code in-proc:

- The **client↔worker** boundary always uses the protocol and a transport. In-proc it
  runs over an in-memory duplex stream that reuses the exact JSON-RPC codec and framing
  used over the named pipe (`InProcDuplexTransport`). The client is equally thin whether
  the worker is local-in-proc or a separate process.
- The **worker↔target** boundary always uses the documented target protocol. In-proc it
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

### `appcap screenshot` with no recording running

1. **Client** resolves the target and hosts a **worker in-proc** (over an in-proc duplex
   stream), backed by an in-proc **`WindowCaptureTarget`**.
2. Client sends a `screenshot` request (output path, cursor, caption) over the
   **client↔worker** protocol.
3. **Worker** asks its **target** for a frame, renders the caption, and writes the PNG.
4. Worker returns an acknowledgement; the client exits.

### `appcap screenshot` while a recording is running

1. **Client** detects a recording worker for the target and connects to it over the
   named pipe.
2. Client sends the same `screenshot` request over the **client↔worker** protocol.
3. The recording **worker** serves the screenshot from its **existing capture session**
   (its live in-proc target) — no second capture session is started — then renders and
   saves the file.

### `appcap record start` / `stop`

1. **Client** ensures the machine-wide **worker** is running (ping the per-user pipe;
   launch one under a lock if none answers), then sends `recording.start` for its target
   and exits once the worker confirms the recording is live.
2. The **worker** creates a `RecordingSession` for that target, running an in-proc
   **`RecordingCaptureTarget`** whose surfaces feed directly into the media encoder (the
   optimized in-proc frame handoff). One worker can host many such sessions at once; it
   serves `recording.status` / `recording.stop` / `recording.cancel` / `screenshot`
   (each keyed by target name) over its pipe.
3. A later **client** (`record stop`) connects over the pipe and asks the worker to
   finalize (or discard) that target's recording. When the worker has no active recordings
   or attached input devices for its idle interval it self-terminates.

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
| Shared protocol primitives | `src/AppCap/Protocol` (`JsonRpc`, `JsonRpcCodec`, `DuplexStream`, `InProcDuplexTransport`) |
| Client↔Worker protocol | `src/AppCap/Protocol` (`WorkerProtocol`), `RecordingIpc` |
| Worker↔Target protocol | `src/AppCap/Protocol` (`TargetProtocol`, `TargetServer`, `ITarget`), documented in `docs/target-protocol.md` |
| Worker (encoding, rendering, file I/O) | `src/AppCap/Platform/Windows/Capture` (`WorkerHost`, `RecordingSession`, `ScreenshotWriter`, `CaptionRenderer`) |
| Target (OS capture + input) | `src/AppCap/Platform/Windows/Capture` (`WindowCaptureTarget`, `RecordingCaptureTarget`, Graphics Capture / D3D helpers), `src/AppCap/Platform/Windows/Input` |
