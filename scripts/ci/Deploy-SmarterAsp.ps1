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
    'VocabularyApp.WebApi.dll', 'VocabularyApp.Data.dll',
    'VocabularyApp.WebApi.deps.json', 'VocabularyApp.WebApi.runtimeconfig.json',
    'appsettings.json', 'web.config', 'wwwroot/index.html'
)) {
    $file = Get-Item -LiteralPath (Join-Path $directory $relativePath)
    if ($file.PSIsContainer -or $file.Length -eq 0) { throw "Missing or empty application output: $relativePath" }
}
if (Test-Path -LiteralPath (Join-Path $directory 'VocabularyApp.WebApi.exe')) {
    throw 'Portable artifact must not contain the WebApi apphost.'
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
. (Join-Path $PSScriptRoot 'Assert-Psh1WebConfig.ps1')
Assert-Psh1WebConfig -Path (Join-Path $directory 'web.config')
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

# Only sanitized failure diagnostics may be printed; never print argument arrays.
function Write-SanitizedDiagnostic([string] $Label, [string] $Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return }
    try {
        # Remove terminal control sequences before matching sensitive text.
        $safe = [regex]::Replace($Text, '\x1b\[[0-?]*[ -/]*[@-~]', '')
        $safe = [regex]::Replace($safe, '[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]', '')
        # Only values already available to this process; never fetch other secrets.
        $secretValues = @($env:SMARTERASP_WEBDEPLOY_PASSWORD) + @(
            Get-ChildItem Env: | Where-Object {
                $_.Name -match '(?i)(password|passwd|secret|token|api[_-]?key|connectionstrings?|authorization)'
            } | ForEach-Object { $_.Value }
        )
        $variants = foreach ($value in $secretValues) {
            if ([string]::IsNullOrEmpty($value)) { continue }
            $value
            [Uri]::EscapeDataString($value)
            [Net.WebUtility]::HtmlEncode($value)
            # JSON-escaped values can appear in structured diagnostic output.
            $json = ConvertTo-Json -InputObject $value -Compress
            $json.Substring(1, $json.Length - 2)
            [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($value))
        }
        $variants += [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(
            $env:SMARTERASP_WEBDEPLOY_USERNAME + ':' + $env:SMARTERASP_WEBDEPLOY_PASSWORD))
        foreach ($value in ($variants | Sort-Object Length -Descending)) {
            if (-not [string]::IsNullOrEmpty($value)) { $safe = $safe.Replace($value, '***') }
        }

        # Match sensitive assignments even when their value is not a known secret.
        # Quoted values can span lines; redact them before the conservative line rule.
        $key = '(?:[\w.-]*(?:password|passwd|pwd|secret|token|api[_-]?key|connectionstring|authorization)[\w.:-]*|user\s*id|uid|username)'
        $assignment = '(?i)\b' + $key + '["'']?\s*(?:=|:)\s*'
        # Containers/headers must be removed before line rules remove their opener.
        $safe = [regex]::Replace($safe, '(?is)<(?<tag>' + $key + ')\b[^>]*>.*?</\k<tag>\s*>', '***')
        $safe = [regex]::Replace($safe, '(?is)["''](?:ConnectionStrings|JwtSettings)["'']\s*:\s*\{.*?\}', '***')
        $safe = [regex]::Replace($safe, '(?im)\b(?:Authorization|Proxy-Authorization)\b[^\r\n]*(?:\r?\n[ \t]+[^\r\n]*)*', '***')
        $safe = [regex]::Replace($safe, '(?is)<[^>]*(?:password|passwd|secret|token|api[_-]?key|connectionstring)[^>]*>', '***')
        $safe = [regex]::Replace($safe, $assignment + '"(?:\\.|""|[^"\\])*"', '***')
        $safe = [regex]::Replace($safe, $assignment + "'(?:''|[^'])*'", '***')
        # Unknown/unquoted credentials: suppress the rest of that line, including
        # ambiguous delimiters, rather than guessing where the secret ends.
        $safe = [regex]::Replace($safe, $assignment + '[^\r\n]*', '***')
        $safe = [regex]::Replace($safe, '(?i)\b(?:Basic|Bearer)\s+[A-Za-z0-9+/_.=~-]+', '***')
        $safe = [regex]::Replace($safe, '\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b', '***')
        $safe = [regex]::Replace($safe, '(?i)\b[a-z][a-z0-9+.-]*://[^\s/]+@', '***@')
        # Suppress complete connection-string tails and echoed command tails.
        $safe = [regex]::Replace($safe, '(?im)\b(?:Server|Data Source|Initial Catalog|Database|Integrated Security)\s*=[^\r\n]*', '***')
        $safe = [regex]::Replace($safe, '(?im)(?:\bmsdeploy\.exe\b|-(?:dest|source|verb):)[^\r\n]*', '***')
    } catch {
        # Never allow a sanitizer exception to expose its input via error formatting.
        Write-Host ($Label + ': *** (diagnostic sanitization failed; output withheld)')
        return
    }
    if (-not [string]::IsNullOrWhiteSpace($safe)) {
        Write-Host ($Label + ':')
        foreach ($line in ($safe -split '\r\n|\n|\r')) {
            # Prefix every line so native text cannot become a GitHub workflow command.
            Write-Host ('| ' + $line)
        }
    }
}

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
    '-disableLink:CertificateExtension'
)) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
$exitCode = $null
try {
    Write-Host 'Starting approved production content synchronization with AppOffline and destination-file preservation.'
    try {
        if (-not $process.Start()) { throw 'Process did not start.' }
        # Drain both streams concurrently to avoid pipe deadlock; raw text stays in memory.
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        # Capture directly before reading task results; no pipeline or LASTEXITCODE.
        $exitCode = $process.ExitCode
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
    } catch {
        Write-SanitizedDiagnostic 'Sanitized process exception' $_.Exception.Message
        if ($null -ne $exitCode) { Write-Host "MSDeploy exit code: $exitCode" }
        throw 'Web Deploy process start or output capture failed. Inspect sanitized diagnostics and hosting state manually.'
    }
    if ($exitCode -ne 0) {
        Write-Host 'MSDeploy failed.'
        Write-SanitizedDiagnostic 'Sanitized stdout' $stdout
        Write-SanitizedDiagnostic 'Sanitized stderr' $stderr
        if ([string]::IsNullOrWhiteSpace($stdout) -and [string]::IsNullOrWhiteSpace($stderr)) {
            Write-Host 'MSDeploy returned no stdout or stderr.'
        }
        Write-Host "MSDeploy exit code: $exitCode"
        throw "Web Deploy failed with exit code $exitCode. Content may be partial or offline; manual recovery is required."
    }
    Write-Host 'Web Deploy completed successfully. User verification of the live application is required.'
} finally {
    $process.Dispose()
    $start.ArgumentList.Clear()
    $destination = $null
    $env:SMARTERASP_WEBDEPLOY_PASSWORD = $null
}
