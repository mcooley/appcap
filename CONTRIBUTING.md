# Contributing

This project is a .NET 10 NativeAOT command-line tool. Contributions should keep the command surface small, predictable, and script-friendly.

## Development Setup

Requirements:

- Windows 11 or newer.
- .NET 10 SDK.

Useful commands from the repository root:

```powershell
dotnet build src/AppCap/AppCap.csproj
dotnet test appcap.slnx
dotnet publish src/AppCap/AppCap.csproj -c Release -r win-x64 -o publish/win-x64
```

## Architecture

`appcap` is divided into three components — **client**, **worker**, and **target** — that
all ship in one executable but communicate over remotable protocols. The full design,
component responsibilities, lifecycle, and the two protocols are described in
[`docs/architecture.md`](docs/architecture.md).

Core command parsing and orchestration (the **client**) live under `src/AppCap/Core` and `src/AppCap/Cli`. Code in these layers can generally be unit-tested, since interaction with the platform is done through interfaces.

Concrete Windows-specific implementations of those interfaces (the **worker** and **target**) live under `src/AppCap/Platform/Windows` in the `AppCap.Windows` namespace.

## Validation Expectations

Before handing off changes, run the narrowest useful validation first, then broaden when appropriate.

Typical validation sequence:

```powershell
dotnet build src/AppCap/AppCap.csproj
dotnet test tests/AppCap.Tests/AppCap.Tests.csproj --filter <RelevantTestClassOrName>
dotnet test appcap.slnx
dotnet publish src/AppCap/AppCap.csproj -c Release -r win-x64 -o publish/win-x64
```

For behavior that touches live Windows capture or input injection, also run a manual smoke test with the published executable when possible.

## End-To-End Tests

End-to-end tests live in `tests/AppCap.E2ETests` so the unit test project can stay fast and deterministic. They are organized by command/feature area and run only against the packaged test app.

E2E tests are opt-in because they run the real CLI against an installed desktop app. They require a registered test app and a path to a previously-built `appcap.exe`.

To build/register the test app, publish `AppCap`, and run the E2E tests:

```powershell
powershell -ExecutionPolicy Bypass -File tests/AppCap.TestApp/package-test-app.ps1 -Install

$publishDirectory = Join-Path $PWD 'publish/win-x64'
dotnet publish src/AppCap/AppCap.csproj -c Release -r win-x64 -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$env:APPCAP_E2E_EXECUTABLE = Join-Path $publishDirectory 'AppCap.exe'
try {
	dotnet test tests/AppCap.E2ETests/AppCap.E2ETests.csproj
}
finally {
	Remove-Item Env:\APPCAP_E2E_EXECUTABLE -ErrorAction SilentlyContinue
}
```

`APPCAP_E2E_EXECUTABLE` can point to any previously-built `appcap.exe`; setting it is the only requirement to enable the E2E tests. Before running, the test harness copies the executable (and its published output directory) to a fresh temporary directory alongside its own `appcap.config.json` (defining the `testapp` target), so the original publish directory is left untouched.

The MCP conformance script uses the same previously-built executable and does not build
AppCap itself:

```powershell
$env:APPCAP_E2E_EXECUTABLE = Join-Path $publishDirectory 'AppCap.exe'
try {
	powershell -ExecutionPolicy Bypass -File tests/run-mcp-conformance.ps1
}
finally {
	Remove-Item Env:\APPCAP_E2E_EXECUTABLE -ErrorAction SilentlyContinue
}
```

The repository includes a packaged NativeAOT Win32 test app in `tests/AppCap.TestApp`. It exposes deterministic colored regions for pointer, keyboard, screenshot, and coordinate assertions. The packaging script writes an unsigned MSIX to `artifacts/testapp/AppCap.E2ETestApp.msix`; the `-Install` switch registers the generated package layout for local developer testing.

`AppCap.TestApp` uses GameInput as a wrapper over Windows input APIs. The GameInput redist must be installed on the machine running the tests. If it's not installed, run `winget install Microsoft.GameInput`.

## Target Configuration

Targets are represented by `TargetApplication`, which pairs a target name with an application `id`. At startup, `ConfigLoader` reads `appcap.config.json` from next to the executable and builds a `TargetCatalog`. When `--target` is omitted, the first configured target is used.

In the future, we may generalize this further to support unpackaged applications and other platforms.

## Dependencies and Interop

To keep the application deployable as a single executable file, we must not add dependencies that cannot be statically linked into the executable. Win2D, WinAppSDK, SkiaSharp, etc. are examples of dependencies that would violate this rule. Prefer using functionality built in to the OS or pure .NET libraries that are NativeAOT-friendly.

Use CsWin32 wherever possible for interop with Windows APIs. Keep raw `DllImport`/`LibraryImport` usage out of application code unless CsWin32 cannot generate the needed API.

## Notes For Agents

- If you add or change CsWin32 symbols and need to inspect signatures, build once with generated files enabled:

```powershell
dotnet build src/AppCap/AppCap.csproj -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=obj/generated
```
