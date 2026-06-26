# appcap

`appcap` is a screen recording and automation CLI tool.

Currently supports Windows 11.

## Installing
`appcap` is a single-file executable with no dependencies. Copy the exe to your target machine and run it from the command line.

## Targets

Use `--target` to choose a configured target. If the target is already running, `appcap` attaches to the running window. If not, it launches the installed app and waits for the window.

Available targets:

- `bedrock`
- `bedrockpreview`
- `education`
- `testapp` - developer E2E test app, when registered locally

If `--target` is omitted, `appcap` tries the built-in targets in this order:

1. `bedrock`
2. `bedrockpreview`
3. `education`

## Commands

### Focus

Launches or finds the target and brings its window to the foreground.

```powershell
appcap --target bedrock
```

### Click

Taps the screen.

```powershell
appcap --target bedrock click -x 151 -y 684
```

Coordinates are relative to the top-left corner of the target window.

### Hover

Moves the cursor.

```powershell
appcap --target education hover -x 151 -y 684
```

Coordinates are relative to the top-left corner of the target window.

### Type

Injects literal text and bracketed key presses.

```powershell
appcap --target bedrock type "hello[Enter]"
appcap --target bedrock type "[Escape][F2][Shift+F2]"
appcap --target bedrock type "[Control+A]replacement text[Enter]"
```

Bracketed keys use WebDriver/Playwright-style key names, for example `[Escape]`, `[Enter]`, `[Shift+F2]`, and `[Control+A]`.

Use `[[` and `]]` for literal square brackets.

### Resize

Resizes the target window so screenshots match the requested dimensions.

```powershell
appcap --target bedrock resize --width 1024 --height 768
appcap --target bedrock resize -w 1024 -h 768
```

### Screenshot

Captures the target window as a PNG.

```powershell
appcap --target bedrock screenshot --output shot.png
```

Optional cursor capture:

```powershell
appcap --target bedrock screenshot --include-cursor --output shot.png
```

Optional caption overlay:

```powershell
appcap --target education screenshot --caption "Before opening inventory" --output shot.png
```

Screenshots include `Captured from <window title> <version>` as a comment in file metadata.

### Record

Starts or stops a recording session for the target.

```powershell
appcap --target bedrock record start --output recording.mp4
appcap --target bedrock record stop
```

Recording start/stop currently tracks recording lifecycle state for the target. MP4 encoding is planned separately.
