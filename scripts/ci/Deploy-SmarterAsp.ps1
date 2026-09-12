# GitHub Actions only. This script performs production deployment when executed.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-PSDebug -Off

if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_EVENT_NAME -ne 'push' -or
    $env:GITHUB_REF -ne 'refs/heads/master' -or -not $env:RUNNER_TEMP) {
    throw 'Deployment requires the approved master-push GitHub Actions job.'
}
if ($env:RUNNER_DEBUG -eq '1') {
    throw 'Disable Actions debug logging before deploying with credentials.'
}
if (-not $env:DEPLOY_DIRECTORY) { throw 'Artifact staging directory is missing.' }
$directory = (Resolve-Path -LiteralPath $env:DEPLOY_DIRECTORY).Path
$tempRoot = (Resolve-Path -LiteralPath $env:RUNNER_TEMP).Path.TrimEnd('\') + '\'
if (-not $directory.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Artifact must be staged inside the runner temporary directory.'
}
if (-not (Get-ChildItem -LiteralPath $directory -Force)) { throw 'Artifact is empty.' }
if ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Linked artifact directory is forbidden.'
}

foreach ($relativePath in @(
    'VocabularyApp.WebApi.dll', 'VocabularyApp.WebApi.exe', 'VocabularyApp.Data.dll',
    'VocabularyApp.WebApi.deps.json', 'VocabularyApp.WebApi.runtimeconfig.json',
    'appsettings.json', 'web.config', 'wwwroot/index.html'
)) {
    $file = Get-Item -LiteralPath (Join-Path $directory $relativePath)
    if ($file.PSIsContainer -or $file.Length -eq 0) { throw "Missing or empty application output: $relativePath" }
}
foreach ($pattern in @('*.js', '*.css')) {
    $assets = @(Get-ChildItem -LiteralPath (Join-Path $directory 'wwwroot') -Filter $pattern -File |
        Where-Object { $_.Length -gt 0 })
    if ($assets.Count -eq 0) { throw "Missing Angular production bundles: $pattern" }
}
foreach ($entry in Get-ChildItem -LiteralPath $directory -Recurse -Force) {
    $relativePath = [IO.Path]::GetRelativePath($directory, $entry.FullName)
    if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked artifact entries are forbidden.' }
    if ($entry.Name.StartsWith('.') -or ($entry.Attributes -band [IO.FileAttributes]::Hidden)) {
        throw 'Hidden artifact entry is forbidden.'
    }
    if ($entry.PSIsContainer) {
        if ($entry.Name -match '^(src|obj|bin|node_modules|Properties|tests?|TestResults|VocabularyApp\.(UI|WebApi|Data|WebApi\.Tests))$') {
            throw 'Source or test directory found in artifact.'
        }
        continue
    }
    if ($entry.Name -match '(?i)(secrets|launchSettings|\.Tests\.|testhost|xunit|^app_offline\.htm$)' -or
        $entry.Extension -match '^\.(cs|csproj|sln|ts|map|pdb|pubxml|publishsettings|user|pfx|p12|pem|key|sql|ps1)$') {
        throw 'Nondeployable or sensitive file found in artifact.'
    }
    if ($entry.Name -like 'appsettings*' -and $relativePath -ne 'appsettings.json') {
        throw 'Unexpected configuration, including development settings, found in artifact.'
    }
    if ($entry.Name -eq 'web.config' -and $relativePath -ne 'web.config') {
        throw 'Nested web.config is forbidden.'
    }
}
Write-Host 'Downloaded application artifact passed pre-deployment validation.'

# The windows-2022 image includes the Visual Studio WebDeploy component.
# Check supported installation locations, then validate Microsoft Authenticode.
# Never resolve an executable from the checkout or an arbitrary PATH entry.
$msdeploy = $null
foreach ($programRoot in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
    if (-not $programRoot) { continue }
    $candidate = Join-Path $programRoot 'IIS/Microsoft Web Deploy V3/msdeploy.exe'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $signature = Get-AuthenticodeSignature -LiteralPath $candidate
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') {
            throw 'Installed Web Deploy executable does not have a valid Microsoft signature.'
        }
        $msdeploy = $candidate
        break
    }
}
if (-not $msdeploy) {
    throw 'Microsoft Web Deploy V3 is missing from windows-2022. Stop and review the runner image; no fallback download is permitted.'
}
Write-Host ('Microsoft Web Deploy discovered; file version: ' + (Get-Item -LiteralPath $msdeploy).VersionInfo.FileVersion)

