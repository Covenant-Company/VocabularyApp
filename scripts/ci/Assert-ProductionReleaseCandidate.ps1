# Definitions only: dot-sourcing must never perform a request or deploy anything.
function Invoke-ProductionMasterRefRequest {
    param([Net.Http.HttpRequestMessage] $Request)
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $false
    $handler.UseDefaultCredentials = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(10)
    $client.MaxResponseContentBufferSize = 64KB
    $response = $null
    try {
        # Default ResponseContentRead bounds both headers and body by Timeout.
        $response = $client.SendAsync($Request).GetAwaiter().GetResult()
        return [pscustomobject]@{
            Status = [int]$response.StatusCode
            Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        }
    } finally {
        if ($null -ne $response) { $response.Dispose() }
        $client.Dispose()
    }
}

function Assert-ProductionReleaseCandidate {
    [CmdletBinding()]
    param(
        # Offline fixtures inject read-only responses. Production supplies no override.
        [scriptblock] $Send = { param($Request) Invoke-ProductionMasterRefRequest $Request }
    )
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    $request = $null
    $token = $null
    try {
        if ($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_EVENT_NAME -cne 'push' -or
            $env:GITHUB_REF -cne 'refs/heads/master' -or
            $env:GITHUB_REPOSITORY -cne 'Covenant-Company/VocabularyApp' -or
            $env:GITHUB_SHA -cnotmatch '\A[0-9a-fA-F]{40}\z') {
            throw 'RELEASE_GUARD_INVALID_CONTEXT'
        }
        $candidate = $env:GITHUB_SHA
        $token = $env:RELEASE_GITHUB_TOKEN
        if ([string]::IsNullOrWhiteSpace($token)) { throw 'RELEASE_GUARD_UNAVAILABLE' }
        try {
            $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,
                'https://api.github.com/repos/Covenant-Company/VocabularyApp/git/ref/heads/master')
            $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
            $request.Headers.Accept.ParseAdd('application/vnd.github+json')
            $request.Headers.UserAgent.ParseAdd('VocabularyApp-ReleaseGuard/1.0')
            $request.Headers.CacheControl = [Net.Http.Headers.CacheControlHeaderValue]::new()
            $request.Headers.CacheControl.NoCache = $true
            $request.Headers.Add('X-GitHub-Api-Version', '2026-03-10')
            $response = & $Send $request
            if ($null -eq $response -or $response.Status -ne 200 -or
                $response.Body -isnot [string] -or $response.Body.Length -gt 64KB) {
                throw 'Invalid response.'
            }
            $ref = ConvertFrom-Json -InputObject $response.Body -AsHashtable -ErrorAction Stop
            if ($ref -isnot [System.Collections.IDictionary] -or
                $ref['ref'] -cne 'refs/heads/master' -or
                $ref['object'] -isnot [System.Collections.IDictionary] -or
                $ref['object']['type'] -cne 'commit' -or
                $ref['object']['sha'] -isnot [string] -or
                $ref['object']['sha'] -cnotmatch '\A[0-9a-fA-F]{40}\z') {
                throw 'Invalid ref.'
            }
            $current = $ref['object']['sha']
        } catch {
            # Never forward transport exceptions, response bodies or auth headers.
            throw 'RELEASE_GUARD_UNAVAILABLE'
        }
        if (-not [string]::Equals($candidate, $current, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'RELEASE_STALE'
        }
    } finally {
        if ($null -ne $request) {
            $request.Headers.Authorization = $null
            $request.Dispose()
        }
        $token = $null
        $env:RELEASE_GITHUB_TOKEN = $null
    }
}
