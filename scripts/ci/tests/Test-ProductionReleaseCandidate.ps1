# Offline only. Never execute the deployment script or a native process.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$helper = Join-Path $root 'scripts/ci/Assert-ProductionReleaseCandidate.ps1'
$deployPath = Join-Path $root 'scripts/ci/Deploy-SmarterAsp.ps1'
$script:passed = 0
function Check([bool] $Condition, [string] $Label) {
    if (-not $Condition) { throw ('Offline release test failed: ' + $Label) }
    $script:passed++
}
function Parse-Script([string] $Path) {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    Check ($errors.Count -eq 0) ('PowerShell syntax: ' + [IO.Path]::GetFileName($Path))
    return $ast
}
$helperAst = Parse-Script $helper
Check (@($helperAst.EndBlock.Statements | Where-Object {
    $_ -isnot [Management.Automation.Language.FunctionDefinitionAst]
}).Count -eq 0) 'dot-source contains definitions only'
. $helper

# Extract the real process setup and outer try/finally, not a parallel imitation
# of guard->start ordering. Replace only native construction and helper loading.
$deployAst = Parse-Script $deployPath
$statements = @($deployAst.EndBlock.Statements)
$setupIndex = -1
for ($i = 0; $i -lt $statements.Count; $i++) {
    if ($statements[$i].Extent.Text -eq '$start = [Diagnostics.ProcessStartInfo]::new()') { $setupIndex = $i }
}
Check ($setupIndex -ge 0) 'real process setup found'
$outer = $statements[-1]
Check ($outer -is [Management.Automation.Language.TryStatementAst]) 'last deployment statement is cleanup-protected try'
$body = @($outer.Body.Statements)
Check ($body[0].Extent.Text -eq ". (Join-Path `$PSScriptRoot 'Assert-ProductionReleaseCandidate.ps1')") 'helper loaded in outer try'
Check ($body[1].Extent.Text -eq 'Assert-ProductionReleaseCandidate') 'production guard has no bypass/request override'
Check ($body[2].Extent.Text -like "Write-Host 'Starting approved production*") 'only start log follows guard'
Check ($body[3] -is [Management.Automation.Language.TryStatementAst]) 'process start immediately follows log'
Check ($body[3].Body.Statements[0].Extent.Text -eq "if (-not `$process.Start()) { throw 'Process did not start.' }") 'actual process-start boundary'
$setup = ($statements[$setupIndex..($statements.Count - 2)] | ForEach-Object { $_.Extent.Text }) -join "`n"
Check (([regex]::Matches($setup, '\[Diagnostics\.Process\]::new\(\)')).Count -eq 1) 'one native process construction'
$setup = $setup.Replace('[Diagnostics.Process]::new()', '(New-RecordedProcess)')
$outerText = $outer.Extent.Text.Replace($body[0].Extent.Text, '# Helper already loaded; fixture transport stays installed.')
$script:boundary = [scriptblock]::Create($setup + "`n" + $outerText)
# Use the existing sanitizer for simulated native failures too.
$sanitizer = $statements | Where-Object {
    $_ -is [Management.Automation.Language.FunctionDefinitionAst] -and $_.Name -eq 'Write-SanitizedDiagnostic'
}
. ([scriptblock]::Create($sanitizer.Extent.Text))

$script:tokenMarker = 'OFFLINE_RELEASE_TOKEN_DO_NOT_LOG'
$script:passwordMarker = 'OFFLINE_DEPLOY_PASSWORD_DO_NOT_LOG'
$script:a = 'a' * 40; $script:b = 'b' * 40; $script:c = 'c' * 40
$script:starts = 0; $script:requests = 0; $script:disposals = 0
$script:response = $null; $script:requestFailure = ''; $script:exitCode = 0
$script:advanceAfterRead = ''
$script:observedHead = $script:a

