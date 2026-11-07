param(
    [Parameter(Position = 0)]
    [ValidateSet("dev", "run", "build", "setup", "test", "clean")]
    [string]$Command = "dev",

    [Parameter(Position = 1)]
    [string]$Project = "src\Rambl.csproj",

    [string]$Framework = "net10.0-windows10.0.19041.0",

    [string]$InnoSetupCompiler
)

$projectPath = Join-Path $PSScriptRoot $Project

if (-not (Test-Path $projectPath)) {
    throw "Project not found: $projectPath"
}

switch ($Command) {
    "dev" {
        dotnet watch --project $projectPath run -f $Framework
    }

    "run" {
        dotnet run --project $projectPath -f $Framework
    }

    "build" {
        $output = Join-Path $PSScriptRoot 'artifacts\rambl'
        Remove-Item (Join-Path $output 'Rambl.pdb') -ErrorAction SilentlyContinue

        dotnet publish $projectPath -f $Framework -c Release `
            -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None `
            -p:WindowsAppSDKSelfContained=true -p:SelfContained=true `
            -p:PublishSingleFile=true -p:EnableMsixTooling=true `
            -p:IncludeAllContentForSelfExtract=true -p:RamblHybridPublish=true `
            -p:CopyOutputSymbolsToPublishDirectory=false `
            "-p:PublishDir=$output\"
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    }

    "setup" {
        $iscc = if ($InnoSetupCompiler) { $InnoSetupCompiler } else {
            Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
        }
        if (-not (Test-Path $iscc)) {
            throw 'Inno Setup 6 is required to build the installer.'
        }

        [xml]$projectXml = Get-Content $projectPath
        $version = $projectXml.Project.PropertyGroup.ApplicationDisplayVersion
        if (-not $version) {
            throw "ApplicationDisplayVersion was not found in $projectPath."
        }

        dotnet publish $projectPath -f $Framework -c Release `
            -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None `
            -p:WindowsAppSDKSelfContained=true -p:SelfContained=true
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

        $artifacts = Join-Path $PSScriptRoot 'artifacts'
        New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
        $redist = Join-Path $artifacts 'vc_redist.x64.exe'
        Invoke-WebRequest 'https://aka.ms/vc14/vc_redist.x64.exe' -OutFile $redist
        $signature = Get-AuthenticodeSignature $redist
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Microsoft Corporation(?:,|$)') {
            throw 'The downloaded Microsoft Visual C++ Redistributable has an invalid publisher signature.'
        }

        & $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'tools\Rambl.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
    }

    "test" {
        dotnet test $projectPath
    }

    "clean" {
        dotnet clean $projectPath -f $Framework
    }
}
