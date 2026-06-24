# Contributing

This project is a .NET 10 NativeAOT command-line tool. Contributions should keep the command surface small, predictable, and script-friendly.

## Development Setup

Requirements:

- Windows 11 or newer.
- .NET 10 SDK.

Useful commands from the repository root:

```powershell
dotnet build src/RunMc/RunMc.csproj
dotnet test runmc.slnx
dotnet publish src/RunMc/RunMc.csproj -c Release -r win-x64 -o publish/win-x64
```

## Validation Expectations

Before handing off changes, run the narrowest useful validation first, then broaden when appropriate.

Typical validation sequence:

```powershell
dotnet build src/RunMc/RunMc.csproj
dotnet test tests/RunMc.Tests/RunMc.Tests.csproj --filter <RelevantTestClassOrName>
dotnet test runmc.slnx
dotnet publish src/RunMc/RunMc.csproj -c Release -r win-x64 -o publish/win-x64
```

For behavior that touches live Windows capture or input injection, also run a manual smoke test with the published executable when possible.

## Architecture Notes

Core command parsing and orchestration live under `src/RunMc/Core` and `src/RunMc/Cli`. Code in these layers can generally be unit-tested, since interaction with the platform is done through interfaces.

Concrete Windows-specific implementations of those interfaces live under `src/RunMc/Platform/Windows` in the `RunMc.Windows` namespace.

## Target Configuration

Targets are represented by `TargetConfiguration` and `TargetApplication`. Built-in targets are currently created in `TargetParser`.

Currently, targets must define a package family name and AUMID. In the future, we may generalize this to support unpackaged applications and other platforms.

## Dependencies and Interop

To keep the application deployable as a single executable file, we must not add dependencies that cannot be statically linked into the executable. Win2D, WinAppSDK, SkiaSharp, etc. are examples of dependencies that would violate this rule. Prefer using functionality built in to the OS or pure .NET libraries that are NativeAOT-friendly.

Use CsWin32 wherever possible for interop with Windows APIs. Keep raw `DllImport`/`LibraryImport` usage out of application code unless CsWin32 cannot generate the needed API.

## Notes For Agents

- If you add or change CsWin32 symbols and need to inspect signatures, build once with generated files enabled:

```powershell
dotnet build src/RunMc/RunMc.csproj -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=obj/generated
```
