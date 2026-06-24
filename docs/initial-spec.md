# runmc

(This is the initial spec for the tool, written by Matt Cooley.)

runmc is a command-line tool to automate interactions with Minecraft for bug reproduction, test automation, and AI development workflows.

## Project setup
- .NET 10 command-line app
- NativeAOT single executable
- End-to-end tests for all major functionality (automated where possible, semi-automated may be OK), unit tests for code that doesn't have side-effects if it reduces the number of end-to-end tests that are needed 
- Prefer "modern" Windows APIs where possible, such as Windows.Graphics.Capture for screen capture
- Where win32 APIs are needed, try using cswinrt before writing raw p/invokes
- No ambiguities for coding style: enforce a style through analyzers
- Report terse, human-readable errors to stderr

## Global options
### --target
Selects which instance of Minecraft to target. If omitted, tries the options below _in order_. Options:
 - `runningbedrock` - attach to a currently-running instance of Bedrock on the local machine
 - `runningbedrockpreview` - attach to a currently-running instance of Bedrock Preview on the local machine
 - `runningeducation` - attach to a currently-running instance of Minecraft: Education Edition on the local machine
 - `runningjava`- attach to a currently-running instance of Java on the local machine
 - `installedbedrock` - launch an installed copy of Minecraft Bedrock on the local machine
 - `installedbedrockpreview` - launch an installed copy of Minecraft Bedrock (Preview) on the local machine
 - `installededucation` - launch an installed copy of Minecraft: Education Edition on the local machine
 - `installedjava` - launch an installed copy of Minecraft Java on the local machine
 - (to be expanded later: emulators, attached consoles, remote machines, etc.)

Implementation note: for Bedrock, always use the AUMID/package family name to identify the Minecraft process. Do not use other methods, like matching exe filenames.

Future implementation note: Bedrock is a GDK app and may launch a bootstrapper process/window before the main app window is ready. Target resolution should tolerate this by waiting for the real Minecraft window, ignoring transient bootstrapper windows, and reporting a clear timeout if the main app never appears.

### --help
Prints help for the current command.

## Commands

### runmc
Launches Minecraft (if target is not already running) and brings the Minecraft window to the foreground.

### runmc click -x 5 -y 5
Injects a mouse click into the Minecraft window at the provided coordinates, relative to the top-left corner of the window.

Implementation notes: should never inject input into other processes or windows, even if the Minecraft window is occluded. Do not exit until the click is complete.

### runmc hover -x 5 -y 5
Moves the cursor to the provided coordinates, relative to the top-left corner of the window.

### runmc type "the Creeper exploded[ESC][F2][Shift+F2]"
Injects keyboard input into the Minecraft window.

Implementation notes: supports both literal text and keyboard keys + modifiers in square brackets. Bracketed keys should use WebDriver/Playwright-style key names, such as `[Escape]`, `[Enter]`, `[Shift+F2]`, and `[Control+A]`. Write unit tests for the parsing.

### runmc resize --width 800 --height 600
Resizes the Minecraft window.

Implementation notes: prints an error if the desired window size is not possible. Should include visible non-client elements like the title bar. Restores (un-maximizes) the window if necessary.

### runmc screenshot --include-cursor --output path/to/foo.png
Takes a screenshot of the Minecraft window.

Implementation notes: always encode output as PNG. Include only the Minecraft window and no other windows, even if the window is occluded. Users will expect the image dimensions to match "resize", so design "resize" accordingly.

### runmc record --start --output path/to/foo.mp4, runmc record --stop
Starts recording the Minecraft window.

Implementaton notes: spawn a separate process for recording, so user's current terminal session can be used for running more runmc commands. (keep in mind constraint that we want this to be deployed a single executable--you decide command line switches to launch in stay-open-for-capture mode). Use built-in Windows media stack for encoding. Output format should always be mp4. Keep Windows.Media.Capture defaults which shows a yellow border around the window, which does not appear in the output. Like "screenshot", output size should be determined by window size; users will expect output dimensions to match "resize".

### runmc screenshot --caption "Test"
### runmc record --caption "Test"
Adds the text overlay to the current screenshot or video recording. Fades out after 3 seconds.

Implementation notes: text is centered at the bottom of the image or video. White with a drop shadow, Segoe UI font. If text is too long, wrap it so it fits on two lines. If it's too long for two lines, truncate it and use an ellipsis to indicate that it was truncated.

## Project sequencing
Phase 1: click, resize, screenshot for Minecraft Bedrock
Phase 1b: EDU support
Phase 2: recording start and stop
Phase 3: recording captions
Phase 4: other input injection scenarios like gamepad, mouse scroll, etc.
Phase 5: Java support
Phase 6: support child windows (like sign in pop ups), bootstrapper window, etc. gracefully