# Override the read-only transport, never the assertion. No test calls the network.
function Invoke-ProductionMasterRefRequest {
    param([Net.Http.HttpRequestMessage] $Request)
    $script:requests++
    Check ($Request.Method.Method -ceq 'GET') 'read-only request'
    Check ($Request.RequestUri.AbsoluteUri -ceq 'https://api.github.com/repos/Covenant-Company/VocabularyApp/git/ref/heads/master') 'fixed master endpoint'
    Check ($Request.Headers.Authorization.Scheme -ceq 'Bearer') 'Bearer scheme'
    Check ($Request.Headers.Authorization.Parameter -ceq $script:tokenMarker) 'fixture token used'
    Check ($Request.Headers.Accept.ToString() -ceq 'application/vnd.github+json') 'GitHub JSON Accept'
    Check ($Request.Headers.UserAgent.ToString() -ceq 'VocabularyApp-ReleaseGuard/1.0') 'explicit User-Agent'
    Check $Request.Headers.CacheControl.NoCache 'no-cache'
    Check ((@($Request.Headers.GetValues('X-GitHub-Api-Version')) -join '') -ceq '2026-03-10') 'API version'
    if ($script:requestFailure) { throw $script:requestFailure }
    if ($script:advanceAfterRead) { $script:observedHead = $script:advanceAfterRead }
    return $script:response
}
function New-RecordedProcess {
    $stream = [pscustomobject]@{}
    $stream | Add-Member ScriptMethod ReadToEndAsync { return [Threading.Tasks.Task]::FromResult([string]'') }
    $process = [pscustomobject]@{
        StartInfo = $null; StandardOutput = $stream; StandardError = $stream; ExitCode = $script:exitCode
    }
    $process | Add-Member ScriptMethod Start {
        Check (-not $this.StartInfo.Environment.ContainsKey('RELEASE_GITHUB_TOKEN')) 'child excludes release token'
        Check (-not $this.StartInfo.Environment.ContainsKey('SMARTERASP_WEBDEPLOY_PASSWORD')) 'child excludes password variable'
        Check ([string]::IsNullOrEmpty($env:RELEASE_GITHUB_TOKEN)) 'parent release token cleared before start'
        $script:starts++
        return $true
    }
    $process | Add-Member ScriptMethod WaitForExit { }
    $process | Add-Member ScriptMethod Dispose { $script:disposals++ }
    return $process
}
function Ref-Response([string] $Sha) {
    return [pscustomobject]@{ Status = 200; Body = (@{
        ref = 'refs/heads/master'; object = @{ type = 'commit'; sha = $Sha }
    } | ConvertTo-Json -Compress) }
}
function Invoke-Attempt([string] $Candidate, [string] $Head, [string] $Expected = '',
    [hashtable] $Overrides = @{}, $Response = $null, [string] $Failure = '', [int] $NativeExit = 0) {
    $context = @{
        GITHUB_ACTIONS = 'true'; GITHUB_EVENT_NAME = 'push'; GITHUB_REF = 'refs/heads/master'
        GITHUB_REPOSITORY = 'Covenant-Company/VocabularyApp'; GITHUB_SHA = $Candidate
        RELEASE_GITHUB_TOKEN = $script:tokenMarker; SMARTERASP_WEBDEPLOY_PASSWORD = $script:passwordMarker
    }
    foreach ($key in $Overrides.Keys) { $context[$key] = $Overrides[$key] }
    foreach ($key in $context.Keys) { [Environment]::SetEnvironmentVariable($key, $context[$key], 'Process') }
    $script:response = if ($null -ne $Response) { $Response } else { Ref-Response $Head }
    $script:requestFailure = $Failure; $script:exitCode = $NativeExit
    $before = $script:starts; $disposed = $script:disposals
    $diagnostics = [Collections.Generic.List[string]]::new()
    $caught = ''; $accepted = $false
    try {
        & {
            # These values only construct arguments; native process is replaced above.
            $msdeploy = 'OFFLINE-NOT-EXECUTED.exe'
            $source = '-source:contentPath=offline'; $destination = '-dest:contentPath=offline'
            . $script:boundary
        } *>&1 | ForEach-Object { $diagnostics.Add([string]$_) }
        $accepted = $true # models the workflow's subsequent acceptance step
    } catch { $caught = $_.Exception.Message; $diagnostics.Add([string]$_) }
    Check (($diagnostics -join "`n") -notmatch 'OFFLINE_(RELEASE_TOKEN|DEPLOY_PASSWORD)_DO_NOT_LOG') 'no credential in diagnostics'
    Check ([string]::IsNullOrEmpty($env:RELEASE_GITHUB_TOKEN)) 'release token cleared on all paths'
    Check ([string]::IsNullOrEmpty($env:SMARTERASP_WEBDEPLOY_PASSWORD)) 'existing password cleanup retained'
    Check ($script:disposals -eq $disposed + 1) 'outer finally disposes on all paths'
    if ($Expected) {
        Check ($caught.Contains($Expected)) ('expected rejection: ' + $Expected)
        Check (-not $accepted) 'acceptance not reached after failure'
        Check ($script:starts -eq $before + [int]($NativeExit -ne 0)) 'failure occurs at expected side of start'
    } else {
        Check ($caught -eq '') ('current candidate accepted: ' + $caught)
        Check ($accepted -and $script:starts -eq $before + 1) 'exactly one process start then acceptance'
    }
}

