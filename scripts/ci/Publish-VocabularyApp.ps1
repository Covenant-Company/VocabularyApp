# Run only after the existing build and Angular staging steps in GitHub Actions.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $env:GITHUB_WORKSPACE -or -not $env:RUNNER_TEMP -or -not $env:GITHUB_OUTPUT) {
    throw 'This script requires the GitHub Actions workspace, temporary directory, and output file.'
}
$workspace = (Resolve-Path -LiteralPath $env:GITHUB_WORKSPACE).Path
$project = Join-Path $workspace 'VocabularyApp.WebApi/VocabularyApp.WebApi.csproj'
$browserOutput = Join-Path $workspace 'VocabularyApp.UI/dist/vocabulary-app.ui/browser'
$tempRoot = (Resolve-Path -LiteralPath $env:RUNNER_TEMP).Path
# A unique directory avoids stale output and requires no recursive deletion.
$publishDirectory = Join-Path $tempRoot ('VocabularyApp-publish-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $publishDirectory | Out-Null

# Pin reviewed non-secret defaults, normalizing checkout line endings.
# Changes require explicit configuration review and a new digest, not secret substitution.
function Assert-ReviewedSettings([string] $Path) {
    $text = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n").Trim()
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $digest = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '')
    } finally {
        $sha.Dispose()
    }
    if ($digest -ne '4D7BBE487806840C228B920060B93757547389D811A03FDA9CB3082E1D3E9965') {
        throw 'appsettings.json differs from reviewed non-secret defaults; review before packaging.'
    }
}
Assert-ReviewedSettings (Join-Path $workspace 'VocabularyApp.WebApi/appsettings.json')

# Override project RIDs for this artifact only; use the installed dotnet host.
dotnet restore $project -p:RuntimeIdentifier= -p:UseAppHost=false -p:SelfContained=false
if ($LASTEXITCODE -ne 0) { throw 'WebApi publish restore failed.' }
# Rebuild after Angular staging; do not use --no-build with earlier static assets.
dotnet publish $project --configuration Release -p:RuntimeIdentifier= -p:UseAppHost=false --self-contained false --no-restore --output $publishDirectory -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'WebApi publish failed.' }

$developmentSettings = Join-Path $publishDirectory 'appsettings.Development.json'
if (Test-Path -LiteralPath $developmentSettings -PathType Leaf) {
    Remove-Item -LiteralPath $developmentSettings
}

foreach ($relativePath in @(
    'VocabularyApp.WebApi.dll', 'VocabularyApp.Data.dll',
    'VocabularyApp.WebApi.deps.json', 'VocabularyApp.WebApi.runtimeconfig.json',
    'appsettings.json', 'web.config', 'wwwroot/index.html'
)) {
    $file = Get-Item -LiteralPath (Join-Path $publishDirectory $relativePath)
    if ($file.PSIsContainer -or $file.Length -eq 0) { throw "Missing or empty required output: $relativePath" }
}
Assert-ReviewedSettings (Join-Path $publishDirectory 'appsettings.json')
if (Test-Path -LiteralPath (Join-Path $publishDirectory 'VocabularyApp.WebApi.exe')) {
    throw 'Portable publish must not contain the WebApi apphost.'
}
$publishedStaticRoot = Join-Path $publishDirectory 'wwwroot'
foreach ($pattern in @('*.js', '*.css')) {
    if (-not (Get-ChildItem -LiteralPath $publishedStaticRoot -Filter $pattern -File)) {
        throw "Missing Angular production bundles: $pattern"
    }
}
# Verify every produced browser asset survived staging and publishing unchanged.
foreach ($asset in Get-ChildItem -LiteralPath $browserOutput -File -Recurse -Force) {
    $relativePath = [IO.Path]::GetRelativePath($browserOutput, $asset.FullName)
    if ($relativePath -eq 'web.config') { continue }
    $publishedAsset = Join-Path $publishedStaticRoot $relativePath
    if (-not (Test-Path -LiteralPath $publishedAsset -PathType Leaf)) {
        throw "Missing published Angular asset: $relativePath"
    }
    if ((Get-FileHash -LiteralPath $asset.FullName).Hash -ne (Get-FileHash -LiteralPath $publishedAsset).Hash) {
        throw "Published Angular asset differs: $relativePath"
    }
}

