param(
    [string] $Configuration = 'Release',
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\testapp'),
    [switch] $Install
)

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'AppCap.TestApp.csproj'
$publishDirectory = Join-Path $OutputDirectory 'publish'
$packageDirectory = Join-Path $OutputDirectory 'package'
$assetsDirectory = Join-Path $packageDirectory 'Assets'
$msixPath = Join-Path $OutputDirectory 'AppCap.E2ETestApp.msix'
$makeAppx = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter makeappx.exe |
    Where-Object { $_.FullName -like '*\x64\makeappx.exe' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $makeAppx) {
    throw 'makeappx.exe was not found. Install the Windows SDK to package the E2E test app.'
}

Get-Process -Name 'AppCap.TestApp' -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item $publishDirectory, $packageDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $OutputDirectory 'package-secondary'), (Join-Path $OutputDirectory 'AppCap.E2ETestApp.Secondary.msix') -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publishDirectory, $packageDirectory, $assetsDirectory -Force | Out-Null

dotnet publish $projectPath -c $Configuration -r win-x64 -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

Copy-Item (Join-Path $publishDirectory '*') $packageDirectory -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'AppxManifest.xml') (Join-Path $packageDirectory 'AppxManifest.xml') -Force

Add-Type -AssemblyName System.Drawing
foreach ($asset in @(
    @{ Path = Join-Path $assetsDirectory 'StoreLogo.png'; Size = 50 },
    @{ Path = Join-Path $assetsDirectory 'Square44x44Logo.png'; Size = 44 },
    @{ Path = Join-Path $assetsDirectory 'Square150x150Logo.png'; Size = 150 }
)) {
    $bitmap = [System.Drawing.Bitmap]::new($asset.Size, $asset.Size)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::FromArgb(10, 90, 140))
        }
        finally {
            $graphics.Dispose()
        }

        $bitmap.Save($asset.Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

& $makeAppx pack /d $packageDirectory /p $msixPath /overwrite
if ($LASTEXITCODE -ne 0) {
    throw 'makeappx pack failed.'
}

if ($Install) {
    foreach ($identityName in 'AppCap.E2ETestApp', 'AppCap.E2ETestApp.Secondary') {
        Get-AppxPackage -Name $identityName | Remove-AppxPackage
    }
    Add-AppxPackage -Register (Join-Path $packageDirectory 'AppxManifest.xml') -ForceApplicationShutdown
}

Write-Host "MSIX: $msixPath"
Write-Host "Package layout: $packageDirectory"
Write-Host 'Targets: testapp, testapp-secondary'