# A retained queue model makes admission distinct from active deployment.
# GitHub owns the real lock; YAML structure is checked separately below.
function Reset-Queue([string] $Head) {
    $script:queue = [pscustomobject]@{
        Head = $Head; Active = ''; Production = ''; Pending = [Collections.Generic.List[string]]::new()
    }
}
function Enqueue-Candidate([string] $Sha, [bool] $CiPassed = $true) {
    if ($CiPassed) { $script:queue.Pending.Add($Sha) }
}
function Admit-Next([int] $NativeExit = 0) {
    if ($script:queue.Active -or $script:queue.Pending.Count -eq 0) { return }
    $sha = $script:queue.Pending[0]
    $script:queue.Pending.RemoveAt(0)
    if ($sha -ne $script:queue.Head) {
        Invoke-Attempt $sha $script:queue.Head 'RELEASE_STALE'
        return
    }
    $expected = if ($NativeExit) { 'Web Deploy failed with exit code 1' } else { '' }
    Invoke-Attempt $sha $script:queue.Head $expected -NativeExit $NativeExit
    $script:queue.Active = $sha
}
function Complete-Active([bool] $Success = $true) {
    Check ([bool]$script:queue.Active) 'completion requires an admitted deployment'
    if ($Success) { $script:queue.Production = $script:queue.Active }
    else { $script:queue.Production = 'partial-or-offline' }
    $script:queue.Active = ''
}

