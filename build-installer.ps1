[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# Some launchers can supply both `Path` and `PATH` in the same Windows
# environment block. MSBuild treats environment properties case-insensitively
# and otherwise crashes while constructing its property dictionary. Preserve
# the effective value and expose one canonical entry to this build process and
# every child process it starts.
$processPath = [Environment]::GetEnvironmentVariable(
    'Path',
    [EnvironmentVariableTarget]::Process)
if (-not [string]::IsNullOrWhiteSpace($processPath)) {
    [Environment]::SetEnvironmentVariable(
        'PATH',
        $null,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        'Path',
        $processPath,
        [EnvironmentVariableTarget]::Process)
}

$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot 'SynToolkit\SynToolkit.csproj'
$publishDirectory = Join-Path $projectRoot 'artifacts\publish'
$installerScript = Join-Path $projectRoot 'Installer\setup.iss'
$installerOutputDirectory = Join-Path $projectRoot 'Installer\Output'
$brandingScript = Join-Path $projectRoot 'tools\build-branding-assets.ps1'
$systemInformationTests = Join-Path $projectRoot 'SynToolkit.SystemInformationTests\SynToolkit.SystemInformationTests.csproj'
$powerPlanPath = Join-Path $projectRoot 'SynToolkit\Assets\PowerPlans\SOS.pow'
$expectedPowerPlanSha256 = '5AF6566BBD67663DEA73A8CA9513F5BDF1C882E23E1AEBB5C4C098D0988C2B13'

[xml]$projectXml = Get-Content -Raw -LiteralPath $projectFile
$applicationVersion = @($projectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ([string]::IsNullOrWhiteSpace($applicationVersion)) {
    throw 'The SynToolkit version could not be read from the project file.'
}

function Remove-GeneratedDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $rootPath = [System.IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a generated directory outside the project: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'Visual Studio Build Tools were not found.'
}

$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\amd64\MSBuild.exe' |
    Select-Object -First 1
if (-not $msbuild) {
    throw 'The 64-bit Visual Studio MSBuild executable was not found.'
}

if (-not (Test-Path -LiteralPath $powerPlanPath -PathType Leaf)) {
    throw "The embedded SOS power plan is missing: $powerPlanPath"
}
$actualPowerPlanSha256 = (Get-FileHash -LiteralPath $powerPlanPath -Algorithm SHA256).Hash
if (-not [string]::Equals(
    $actualPowerPlanSha256,
    $expectedPowerPlanSha256,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The embedded SOS power plan failed its checksum validation. Expected $expectedPowerPlanSha256 but found $actualPowerPlanSha256."
}
Write-Host "Validated embedded SOS.pow ($actualPowerPlanSha256)."

Write-Host 'Regenerating SynToolkit branding assets...'
& $brandingScript
if (-not $?) {
    throw 'Branding generation failed.'
}

Write-Host 'Running non-invasive SynToolkit service tests...'
& dotnet.exe run `
    --project $systemInformationTests `
    --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "SynToolkit service tests failed with exit code $LASTEXITCODE."
}

Write-Host "Publishing SynToolkit $applicationVersion ($Configuration, win-x64)..."
Remove-GeneratedDirectory -Path $publishDirectory
& $msbuild $projectFile `
    /restore `
    /t:Publish `
    /p:Configuration=$Configuration `
    /p:Platform=x64 `
    /p:RuntimeIdentifier=win-x64 `
    /p:SelfContained=true `
    /p:PublishDir="$publishDirectory\" `
    /v:minimal

if ($LASTEXITCODE -ne 0) {
    throw "SynToolkit publish failed with exit code $LASTEXITCODE."
}

$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $compiler) {
    throw 'Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php and run this script again.'
}

Write-Host 'Compiling the SynToolkit setup wizard...'
Remove-GeneratedDirectory -Path $installerOutputDirectory
& $compiler "/DMyAppVersion=$applicationVersion" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

Write-Host "Installer created in: $(Join-Path $projectRoot 'Installer\Output')"
