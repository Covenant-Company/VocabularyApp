# Release A only. Dot-source for offline tests; execute only for approved live acceptance.
# No credentials, HSTS requirement, automatic redirects, or deployment actions.
function Assert-Psh1CanonicalUri {
    param([string] $Text, [switch] $AllowHttp)
    $uri = $null
    if (-not [Uri]::TryCreate($Text, [UriKind]::Absolute, [ref] $uri) -or
        $uri.DnsSafeHost -cne 'myvocabularybuilder.org' -or $uri.UserInfo -or $uri.Fragment -or
        -not $uri.IsDefaultPort -or
        ($uri.Scheme -ne 'https' -and -not ($AllowHttp -and $uri.Scheme -eq 'http'))) {
        throw 'PSH1_UNSAFE_TARGET'
    }
    return $uri
}

function Get-Psh1Header {
    param($Response, [string] $Name)
    if ($Response.Headers.ContainsKey($Name)) { return @($Response.Headers[$Name]) }
    return @()
}

function Assert-Psh1Page {
    param($Response)
    $type = @(Get-Psh1Header $Response 'Content-Type') -join ','
    if ($Response.Status -ne 200 -or $type -notmatch '^text/html(?:;|$)' -or
        $Response.Body -notmatch '<app-root(?:\s|>)' -or
        @(Get-Psh1Header $Response 'Location').Count -ne 0) {
        throw 'PSH1_PAGE_CONTRACT'
    }
}

function Invoke-Psh1Probe {
    param([scriptblock] $Send, [string] $Method, [string] $Url, [hashtable] $Headers = @{})
    $null = Assert-Psh1CanonicalUri $Url -AllowHttp
    try { $response = & $Send $Method $Url $Headers }
    catch { throw 'PSH1_NETWORK' }
    if ($response.Failure -eq 'Tls') { throw 'PSH1_TLS' }
    if ($response.Failure -eq 'TooLarge') { throw 'PSH1_RESPONSE_SIZE' }
    if ($response.Failure) { throw 'PSH1_NETWORK' }
    return $response
}

function Assert-Psh1Redirect {
    param($Response, [string] $Expected)
    $location = @(Get-Psh1Header $Response 'Location')
    if ($Response.Status -ne 301 -or $location.Count -ne 1 -or $location[0] -cne $Expected) {
        throw 'PSH1_REDIRECT_CONTRACT'
    }
    $null = Assert-Psh1CanonicalUri $location[0]
}

function Test-Psh1RedirectChain {
    param([scriptblock] $Send, [string] $Url)
    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($hop = 0; $hop -le 5; $hop++) {
        if (-not $visited.Add($Url)) { throw 'PSH1_REDIRECT_LOOP' }
        $response = Invoke-Psh1Probe $Send 'GET' $Url
        if ($response.Status -notin @(301, 302, 303, 307, 308)) {
            $null = Assert-Psh1CanonicalUri $Url
            Assert-Psh1Page $response
            return
        }
        if ($hop -eq 5) { throw 'PSH1_REDIRECT_LIMIT' }
        $location = @(Get-Psh1Header $response 'Location')
        if ($location.Count -ne 1) { throw 'PSH1_REDIRECT_CONTRACT' }
        $null = Assert-Psh1CanonicalUri $location[0]
        $Url = $location[0]
    }
}