# Quote MSDeploy provider values separately from Windows process arguments.
# Fail closed for values whose quoting cannot be represented unambiguously.
function ConvertTo-ProviderValue([string] $Value) {
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -match '[\x00-\x1f\x7f]') {
        throw 'A required Web Deploy value is empty or contains control characters.'
    }
    if (-not $Value.Contains("'")) { return "'" + $Value + "'" }
    if (-not $Value.Contains('"')) { return '"' + $Value + '"' }
    throw 'A Web Deploy value contains both quote types; a reviewed alternative credential transport is required.'
}
$endpointText = $env:SMARTERASP_WEBDEPLOY_URL
$endpoint = $null
if (-not [Uri]::TryCreate($endpointText, [UriKind]::Absolute, [ref] $endpoint) -or
    $endpoint.Scheme -ne 'https' -or $endpoint.UserInfo -or $endpoint.Fragment -or
    $endpoint.AbsolutePath -notmatch '(?i)/msdeploy\.axd$') {
    throw 'Web Deploy URL must be an HTTPS MSDeploy handler URL without embedded credentials or a fragment.'
}
$site = $env:SMARTERASP_SITE_NAME
if ([string]::IsNullOrWhiteSpace($site) -or $site -match '(^[/\\]|[/\\]$|\\|:|(^|/)\.{1,2}(/|$)|//)') {
    throw 'Site name must be the delegated IIS site/application name, not a URL or physical path.'
}
$source = '-source:contentPath=' + (ConvertTo-ProviderValue $directory)
$destination = '-dest:contentPath=' + (ConvertTo-ProviderValue $site) +
    ',computerName=' + (ConvertTo-ProviderValue $endpointText) +
    ',userName=' + (ConvertTo-ProviderValue $env:SMARTERASP_WEBDEPLOY_USERNAME) +
    ',password=' + (ConvertTo-ProviderValue $env:SMARTERASP_WEBDEPLOY_PASSWORD) +
    ',authType=Basic,includeAcls=False'

# Do not print arguments, native output, exceptions, or environment values.
# ArgumentList avoids shell evaluation and performs Windows argument escaping.
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $msdeploy
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment.Remove('SMARTERASP_WEBDEPLOY_PASSWORD') | Out-Null
foreach ($argument in @(
    '-verb:sync', $source, $destination,
    '-enableRule:DoNotDeleteRule', '-enableRule:AppOffline',
    '-disableLink:AppPoolExtension', '-disableLink:ContentExtension',
    '-disableLink:CertificateExtension', '-retryAttempts:0'
)) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
try {
    Write-Host 'Starting approved production content synchronization with AppOffline and destination-file preservation.'
    try {
        if (-not $process.Start()) { throw 'Process did not start.' }
        # Drain both streams concurrently to avoid pipe deadlock; raw text stays in memory.
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
    } catch {
        throw 'Web Deploy could not complete. Raw process diagnostics were suppressed to protect credentials; inspect the hosting state manually.'
    }
    # Allow only standard MSDeploy error identifiers out of native diagnostics.
    # Remove the literal credential first, even if it resembles an error identifier.
    $diagnostic = ($stdout + "`n" + $stderr).Replace($env:SMARTERASP_WEBDEPLOY_PASSWORD, '[REDACTED]')
    $codes = @([regex]::Matches($diagnostic, '\bERROR_[A-Z0-9_]+\b') |
        ForEach-Object { $_.Value } | Sort-Object -Unique)
    foreach ($code in $codes) { Write-Host "Web Deploy diagnostic code: $code" }
    if ($exitCode -ne 0) {
        throw "Web Deploy failed with exit code $exitCode. Content may be partial or offline; manual recovery is required."
    }
    Write-Host 'Web Deploy completed successfully. User verification of the live application is required.'
} finally {
    $process.Dispose()
    $start.ArgumentList.Clear()
    $destination = $null
    $env:SMARTERASP_WEBDEPLOY_PASSWORD = $null
}
