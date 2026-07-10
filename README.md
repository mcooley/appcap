# appcap

`appcap` is a screen recording and automation CLI tool.

Currently supports Windows 11.

## Installing
`appcap` is a single-file executable. Copy the exe to your target machine along with an `appcap.config.json` file (see [Targets](#targets)) and run it from the command line.

## Targets

`appcap` reads its targets from a JSON configuration file named `appcap.config.json` located next to the executable.

Use `--target` to choose a configured target. If the target is already running, `appcap` attaches to the running window. If not, it launches the installed app and waits for the window. If `--target` is omitted, `appcap` tries every configured target in order.

### Configuration file

In `appcap.config.json`, each entry under `targets` maps a target name to an application.

On Windows, run `Get-StartApps` to find the ID (AppUserModelID) of the application you wish to target and put that in the `id` property.

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

Use `--exclude-cursor` to omit the cursor from a screenshot.

Optional caption overlay:

```powershell
appcap --target calculator screenshot --caption "Before clearing the display" --output shot.png
```

### Record

Starts or stops a recording session for the target.

```powershell
appcap --target calculator record start --output recording.mp4
appcap --target calculator record stop
```

Recordings automatically save and stop after 30 minutes. To allow a longer recording, set
the limit in minutes (fractional minutes are supported):

```powershell
appcap --target calculator record start --output recording.mp4 --time-limit 90
```

Use `--exclude-cursor` with `record start` to omit the cursor from a recording.

Add a caption overlay to an active recording with `record caption`:

```powershell
appcap --target calculator record caption "Before clearing the display"
```

Captions appear immediately, remain visible for 3 seconds, then fade out. You can add captions repeatedly.