foreach ($entry in Get-ChildItem -LiteralPath $publishDirectory -Recurse -Force) {
    $relativePath = [IO.Path]::GetRelativePath($publishDirectory, $entry.FullName)
    if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked publish entries are not allowed.' }
    if ($entry.Name.StartsWith('.') -or ($entry.Attributes -band [IO.FileAttributes]::Hidden)) {
        throw "Hidden publish entry is not allowed: $relativePath"
    }
    if ($entry.PSIsContainer) {
        if ($entry.Name -match '^(src|obj|bin|node_modules|Properties|tests?|TestResults|VocabularyApp\.WebApi\.Tests)$') {
            throw "Source or test directory in publish output: $relativePath"
        }
        continue
    }
    if ($entry.Name -match '(?i)(secrets|launchSettings|\.Tests\.|testhost|xunit)' -or
        $entry.Extension -match '^\.(cs|csproj|sln|ts|map|pdb|pubxml|publishsettings|user|pfx|p12|pem|key|sql|ps1)$') {
        throw "Nondeployable or sensitive file in publish output: $relativePath"
    }
    if ($entry.Name -like 'appsettings*' -and $relativePath -ne 'appsettings.json') {
        throw "Unexpected application configuration: $relativePath"
    }
    if ($entry.Name -eq 'web.config' -and $relativePath -ne 'web.config') {
        throw 'Nested web.config is forbidden, including under wwwroot.'
    }
}

[xml] $iis = Get-Content -LiteralPath (Join-Path $publishDirectory 'web.config') -Raw
$hosting = $iis.SelectSingleNode('/configuration/location/system.webServer/aspNetCore')
$handler = $iis.SelectSingleNode('/configuration/location/system.webServer/handlers/add[@modules="AspNetCoreModuleV2"]')
if (-not $hosting -or -not $handler -or $hosting.GetAttribute('hostingModel') -ne 'inprocess') {
    throw 'Missing expected ASP.NET Core IIS hosting configuration.'
}
$process = $hosting.GetAttribute('processPath')
if ($process -ne 'dotnet' -or $hosting.GetAttribute('arguments') -ne '.\VocabularyApp.WebApi.dll') {
    throw 'IIS configuration does not launch the expected WebApi application.'
}
if ($iis.SelectNodes('//environmentVariable').Count -ne 0 -or $iis.SelectNodes('//connectionStrings').Count -ne 0) {
    throw 'Runtime environment values or connection strings must not be embedded in web.config.'
}
$runtime = Get-Content -LiteralPath (Join-Path $publishDirectory 'VocabularyApp.WebApi.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not ($runtime.runtimeOptions.frameworks | Where-Object { $_.name -eq 'Microsoft.AspNetCore.App' -and $_.version -like '8.*' })) {
    throw 'Expected framework-dependent ASP.NET Core 8 runtime configuration.'
}
if ($runtime.runtimeOptions.tfm -ne 'net8.0' -or
    -not ($runtime.runtimeOptions.frameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' -and $_.version -like '8.*' }) -or
    $runtime.runtimeOptions.PSObject.Properties['includedFrameworks']) {
    throw 'Expected shared .NET 8 frameworks, not a bundled runtime.'
}
$deps = Get-Content -LiteralPath (Join-Path $publishDirectory 'VocabularyApp.WebApi.deps.json') -Raw | ConvertFrom-Json
if ($deps.runtimeTarget.name -ne '.NETCoreApp,Version=v8.0') {
    throw 'Expected a portable .NET 8 dependency target without a runtime identifier.'
}
# Expose the path only after all validations pass. Upload is the next gated step.
"publish-directory=$publishDirectory" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8
Write-Host 'Publish output validated; ready for GitHub artifact upload.'
