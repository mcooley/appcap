# appcap

`appcap` is a screen recorder and automation tool for targeting Windows apps. From a command line or agent, you can capture screenshots and video, send keyboard and pointer input, and control app windows without involving your whole desktop.

## Installing
`appcap` is a single-file executable. Copy the exe to your target machine along with an `appcap.config.json` file (see [Targets](#targets)) and run it from the command line.

### MCP Server

AppCap provides a stdio Model Context Protocol server with the same operations as
the CLI. Configure an MCP host to launch `appcap mcp`. Like the rest of the cli, configure targets with an
`appcap.config.json` file next to the executable.

## Targets

`appcap` reads its targets from a JSON configuration file named `appcap.config.json` located next to the executable.

Start a session by attaching a configured target:

```powershell
appcap target attach calculator
```

Attachment starts the shared worker process. By default it also launches the app when it
is not running. Use `--no-launch` to attach session state without launching it. If the
name is omitted, AppCap chooses the first running configured target, or the first
configured target when none are running, and prints the selected name.

All other operational commands require an attached target. If exactly one target is
attached, it is selected automatically. Use `--target` to select among multiple attached
targets. Operational commands never launch a closed app.

```powershell
appcap target list
appcap target detach calculator
```

`target list` shows every configured target and independently reports whether it is
attached and whether its app is running. Detaching cancels any recording, removes input
devices, and stops the worker after the last target is detached. Closing an app does not
detach its target.

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

### Input devices

Targets expose the input devices they support. The Windows target currently supports
`touch` and `keyboard`. Input commands automatically attach the device they need:

```powershell
appcap --target calculator inputdevice list
appcap --target calculator inputdevice remove touch
```

Only one device of each type can be attached to a target. `inputdevice list` shows both
supported device types and whether each is attached.

### Tap

Injects a touch tap into the target window. Coordinates are relative to the top-left
corner of the target window.

```powershell
appcap --target calculator tap 151,684
appcap --target calculator tap --device touch -x 151 -y 684
```

`tap` attaches and uses `touch` by default. `--device` explicitly selects it.

### Type

Injects literal text and bracketed key presses.

```powershell
appcap --target calculator type "hello[Enter]"
appcap --target calculator type "[Escape][F2][Shift+F2]"
appcap --target calculator type "[Control+A]replacement text[Enter]"
appcap --target calculator type --device keyboard "replacement text[Enter]"
```

Bracketed keys use WebDriver/Playwright-style key names, for example `[Escape]`, `[Enter]`, `[Shift+F2]`, and `[Control+A]`.

Use `[[` and `]]` for literal square brackets.

`type` attaches and uses `keyboard` by default. `--device` explicitly selects it.

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
appcap --target calculator screenshot --crop 160,0,320,240 --output shot.png
```

Use `--exclude-cursor` to omit the cursor from a screenshot.
Use `--crop x,y,width,height` to save a smaller portion of the frame.

Optional caption overlay:

```powershell
appcap --target calculator screenshot --caption "Before clearing the display" --output shot.png
```

### Record

Starts or stops a recording session for the target.

```powershell
appcap --target calculator record start --output recording.mp4
appcap --target calculator record start --crop 160,0,320,240 --output recording.mp4
appcap --target calculator record stop
appcap --target calculator record status
```

Recordings automatically save and stop after 30 minutes. To allow a longer recording, set
the limit in minutes (fractional minutes are supported):

```powershell
appcap --target calculator record start --output recording.mp4 --time-limit 90
```

Use `--exclude-cursor` with `record start` to omit the cursor from a recording. Use `--crop x,y,width,height` to save a smaller portion of the frame.

Add a caption overlay to an active recording with `record caption`:

```powershell
appcap --target calculator record caption "Before clearing the display"
```

Captions appear immediately, remain visible for 3 seconds, then fade out. You can add captions repeatedly.

`record status` reports `recording`, `stopped`, `cancelled`, `timed-out`, `app-closed`,
`failed`, or `never-started`, together with the output path or failure message when
applicable. The latest outcome is retained until another recording starts or the target
is detached.
