# TODO

This is the developer backlog for `runmc`. Keep this file focused on implementation work, design decisions, and validation gaps.

## Target Configuration

- Make it possible to use a JSON config file to define targets. Add validation and clear errors for malformed target applications
- Include a notion of "device" in the target configuration, to support targeting applications on remote devices
- Add the ability to target unpackaged executables--match by executable path or PID
- Add graceful handling for game bootstrapper windows and child sign-in/pop-up windows. Capture any child windows separately and composite them into screenshots and recordings. Perhaps add a command to center/resize child windows as needed so they fully overlap the parent window.
- Add the ability to target a monitor or the entire desktop
- Add a "target list" command to make it easier to find an app's aumid or other targeting info

## Input

- Investigate whether we should change the "hover" command to use the same synthetic pointer device as "click"
- Consider renaming "click" to "tap" since it's using synthetic pointer input and not mouse
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
- Limit recordings to 30 minutes (and have the recording background process gracefully save and exit), but add a time-limit option to start which can be used if a longer recording is needed
- Add recording pause/resume commands--while paused, keep capturing but drop the frames
- Reuse screenshot caption rendering infrastructure for video captions.
- Add caption timing for recordings--fade out captions after 3 seconds.
- Add "captured from" metadata to mp4 files, similar to existing screenshot implementation
- Add an option to include/exclude cursor, just like screenshots

## Audio Capture
- Capture loopback audio from the targeted process and include it in the video

## Platform Support

- Generalize to cover other platforms, like MacOS

## Testing

- Expand the purpose-built E2E test app with multiple package identities, so targeting behavior can be tested across multiple installed apps
