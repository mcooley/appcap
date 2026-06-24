# runmc

`runmc` is a CLI for recording and interacting with your app. It's useful for bug reproduction, test automation, and AI development workflows.

The current built-in targets are Minecraft for Windows, Minecraft Preview for Windows, and Minecraft Education.

## Installing
`runmc` is a single-file executable with no dependencies. Copy the exe to your target machine and run it from the command line.

## Targets

Use `--target` to choose a configured target. If the target is already running, `runmc` attaches to the running window. If not, it launches the installed app and waits for the window.

Available targets:

- `bedrock`
- `bedrockpreview`
- `education`
- `testapp` - developer E2E test app, when registered locally

If `--target` is omitted, `runmc` tries the built-in targets in this order:

1. `bedrock`
2. `bedrockpreview`
3. `education`

## Commands

### Focus

Launches or finds the target and brings its window to the foreground.

```powershell
runmc --target bedrock
```

### Click

Taps the screen.

```powershell
runmc --target bedrock click -x 151 -y 684
```

Coordinates are relative to the top-left corner of the target window.

### Hover

Moves the cursor.

```powershell
runmc --target education hover -x 151 -y 684
```

Coordinates are relative to the top-left corner of the target window.

### Type

Injects literal text and bracketed key presses.

```powershell
runmc --target bedrock type "hello[Enter]"
runmc --target bedrock type "[Escape][F2][Shift+F2]"
runmc --target bedrock type "[Control+A]replacement text[Enter]"
```

Bracketed keys use WebDriver/Playwright-style key names, for example `[Escape]`, `[Enter]`, `[Shift+F2]`, and `[Control+A]`.

Use `[[` and `]]` for literal square brackets.

### Resize

Resizes the target window so screenshots match the requested dimensions.

```powershell
runmc --target bedrock resize --width 1024 --height 768
runmc --target bedrock resize -w 1024 -h 768
```

### Screenshot

Captures the target window as a PNG.

```powershell
runmc --target bedrock screenshot --output shot.png
```

Optional cursor capture:

```powershell
runmc --target bedrock screenshot --include-cursor --output shot.png
```

Optional caption overlay:

```powershell
runmc --target education screenshot --caption "Before opening inventory" --output shot.png
```

Screenshots include `Captured from <window title> <version>` as a comment in file metadata.
