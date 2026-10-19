# Builds the embedded llama.cpp runtime for Android (libomni_llama.so) as a
# standalone check, without going through a full Flutter build.
#
# In normal development you do NOT need this: the Flutter app builds the library
# automatically via externalNativeBuild (see android/app/build.gradle.kts and
# native/README.md). Use this script when you want to verify the NDK build in
# isolation, or to produce the .so artifacts for inspection.
#
# Prerequisites (build-time only):
#   • Android NDK            (set $env:ANDROID_NDK_HOME or pass -Ndk)
#   • CMake 3.22+            (cmake --version)
#   • Git                    (git --version)
#   • Ninja (bundled with the Android SDK/CMake package)
#
# Usage:
#   pwsh ./build-android.ps1
#   pwsh ./build-android.ps1 -Tag b5153 -Abis arm64-v8a

[CmdletBinding()]
param(
    [string]$Tag = "b5153",
    [string]$Ndk = $env:ANDROID_NDK_HOME,
    [string[]]$Abis = @("arm64-v8a", "x86_64"),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$shimDir   = Join-Path $scriptDir "omni_llama"
$distDir   = Join-Path $scriptDir "dist/android"

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "'$Name' was not found on PATH. See native/README.md for prerequisites."
    }
}

Require-Command cmake
Require-Command git

if (-not $Ndk) {
    throw "Android NDK not found. Set ANDROID_NDK_HOME or pass -Ndk <path>. See native/README.md."
}
if (-not (Test-Path $Ndk)) { throw "NDK path does not exist: $Ndk" }

# The NDK ships its own toolchain file; this is what makes the desktop
# CMakeLists.txt produce a cross-compiled Android shared library.
$toolchain = Join-Path $Ndk "build/cmake/android.toolchain.cmake"
if (-not (Test-Path $toolchain)) { throw "NDK toolchain file not found: $toolchain" }

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

foreach ($abi in $Abis) {
    $buildDir = Join-Path $scriptDir "build/android-$abi"
    Write-Host "==> Configuring omni_llama for Android $abi (llama.cpp $Tag)" -ForegroundColor Cyan

    cmake -S $shimDir -B $buildDir -G Ninja `
          -DCMAKE_TOOLCHAIN_FILE=$toolchain `
          -DANDROID_ABI=$abi `
          -DANDROID_PLATFORM=android-24 `
          -DANDROID_STL=c++_shared `
          -DCMAKE_BUILD_TYPE=$Configuration `
          -DOMNI_LLAMA_CPP_TAG=$Tag
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed for $abi" }

    Write-Host "==> Building $abi" -ForegroundColor Cyan
    cmake --build $buildDir --parallel
    if ($LASTEXITCODE -ne 0) { throw "cmake build failed for $abi" }

    $so = Get-ChildItem -Path $buildDir -Recurse -Filter "libomni_llama.so" |
          Select-Object -First 1
    if (-not $so) { throw "libomni_llama.so was not produced for $abi" }

    $outDir = Join-Path $distDir $abi
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    Copy-Item $so.FullName (Join-Path $outDir "libomni_llama.so") -Force
    Write-Host "    -> $outDir/libomni_llama.so" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Done. Android runtimes are in native/dist/android/<abi>/libomni_llama.so" -ForegroundColor Green
