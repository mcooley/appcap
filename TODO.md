# TODO

This is the developer backlog for `appcap`. Keep this file focused on implementation work, design decisions, and validation gaps.

## Target Configuration

- Include a notion of "device" in the target configuration, to support targeting applications on remote devices
- Add the ability to target unpackaged executables--match by executable path or PID
- Support AUMIDs for unpackaged applications
- Add graceful handling for game bootstrapper windows and child sign-in/pop-up windows. Capture any child windows separately and composite them into screenshots and recordings. Perhaps add a command to center/resize child windows as needed so they fully overlap the parent window.
- Add the ability to target a monitor or the entire desktop
- Include more robust support for capturing UWP app windows (like Calculator)

## Input

- Add mouse wheel or scroll support.
- Add drag support.
- Add gamepad input support.
- Add broader key-name coverage for `appcap type` if real workflows need it.
- Add optional timing controls for type and pointer input.
- Add a `mouse` input device before restoring pointer-hover support.

## Video Capture

- Gracefully handle resize while recording is running
- Add recording pause/resume commands--while paused, keep capturing but drop the frames
- Add recording speed commands--set speed to 0.25x or 4x, for example
- Double check that RedrawWindow is necessary and matches what reference screen capture tools do
- Add --crop desktop and --crop monitor which pad the recording size to the size of the desktop or monitor, as a way to be more forgiving for apps that have lots of window resizes/child windows/etc. while still avoiding capturing the whole desktop

## Audio Capture
- Capture loopback audio from the targeted process and include it in the video

## Architecture / Protocols

See [`docs/architecture.md`](docs/architecture.md) for the client ↔ worker ↔ target design.

- Build out **remote targets** end-to-end: configuring a remote target in the config file, discovering/authenticating its endpoint, a concrete remote transport binding (for example TCP or WebSocket), and a `RemoteTarget : ITarget` client.
- Define the optimized frame-streaming binding of the target protocol for remote **video** capture (in-proc uses direct GPU surface handoff today).
- Run [conformance test suite](https://github.com/modelcontextprotocol/conformance) against the MCP server
  - Blocked on [lack of stdio support](https://github.com/modelcontextprotocol/conformance/issues/258) 

## Platform Support

- Generalize to cover other platforms, like MacOS

## Testing

- Expand the purpose-built E2E test app with multiple package identities, so targeting behavior can be tested across multiple installed apps
