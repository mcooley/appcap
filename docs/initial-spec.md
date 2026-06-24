# runmc

(This is the initial spec for the tool, written by Matt Cooley.)

runmc is a command-line tool to automate interactions with configured target applications for bug reproduction, test automation, and AI development workflows.

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
Selects which configured target to use. If omitted, tries the options below _in order_. Options:
 - `bedrock` - attach to a currently-running target instance, or launch the installed app if needed
 - `bedrockpreview` - attach to a currently-running target instance, or launch the installed app if needed
 - `education` - attach to a currently-running target instance, or launch the installed app if needed
 - (to be expanded later: emulators, attached consoles, remote machines, etc.)

Implementation note: target definitions include package family names and AUMIDs. Use those values to identify and launch configured targets; do not use methods like matching exe filenames.

Future implementation note: some targets may launch a bootstrapper process/window before the main app window is ready. Target resolution should tolerate this by waiting for the real target window, ignoring transient bootstrapper windows, and reporting a clear timeout if the main app never appears.

### --help
Prints help for the current command.

## Commands

### runmc
Launches the configured target (if it is not already running) and brings the target window to the foreground.

### runmc click -x 5 -y 5
Injects a mouse click into the target window at the provided coordinates, relative to the top-left corner of the window.

Implementation notes: should never inject input into other processes or windows, even if the target window is occluded. Do not exit until the click is complete.

### runmc hover -x 5 -y 5
Moves the cursor to the provided coordinates, relative to the top-left corner of the window.

### runmc type "the Creeper exploded[ESC][F2][Shift+F2]"
Injects keyboard input into the target window.

Implementation notes: supports both literal text and keyboard keys + modifiers in square brackets. Bracketed keys should use WebDriver/Playwright-style key names, such as `[Escape]`, `[Enter]`, `[Shift+F2]`, and `[Control+A]`. Write unit tests for the parsing.

### runmc resize --width 800 --height 600
Resizes the target window.

Implementation notes: prints an error if the desired window size is not possible. Should include visible non-client elements like the title bar. Restores (un-maximizes) the window if necessary.

### runmc screenshot --include-cursor --caption "Test" --output path/to/foo.png
Takes a screenshot of the target window.

Implementation notes: always encode output as PNG. Include only the target window and no other windows, even if the window is occluded. Users will expect the image dimensions to match "resize", so design "resize" accordingly. `--caption` is optional and renders centered text at the bottom of the image.

### runmc record --start --output path/to/foo.mp4, runmc record --stop
Starts recording the target window.

Implementaton notes: spawn a separate process for recording, so user's current terminal session can be used for running more runmc commands. (keep in mind constraint that we want this to be deployed a single executable--you decide command line switches to launch in stay-open-for-capture mode). Use built-in Windows media stack for encoding. Output format should always be mp4. Keep Windows.Media.Capture defaults which shows a yellow border around the window, which does not appear in the output. Like "screenshot", output size should be determined by window size; users will expect output dimensions to match "resize".

### runmc screenshot --caption "Test"
### runmc record --caption "Test"
Adds a text overlay to the current screenshot or video recording. Screenshot captions are rendered into the output image; video captions fade out after 3 seconds.

Implementation notes: text is centered at the bottom of the image or video. White with a drop shadow, Trebuchet MS font. If text is too long, truncate it with an ellipsis.

## Project sequencing
Phase 1: click, resize, screenshot for configured local targets
Phase 1b: EDU support
Phase 2: recording start and stop
Phase 3: recording captions
Phase 4: other input injection scenarios like gamepad, mouse scroll, etc.
Phase 5: Java support
Phase 6: support child windows (like sign in pop ups), bootstrapper window, etc. gracefully