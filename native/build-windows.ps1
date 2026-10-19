# Builds the embedded llama.cpp runtime for Windows and drops it next to the
# OmniConnect backend so P/Invoke can find it at runtime.
#
# Prerequisites (build-time only — the shipped app stays dependency-free):
#   • Visual Studio 2022/2026 with the "Desktop development with C++" workload
#   • CMake 3.16+  (cmake --version)
#   • Git          (git --version)
#
# Usage:
#   pwsh ./build-windows.ps1                 # Release, x64
#   pwsh ./build-windows.ps1 -Tag b5153      # pin a different llama.cpp release

[CmdletBinding()]
param(
    [string]$Tag = "b5153",
    [string]$Configuration = "Release",
    [string]$Arch = "x64"
)

$ErrorActionPreference = "Stop"

$scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$shimDir     = Join-Path $scriptDir "omni_llama"
$buildDir    = Join-Path $scriptDir "build/windows-$Arch"
$distDir     = Join-Path $scriptDir "dist/win-$Arch"

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "'$Name' was not found on PATH. See native/README.md for prerequisites."
    }
}

Require-Command cmake
Require-Command git

Write-Host "==> Configuring omni_llama (llama.cpp $Tag, $Arch, $Configuration)" -ForegroundColor Cyan
cmake -S $shimDir -B $buildDir -G "Visual Studio 17 2022" -A $Arch `
      -DOMNI_LLAMA_CPP_TAG=$Tag -DCMAKE_BUILD_TYPE=$Configuration
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed" }

Write-Host "==> Building" -ForegroundColor Cyan
cmake --build $buildDir --config $Configuration --parallel
if ($LASTEXITCODE -ne 0) { throw "cmake build failed" }

$dll = Get-ChildItem -Path $buildDir -Recurse -Filter "omni_llama.dll" |
       Select-Object -First 1
if (-not $dll) { throw "omni_llama.dll was not produced by the build" }

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Copy-Item $dll.FullName (Join-Path $distDir "omni_llama.dll") -Force
Write-Host "==> Built $($dll.FullName)" -ForegroundColor Green

# Drop the DLL into every backend output folder that already exists so a plain
# `dotnet run` picks it up without a rebuild.
$backendRoot = Join-Path (Split-Path -Parent $scriptDir) "src/Backend"
$targets = Get-ChildItem -Path $backendRoot -Recurse -Directory -Filter "net10.0" -ErrorAction SilentlyContinue
foreach ($t in $targets) {
    Copy-Item (Join-Path $distDir "omni_llama.dll") (Join-Path $t.FullName "omni_llama.dll") -Force
    Write-Host "    copied -> $($t.FullName)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Done. omni_llama.dll is ready in native/dist/win-$Arch and in the backend output." -ForegroundColor Green
