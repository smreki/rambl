param(
    [Parameter(Position = 0)]
    [ValidateSet("dev", "run", "build", "test", "clean")]
    [string]$Command = "dev",

    [Parameter(Position = 1)]
    [string]$Project = "src\Rambl.csproj",

    [string]$Framework = "net10.0-windows10.0.19041.0"
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
        dotnet build $projectPath -f $Framework
    }

    "test" {
        dotnet test $projectPath
    }

    "clean" {
        dotnet clean $projectPath -f $Framework
    }
}
