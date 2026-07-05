# appcap

`appcap` is a screen recording and automation CLI tool.

Currently supports Windows 11.

## Installing
`appcap` is a single-file executable. Copy the exe to your target machine along with an `appcap.config.json` file (see [Targets](#targets)) and run it from the command line.

## Targets

`appcap` reads its targets from a JSON configuration file named `appcap.config.json` located next to the executable.

Use `--target` to choose a configured target. If the target is already running, `appcap` attaches to the running window. If not, it launches the installed app and waits for the window. If `--target` is omitted, `appcap` tries every configured target in order.

### Configuration file

Each entry under `targets` maps a target name to an application. For now the only supported setting is `id`, the application's AUMID (Application User Model ID). The package family name is computed from the AUMID automatically.

```json
{
    "targets": {
        "calculator": {
            "id": "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"
        }
    }
}
```

You can define as many targets as you like:

```json
{
    "targets": {
        "calculator": {
            "id": "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"
        },
        "store": {
            "id": "Microsoft.WindowsStore_8wekyb3d8bbwe!App"
        }
    }
}
```

If the configuration file is missing or malformed, `appcap` prints a friendly error describing what to fix.

## Commands

### Focus

Launches or finds the target and brings its window to the foreground.

```powershell
appcap --target calculator
```

### Click

Taps the screen.

```powershell
appcap --target calculator click -x 151 -y 684
```

Coordinates are relative to the top-left corner of the target window.

### Hover

Moves the cursor.

```powershell
appcap --target calculator hover -x 151 -y 684
```

Coordinates are relative to the top-left corner of the target window.

### Type

Injects literal text and bracketed key presses.

```powershell
appcap --target calculator type "hello[Enter]"
appcap --target calculator type "[Escape][F2][Shift+F2]"
appcap --target calculator type "[Control+A]replacement text[Enter]"
```

Bracketed keys use WebDriver/Playwright-style key names, for example `[Escape]`, `[Enter]`, `[Shift+F2]`, and `[Control+A]`.

Use `[[` and `]]` for literal square brackets.

### Resize

Resizes the target window so screenshots match the requested dimensions.

```powershell
appcap --target calculator resize --width 1024 --height 768
appcap --target calculator resize -w 1024 -h 768
```

### Screenshot

Captures the target window as a PNG.

```powershell
appcap --target calculator screenshot --output shot.png
```

Optional cursor capture:

```powershell
appcap --target calculator screenshot --include-cursor --output shot.png
```

Optional caption overlay:

```powershell
appcap --target calculator screenshot --caption "Before clearing the display" --output shot.png
```

Screenshots include `Captured from <window title> <version>` as a comment in file metadata.

### Record

Starts or stops a recording session for the target.

```powershell
appcap --target calculator record start --output recording.mp4
appcap --target calculator record stop
```

Recording start/stop currently tracks recording lifecycle state for the target. MP4 encoding is planned separately.
