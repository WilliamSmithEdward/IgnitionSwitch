[CmdletBinding()]
param(
    [string]$Python = 'python',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$Executable,
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE."
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$previousConfiguration = $env:Configuration
$previousPlatform = $env:Platform
Push-Location -LiteralPath $repositoryRoot
try {
    $env:Configuration = $Configuration
    $env:Platform = 'x64'
    Invoke-Checked -Executable $Python -Arguments @(
        '-c',
        'import sys; sys.exit(0 if sys.version_info >= (3, 11) else "Python 3.11 or later is required.")'
    )

    if (-not $SkipRestore) {
        Write-Host 'Restoring locked dependencies…'
        Invoke-Checked -Executable 'dotnet' -Arguments @(
            'restore', 'IgnitionSwitch.slnx', '--locked-mode', '-p:Platform=x64'
        )
    }

    Write-Host 'Building the solution…'
    Invoke-Checked -Executable 'dotnet' -Arguments @(
        'build', 'IgnitionSwitch.slnx', '-c', $Configuration, '-p:Platform=x64', '--no-restore'
    )

    $testProjects = @('tests/IgnitionSwitch.Core.Tests', 'tests/IgnitionSwitch.App.Tests')
    if (Test-Path -LiteralPath 'tests/IgnitionSwitch.Midi.Tests/IgnitionSwitch.Midi.Tests.csproj') {
        $testProjects += 'tests/IgnitionSwitch.Midi.Tests'
    }
    foreach ($testProject in $testProjects) {
        Write-Host "Running $testProject…"
        Invoke-Checked -Executable 'dotnet' -Arguments @(
            'run', '--project', $testProject, '-c', $Configuration,
            '-p:Platform=x64', '--no-build', '--no-restore'
        )
    }

    Write-Host 'Checking prettyCSharp formatting…'
    Invoke-Checked -Executable $Python -Arguments @(
        '-m', 'prettycsharp', 'check', 'IgnitionSwitch.slnx'
    )

    Write-Host 'Checking code style, including informational diagnostics…'
    Invoke-Checked -Executable 'dotnet' -Arguments @(
        'format', 'style', 'IgnitionSwitch.slnx', '--verify-no-changes',
        '--severity', 'info', '--no-restore'
    )

    Write-Host 'Checking analyzers, including informational diagnostics…'
    Invoke-Checked -Executable 'dotnet' -Arguments @(
        'format', 'analyzers', 'IgnitionSwitch.slnx', '--verify-no-changes',
        '--severity', 'info', '--no-restore'
    )

    Write-Host 'Verification passed.'
}
finally {
    $env:Configuration = $previousConfiguration
    $env:Platform = $previousPlatform
    Pop-Location
}
