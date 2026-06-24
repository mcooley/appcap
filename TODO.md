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

- Add recording start/stop commands
- Ensure that only one recording is happening at a time
  - But it is acceptable to have multiple recordings targeting different applications
- Ensure that you can take a screenshot while a recording is running, and doing so does not initiate a new capture session (may require delegating screenshot to the same background process that is recording)
- Limit recordings to 30 minutes (and have the recording background process gracefully save and exit), but add an "extend" flag which can be used if a longer recording is needed
- Add recording pause/resume commands--while paused, keep capturing but drop the frames
- Encode recordings as MP4 using built-in Windows media APIs.
- Reuse screenshot caption rendering infrastructure for video captions.
- Add caption timing for recordings--fade out captions after 3 seconds.
- Add "captured from" metadata to mp4 files, similar to existing screenshot implementation

## Platform Support

- Investigate whether CsWin32's "friendly overloads" can be used to simplify code while preserving NativeAOT compatibility
- Generalize to cover other platforms, like MacOS

## Testing

- Write a test app to support end-to-end tests
  - Use CsWin32+NativeAOT
  - Package it into an MSIX file (potentially multiple packages with different identities, so we have multiple apps to test targeting behavior)
  - Have it show a window with a known background color that we can use to visually detect overlays
  - Have it use GameInput from NuGet to get pointer input, have some simple UI controls that react to pointer input
  - Have an area to test text input
  - Add target configuration for it
- Write end-to-end test suite:
  - Targeting behavior: prefer running app, fall back to installed app
  - Test pointer input
  - Test keyboard input
  - Verify that pointer input coordinates match screenshot coordinates
  - Screenshots: includes/excludes cursor as requested
  - Screenshots: writes metadata to the PNG file with the window name and app version
  - Screenshots: adds caption as requested