$names = @('GITHUB_ACTIONS','GITHUB_EVENT_NAME','GITHUB_REF','GITHUB_REPOSITORY','GITHUB_SHA',
    'RELEASE_GITHUB_TOKEN','SMARTERASP_WEBDEPLOY_PASSWORD')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    Invoke-Attempt $a $a
    Invoke-Attempt $a $a.ToUpperInvariant()
    Invoke-Attempt $a $b 'RELEASE_STALE'
    foreach ($invalid in @(
        @{ RELEASE_GITHUB_TOKEN = '' }, @{ GITHUB_ACTIONS = 'false' }, @{ GITHUB_EVENT_NAME = 'pull_request' },
        @{ GITHUB_REF = 'refs/heads/dev' }, @{ GITHUB_REPOSITORY = 'other/VocabularyApp' },
        @{ GITHUB_SHA = '' }, @{ GITHUB_SHA = 'abc' }, @{ GITHUB_SHA = ('g' * 40) },
        @{ GITHUB_SHA = ($a + "`n") }
    )) {
        $count = $script:requests
        $category = if ($invalid.ContainsKey('RELEASE_GITHUB_TOKEN')) { 'RELEASE_GUARD_UNAVAILABLE' } else { 'RELEASE_GUARD_INVALID_CONTEXT' }
        Invoke-Attempt $a $a $category -Overrides $invalid
        Check ($script:requests -eq $count) 'invalid context/token prevents request'
    }
    foreach ($status in @(201, 204, 301, 302, 307, 308, 403, 404, 429, 500)) {
        Invoke-Attempt $a $a 'RELEASE_GUARD_UNAVAILABLE' -Response ([pscustomobject]@{ Status = $status; Body = $tokenMarker })
    }
    foreach ($bodyText in @('not-json', 'null', '[]', '{}', '{"ref":"refs/heads/other"}',
        ('{"ref":"refs/heads/master","object":{"type":"tag","sha":"' + $a + '"}}'),
        '{"ref":"refs/heads/master","object":{"type":"commit","sha":"bad"}}',
        '{"ref":"refs/heads/master","object":{"type":"commit","sha":123}}',
        ('x' * 65537), $tokenMarker)) {
        Invoke-Attempt $a $a 'RELEASE_GUARD_UNAVAILABLE' -Response ([pscustomobject]@{ Status = 200; Body = $bodyText })
    }
    Invoke-Attempt $a $a 'RELEASE_GUARD_UNAVAILABLE' -Response ([pscustomobject]@{})
    foreach ($reason in @('timeout', 'network failure')) {
        Invoke-Attempt $a $a 'RELEASE_GUARD_UNAVAILABLE' -Failure ($reason + ' ' + $tokenMarker)
    }

    # Deterministic scheduling model; does not claim to run GitHub's scheduler.
    # Each admission uses the REAL extracted deployment boundary and real guard.
    # A/B: late building or queued A encounters new head B, including after B deploys.
    Invoke-Attempt $a $b 'RELEASE_STALE'
    Invoke-Attempt $b $b
    Invoke-Attempt $a $b 'RELEASE_STALE'

    # C: head changes just after the read. A is admitted and finishes; B follows.
    $script:advanceAfterRead = $b
    Invoke-Attempt $a $a
    Check ($script:observedHead -eq $b) 'head advanced after admission without cancelling A'
    $script:advanceAfterRead = ''
    Invoke-Attempt $b $b

    # D: B fails CI and is never admitted; A remains the prior successful release.
    Invoke-Attempt $a $a
    $count = $script:starts
    $bCiPassed = $false
    if ($bCiPassed) { Invoke-Attempt $b $b }
    Check ($script:starts -eq $count) 'failed CI has no deployment admission'
    Invoke-Attempt $a $b 'RELEASE_STALE' # no fallback promotion

    # E: A native failure releases the job; B attempts a normal forward release.
    Invoke-Attempt $a $a 'Web Deploy failed with exit code 1' -NativeExit 1
    Invoke-Attempt $b $b

    # F: retained queue C,A,B (older arrivals do not displace C); all orders safe.
    foreach ($order in @(@($a,$b,$c), @($c,$a,$b), @($b,$c,$a))) {
        foreach ($sha in $order) {
            $expected = if ($sha -eq $c) { '' } else { 'RELEASE_STALE' }
            Invoke-Attempt $sha $c $expected
        }
    }
    Invoke-Attempt $a $c 'RELEASE_STALE' # old guarded rerun
    Invoke-Attempt $c $c
    Invoke-Attempt $c $c # same SHA duplicate is serial, not stale

    # A waits/builds while B becomes head: no cached admission.
    Reset-Queue $a
    Enqueue-Candidate $a
    $script:queue.Head = $b
    Enqueue-Candidate $b
    Admit-Next
    Check (-not $script:queue.Active) 'queued A rejected after B becomes head'
    Admit-Next
    Complete-Active
    Check ($script:queue.Production -eq $b) 'B remains after slow A is rejected'

    # A active; B waits without canceling A. Failed C CI must not cause fallback.
    Reset-Queue $a
    Enqueue-Candidate $a
    Admit-Next
    $script:queue.Head = $b
    Enqueue-Candidate $b
    $count = $script:starts
    Admit-Next
    Check ($script:starts -eq $count -and $script:queue.Active -eq $a -and $script:queue.Pending.Count -eq 1) 'active A retained and B waits'
    Complete-Active
    Admit-Next
    Complete-Active
    $script:queue.Head = $c
    Enqueue-Candidate $c $false
    Admit-Next
    Check ($script:queue.Production -eq $b -and -not $script:queue.Active) 'newer failed CI preserves prior successful release'

    Reset-Queue $a
    Enqueue-Candidate $a
    Admit-Next -NativeExit 1
    $script:queue.Head = $b
    Enqueue-Candidate $b
    Complete-Active $false
    Check ($script:queue.Production -eq 'partial-or-offline') 'native failure does not restore prior application'
    Admit-Next
    Complete-Active
    Check ($script:queue.Production -eq $b) 'B makes independent forward attempt after A failure'

    # C was queued before obsolete late A/B: neither displaces it.
    Reset-Queue $a
    Enqueue-Candidate $a
    Admit-Next
    $script:queue.Head = $c
    Enqueue-Candidate $c
    Enqueue-Candidate $a
    Enqueue-Candidate $b
    Check ($script:queue.Pending.Count -eq 3 -and $script:queue.Pending[0] -eq $c) 'retained queue keeps newest waiting candidate'
    Complete-Active
    Admit-Next
    Complete-Active
    Admit-Next
    Admit-Next
    Check ($script:queue.Production -eq $c -and -not $script:queue.Active) 'obsolete late arrivals cannot overwrite C'

    # Verify real transport bounds without calling it.
    $transport = $helperAst.EndBlock.Statements | Where-Object Name -eq 'Invoke-ProductionMasterRefRequest'
    Check ($transport.Extent.Text.Contains('$handler.AllowAutoRedirect = $false')) 'transport disables redirects'
    Check ($transport.Extent.Text.Contains('$client.Timeout = [TimeSpan]::FromSeconds(10)')) 'transport timeout bounded'
    Check ($transport.Extent.Text.Contains('$client.MaxResponseContentBufferSize = 64KB')) 'response buffer bounded'
    $workflow = Get-Content (Join-Path $root '.github/workflows/backend-tests.yml') -Raw
    Check ($workflow -match '(?m)^    needs: \[backend-tests, frontend-tests\]\r?$') 'both CI dependencies retained'
    Check ($workflow -match '(?m)^    needs: build\r?$') 'deployment build dependency retained'
    Check ($workflow -match 'group: vocabularyapp-production\r?\n      cancel-in-progress: false\r?\n      queue: max') 'noncancelling retained production queue'
    Check ($workflow -notmatch '(?m)^concurrency:|cancel-in-progress: true|continue-on-error:') 'no broad cancellation or failure bypass'
    Check ($workflow -match 'environment:\r?\n      name: production') 'production environment retained'
    Check ($workflow.Contains('RELEASE_GITHUB_TOKEN: ${{ github.token }}')) 'built-in token wiring'
    Check ($workflow.IndexOf('run: ./scripts/ci/tests/Test-ProductionReleaseCandidate.ps1') -lt $workflow.IndexOf('uses: actions/upload-artifact@')) 'offline checks precede upload'
    Check ($workflow.Contains('digest-mismatch: error')) 'artifact integrity retained'
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
Write-Host ("Production release safeguard offline checks passed: {0}. No API, native process or production calls." -f $script:passed)