function Invoke-Psh1ReleaseAAcceptance {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][scriptblock] $Send,
        [scriptblock] $Delay = { param($Seconds) Start-Sleep -Seconds $Seconds }
    )
    $check = 'HTTPS readiness'
    try {
        $page = $null
        for ($attempt = 1; $attempt -le 6; $attempt++) {
            $retry = $false
            try {
                $page = Invoke-Psh1Probe $Send 'GET' 'https://myvocabularybuilder.org/login'
                $retry = $page.Status -in @(502, 503)
            } catch {
                if ($_.Exception.Message -ne 'PSH1_NETWORK') { throw }
                $retry = $true
            }
            if (-not $retry) { break }
            if ($attempt -eq 6) { throw 'PSH1_READINESS_TIMEOUT' }
            Write-Host ("Release A readiness retry {0}/6." -f $attempt)
            & $Delay 5
        }
        Assert-Psh1Page $page
        $paths = @(
            '/login',
            '/dictionary?term=council',
            '/login?psh1=one&psh1=two&encoded=a%2Fb%20c%26d%25',
            '/login/psh1%20path?value=a%25b'
        )
        $check = 'HTTP navigation and original target'
        foreach ($path in $paths) {
            $response = Invoke-Psh1Probe $Send 'GET' ('http://myvocabularybuilder.org' + $path)
            Assert-Psh1Redirect $response ('https://myvocabularybuilder.org' + $path)
        }
        $check = 'Bounded redirect traversal'
        Test-Psh1RedirectChain $Send 'http://myvocabularybuilder.org/login'
        Test-Psh1RedirectChain $Send ('http://myvocabularybuilder.org' + $paths[2])

        $check = 'Insecure API and method rejection'
        foreach ($probe in @(
            @('GET', 'http://myvocabularybuilder.org/api/users/profile'),
            @('POST', 'http://myvocabularybuilder.org/api/users/login'),
            @('POST', 'http://myvocabularybuilder.org/login')
        )) {
            # Empty POST body: no credentials and no valid state-changing payload.
            $response = Invoke-Psh1Probe $Send $probe[0] $probe[1]
            if ($response.Status -ne 403 -or @(Get-Psh1Header $response 'Location').Count -ne 0) {
                throw 'PSH1_HTTP_REJECTION'
            }
        }

        $check = 'HTTPS API authentication boundary and production CORS'
        foreach ($origin in @('', 'http://localhost:4200', 'https://localhost:4200',
                'http://ripcody-001-site1.anytempurl.com')) {
            $headers = @{}
            if ($origin) { $headers.Origin = $origin }
            $response = Invoke-Psh1Probe $Send 'GET' 'https://myvocabularybuilder.org/api/users/profile' $headers
            if ($response.Status -ne 401 -or
                (@(Get-Psh1Header $response 'WWW-Authenticate') -join ',') -notmatch '(?i)(?:^|,)\s*Bearer(?:\s|,|$)' -or
                @(Get-Psh1Header $response 'Location').Count -ne 0 -or
                $response.Body -match '(?i)<(?:html|app-root)\b' -or
                @(Get-Psh1Header $response 'Access-Control-Allow-Origin').Count -ne 0 -or
                @(Get-Psh1Header $response 'Access-Control-Allow-Credentials').Count -ne 0) {
                throw 'PSH1_API_CONTRACT'
            }
        }

        $check = 'Same-origin JavaScript asset'
        # Only accept a simple local emitted JS filename; never request arbitrary HTML URLs.
        $asset = [regex]::Match($page.Body, '<script\b[^>]*\bsrc=["''](?<path>/?[A-Za-z0-9._-]+\.js)["'']', 'IgnoreCase')
        if (-not $asset.Success) { throw 'PSH1_ASSET_REFERENCE' }
        $path = '/' + $asset.Groups['path'].Value.TrimStart('/')
        $response = Invoke-Psh1Probe $Send 'GET' ('https://myvocabularybuilder.org' + $path)
        if ($response.Status -ne 200 -or
            (@(Get-Psh1Header $response 'Content-Type') -join ',') -notmatch '^(?:text|application)/(?:javascript|x-javascript)(?:;|$)' -or
            [string]::IsNullOrWhiteSpace($response.Body) -or
            @(Get-Psh1Header $response 'Location').Count -ne 0) {
            throw 'PSH1_ASSET_CONTRACT'
        }
        $response = Invoke-Psh1Probe $Send 'GET' ('http://myvocabularybuilder.org' + $path)
        Assert-Psh1Redirect $response ('https://myvocabularybuilder.org' + $path)
        Write-Host 'Release A transport acceptance passed. API check is an anonymous 401 challenge; authenticated manual smoke is still required. HSTS is deferred.'
    } catch {
        # Only constant check labels and allowlisted categories reach logs.
        $category = 'PSH1_UNEXPECTED'
        if ($_.Exception.Message -cmatch '^PSH1_(NETWORK|TLS|RESPONSE_SIZE|PAGE_CONTRACT|UNSAFE_TARGET|REDIRECT_CONTRACT|REDIRECT_LOOP|REDIRECT_LIMIT|READINESS_TIMEOUT|HTTP_REJECTION|API_CONTRACT|ASSET_REFERENCE|ASSET_CONTRACT)$') {
            $category = $_.Exception.Message
        }
        throw ("Deployment completed but production acceptance failed. Check: {0}; category: {1}. Manual recovery required; no automatic rollback." -f $check, $category)
    }
}

function Invoke-Psh1HttpRequest {
    param([Net.Http.HttpClient] $Client, [string] $Method, [string] $Url, [hashtable] $Headers)
    $request = $null
    $response = $null
    try {
        $null = Assert-Psh1CanonicalUri $Url -AllowHttp
        if ($Method -notin @('GET', 'POST')) { throw 'Unsupported probe method.' }
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), $Url)
        $request.Headers.CacheControl = [Net.Http.Headers.CacheControlHeaderValue]::new()
        $request.Headers.CacheControl.NoCache = $true
        if ($Headers.ContainsKey('Origin')) {
            $null = $request.Headers.TryAddWithoutValidation('Origin', $Headers.Origin)
        }
        if ($Method -eq 'POST') { $request.Content = [Net.Http.ByteArrayContent]::new([byte[]]@()) }
        # ResponseContentRead + MaxResponseContentBufferSize bounds buffering; Timeout covers content.
        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        $resultHeaders = @{}
        foreach ($header in $response.Headers) { $resultHeaders[$header.Key] = @($header.Value) }
        foreach ($header in $response.Content.Headers) { $resultHeaders[$header.Key] = @($header.Value) }
        return [pscustomobject]@{
            Status = [int]$response.StatusCode; Headers = $resultHeaders
            Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult(); Failure = ''
        }
    } catch {
        $failure = 'Network'
        $exception = $_.Exception
        while ($null -ne $exception) {
            if ($exception -is [Security.Authentication.AuthenticationException] -or
                ($exception.PSObject.Properties['HttpRequestError'] -and
                    [string]$exception.HttpRequestError -eq 'SecureConnectionError')) { $failure = 'Tls' }
            if ($exception -is [Net.Http.HttpRequestException] -and
                $exception.Message -match '(?i)buffer|maximum.*size') { $failure = 'TooLarge' }
            $exception = $exception.InnerException
        }
        return [pscustomobject]@{ Status = 0; Headers = @{}; Body = ''; Failure = $failure }
    } finally {
        if ($null -ne $response) { $response.Dispose() }
        if ($null -ne $request) { $request.Dispose() }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $false
    $handler.UseDefaultCredentials = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(10)
    $client.MaxResponseContentBufferSize = 2MB
    try {
        Invoke-Psh1ReleaseAAcceptance -Send {
            param($Method, $Url, $Headers)
            Invoke-Psh1HttpRequest $client $Method $Url $Headers
        }
    } finally { $client.Dispose() }
}
