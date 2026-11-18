param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$SourceSha256,
    [Parameter(Mandatory)][string]$CacheDirectory
)

$ErrorActionPreference = 'Stop'
$cache = [IO.Path]::GetFullPath($CacheDirectory)
New-Item -ItemType Directory -Force $cache | Out-Null
$archive = Join-Path $cache "llama-$Version.tar.gz"
if (!(Test-Path -LiteralPath $archive)) {
    $download = New-Object Net.WebClient
    try { $download.DownloadFile("https://github.com/ggml-org/llama.cpp/archive/refs/tags/$Version.tar.gz", $archive) }
    finally { $download.Dispose() }
}
$hash = [Security.Cryptography.SHA256]::Create()
$stream = [IO.File]::OpenRead($archive)
try { $actualHash = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
finally { $stream.Dispose(); $hash.Dispose() }
if ($actualHash -ne $SourceSha256) {
    throw "The llama.cpp source does not match its pinned SHA-256. Delete $archive and retry."
}
# GitHub omits a leading v from the archive's top-level directory.
$source = Join-Path $cache "llama.cpp-$($Version -replace '^v', '')"
if (!(Test-Path -LiteralPath (Join-Path $source 'CMakeLists.txt'))) {
    & "$env:SystemRoot/System32/tar.exe" -xzf $archive -C $cache
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the llama.cpp source.' }
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio C++ build tools and CMake to build the native language runtime.' }
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 Microsoft.VisualStudio.Component.VC.CMake.Project -property installationPath
if (!$visualStudio) { throw 'Install the Visual Studio Desktop development with C++ workload, including CMake.' }
$cmake = Join-Path $visualStudio 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
foreach ($variant in @(@{ Folder = 'build'; Avx2 = 'OFF' }, @{ Folder = 'build-avx2'; Avx2 = 'ON' })) {
    $build = Join-Path $cache $variant.Folder
    & $cmake -S (Join-Path $PSScriptRoot 'native/llama') -B $build -A x64 `
        "-DLLAMA_SOURCE_DIR=$source" "-DRAMBL_LLAMA_VERSION=$Version" "-DRAMBL_CPU_AVX2=$($variant.Avx2)"
    if ($LASTEXITCODE -ne 0) { throw "Could not configure the native language runtime ($($variant.Folder))." }
    & $cmake --build $build --config Release --target rambl-llama --parallel 4
    if ($LASTEXITCODE -ne 0) { throw "Could not build the native language runtime ($($variant.Folder))." }
}
