# AppCap Target Protocol

`appcap` is divided into three components — **client**, **worker**, and **target** (see
[`architecture.md`](architecture.md)). This document specifies the **worker ↔ target**
protocol: the contract a **target** (the OS-integration component that captures frames
and injects input for one application) exposes to a **worker**.

Unlike the internal client↔worker protocol, this protocol is **documented and
versioned**, because a target may be implemented by a **different tool on a different
machine or OS** — for example, a worker on Windows capturing from an Android device. A
worker drives a target unchanged whether it is in-proc or remote.

- Protocol version: **1.0** (`TargetProtocol.Version`)
- Message format: **JSON-RPC 2.0** (<https://www.jsonrpc.org/specification>)

> **In-proc note.** When the target runs in the same process as the worker (the common
> local case), the worker calls the target directly rather than serializing these
> messages — the optimized path described in [`architecture.md`](architecture.md). This
> protocol is the contract for the **remote** case; a reference server (`TargetServer`)
> and the in-repo tests exercise it over an in-proc duplex stream so the wire contract
> stays correct even before a real remote host exists.

## Roles

| Role | Who | Responsibility |
| --- | --- | --- |
| Worker | `appcap` worker | Requests frames from the target; owns encoding, rendering, and file I/O. |
| Target | OS-integration component | Captures frames (and, in future, injects input) for one application; answers requests. |

A target is **application-scoped**: it serves a single application/window. The worker
selects a target by connecting to its endpoint.

## Message framing

Each message is a single **compact UTF-8 JSON object** terminated by a newline (`\n`).
No BOM is written. This one-object-per-line framing keeps the protocol trivial to
implement over any bidirectional byte stream.

Every message includes `"jsonrpc": "2.0"`. Requests carry a `method`, an `id` (a JSON
number or string), and optional `params`. Responses echo the request `id` and carry
**either** a `result` **or** an `error`.

## Methods

All methods below are sent by the worker to the target.

### `target.status`

Reports the target's availability and the protocol version it speaks, so a worker can
detect version mismatches. This never fails while the target is reachable.

Request:

```json
{ "jsonrpc": "2.0", "id": 1, "method": "target.status" }
```

Response:

```json
{ "jsonrpc": "2.0", "id": 1, "result": { "protocolVersion": "1.0" } }
```

### `target.capture_frame`

Captures a single frame and returns it as **raw image data**. The target returns raw,
uncompressed **BGRA8 premultiplied** pixels (row-major, top-down) as a base64 string,
plus the frame dimensions and the metadata it derived from the captured window. The
**worker** owns any further processing — captioning, encoding, and saving the file; the
target never writes a file.

`params`:

| Field | Type | Meaning |
| --- | --- | --- |
| `includeCursor` | boolean | Whether to include the cursor. A target that serves frames from a live recording session may ignore this (the recording captures with the cursor disabled). |

Request:

```json
{ "jsonrpc": "2.0", "id": 2, "method": "target.capture_frame", "params": { "includeCursor": true } }
```

Response (`pixelsBase64` truncated for brevity):

```json
{ "jsonrpc": "2.0", "id": 2, "result": { "width": 640, "height": 480, "pixelsBase64": "AAAA…", "capturedFrom": "Captured from Example App 1.0.0.0" } }
```

`capturedFrom` may be `null` when the target could not derive window metadata. If the
capture fails, the target returns a `-32001` error with the reason in `message`.

> **Performance note.** `target.capture_frame` serializes pixels, which is appropriate
> for one-off screenshots and for remote targets. Continuous **video** capture is
> performance-sensitive: an in-proc target hands the worker GPU **surfaces** directly
> instead of serializing pixels through this method. A future revision of this protocol
> will define an optimized frame-streaming binding for remote video capture (see the
> TODOs in [`architecture.md`](architecture.md)).

## Errors

Errors use the standard JSON-RPC 2.0 `error` object. Codes in the range
`-32000..-32099` are implementation-defined server errors.

| Code | Name | Meaning |
| --- | --- | --- |
| `-32700` | Parse error | The target could not parse the request as JSON. |
| `-32600` | Invalid request | The payload is not a valid JSON-RPC request. |
| `-32601` | Method not found | The `method` is not supported by the target. |
| `-32602` | Invalid params | The params were malformed for the method. |
| `-32603` | Internal error | An unexpected target error. |
| `-32001` | Capture failed | A frame capture failed (for example, the window could not be captured). The reason is in `message`. |

Example capture failure:

```json
{ "jsonrpc": "2.0", "id": 2, "error": { "code": -32001, "message": "Target window could not be captured." } }
```

### "No target" vs. an error

A **transport-level** failure to reach a target (connection refused/timed out) is
**not** a JSON-RPC error — it means no target is available at that endpoint. It is up to
the worker how to surface this.

## Transport bindings

The protocol is transport agnostic. Two bindings are defined:

### In-proc (reference/local)

For a local target, the worker and target run in the same process and the worker calls
the target directly, bypassing serialization for performance. The reference server
(`TargetServer`) can also run over a pair of connected in-memory duplex streams that
stand in for a socket, using the **identical** message framing and codec; this is how
the in-repo tests validate the wire contract.

### Remote (future)

A remote target keeps the same messages and framing while replacing the in-proc call
with, for example, a TCP or WebSocket connection, plus an endpoint-discovery and
authentication scheme. This binding — along with a `RemoteTarget` client and the
optimized video frame-streaming path — is not yet implemented (see the TODOs in
[`architecture.md`](architecture.md)).

## Implementing the protocol in another tool

To act as a **target** that an `appcap` worker can drive:

1. Expose the application's endpoint (transport-specific).
2. Accept a JSON-RPC request per connection, framed as one UTF-8 JSON line.
3. Answer `target.status` with your protocol version and `target.capture_frame` with raw
   BGRA8 premultiplied pixels; never write a file target-side.
4. Report failures with the error codes above, echoing the request `id`.

The in-repo reference server (`TargetServer`) and the codec (`JsonRpcCodec`) under
`src/AppCap/Protocol` are a reference implementation, and the tests under
`tests/AppCap.Tests/Protocol` exercise the wire format directly.
