[CmdletBinding()]
param([string] $PublishedWebConfig)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../Assert-Psh1WebConfig.ps1')
. (Join-Path $PSScriptRoot '../Test-ProductionHttps.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$source = Join-Path $root 'VocabularyApp.WebApi/web.config'
$script:passed = 0
function Check([bool] $Condition, [string] $Label) {
    if (-not $Condition) { throw ("Offline test failed: " + $Label) }
    $script:passed++
}
function Expect-Rejection([scriptblock] $Action, [string] $Label, [string] $Category = '') {
    $rejected = $false
    try { & $Action | Out-Null } catch {
        $rejected = $true
        Check (-not $_.Exception.Message.Contains('SENSITIVE_FIXTURE_MARKER')) 'exception redaction'
        if ($Category) { Check ($_.Exception.Message.Contains($Category)) ($Label + ' category') }
    }
    Check $rejected $Label
}
Assert-Psh1WebConfig $source
Check $true 'source accepted'
$mutations = @(
    { param($x) $null = $x.SelectSingleNode('//rewrite').ParentNode.RemoveChild($x.SelectSingleNode('//rewrite')) },
    { param($x) $null = $x.SelectSingleNode('//rule[3]').ParentNode.RemoveChild($x.SelectSingleNode('//rule[3]')) },
    { param($x) $null = $x.SelectSingleNode('//rule[1]').ParentNode.RemoveChild($x.SelectSingleNode('//rule[1]')) },
    { param($x) $null = $x.SelectSingleNode('//rule[2]').ParentNode.RemoveChild($x.SelectSingleNode('//rule[2]')) },
    { param($x) $x.SelectSingleNode('//rule').SetAttribute('enabled', 'false') },
    { param($x) $r = $x.SelectSingleNode('//rules'); $null = $r.AppendChild($r.FirstChild) },
    { param($x) $n = $x.SelectSingleNode('//conditions/add'); $null = $n.ParentNode.RemoveChild($n) },
    { param($x) $x.SelectSingleNode('//action[@type="Redirect"]').SetAttribute('url', 'https://wrong.example/login') },
    { param($x) $x.SelectSingleNode('//action[@type="Redirect"]').SetAttribute('appendQueryString', 'true') },
    { param($x) $x.SelectSingleNode('//action[@type="Redirect"]').SetAttribute('redirectType', 'Found') },
    { param($x) $x.SelectSingleNode('//action[@type="CustomResponse"]').SetAttribute('statusCode', '200') },
    { param($x) $r = $x.SelectSingleNode('//rules'); $null = $r.AppendChild($r.LastChild.CloneNode($true)) },
    { param($x) $x.SelectSingleNode('//aspNetCore').SetAttribute('processPath', '.\VocabularyApp.WebApi.exe') },
    { param($x) $x.SelectSingleNode('//aspNetCore').SetAttribute('arguments', '.\Other.dll') },
    { param($x) $x.SelectSingleNode('//aspNetCore').SetAttribute('hostingModel', 'outofprocess') },
    { param($x) $x.SelectSingleNode('//handlers/add').SetAttribute('modules', 'OtherModule') },
    { param($x) $x.SelectSingleNode('//rule').SetAttribute('stopProcessing', 'false') },
    { param($x) $x.SelectSingleNode('//match').SetAttribute('url', '^somethingelse') },
    { param($x) $x.SelectSingleNode('//conditions/add[@negate]').SetAttribute('negate', 'false') },
    { param($x) $x.SelectSingleNode('//aspNetCore').SetAttribute('password', 'SENSITIVE_FIXTURE_MARKER') }
)
$temp = Join-Path ([IO.Path]::GetTempPath()) ('psh1-fixture-' + [guid]::NewGuid().ToString('N') + '.config')
try {
    foreach ($mutation in $mutations) {
        [xml] $x = [IO.File]::ReadAllText($source)
        & $mutation $x
        $x.Save($temp)
        Expect-Rejection { Assert-Psh1WebConfig $temp } 'altered policy rejected'
    }
    if ($PublishedWebConfig) {
        Assert-Psh1WebConfig $PublishedWebConfig
        Check $true 'actual published output accepted'
        [xml] $x = [IO.File]::ReadAllText($PublishedWebConfig)
        $node = $x.SelectSingleNode('//rewrite')
        $null = $node.ParentNode.RemoveChild($node)
        $x.Save($temp)
        Expect-Rejection { Assert-Psh1WebConfig $temp } 'actual output without rewrite rejected'
        Write-Host 'Actual published web.config accepted; rewrite-removal mutation rejected.'
    }
} finally {
    # Delete only this uniquely named fixture, never the artifact or a directory.
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp }
}

function New-Response([int] $Status, [hashtable] $Headers = @{}, [string] $Body = '', [string] $Failure = '') {
    return [pscustomobject]@{ Status = $Status; Headers = $Headers; Body = $Body; Failure = $Failure }
}
$script:state = @{ Mode = ''; Requests = 0; Sleeps = 0 }
$send = {
    param($Method, $Url, $Headers)
    $script:state.Requests++
    $mode = $script:state.Mode
    if ($mode -eq 'Tls') { return New-Response 0 @{} '' 'Tls' }
    if ($mode -eq 'Network') { throw 'SENSITIVE_FIXTURE_MARKER' }
    if ($mode -eq 'TooLarge') { return New-Response 0 @{} '' 'TooLarge' }
    if ($mode -eq 'Startup' -and $script:state.Requests -lt 3) { return New-Response 503 }
    if ($mode -eq 'Unavailable') { return New-Response 503 }
    if ($Url.StartsWith('http://')) {
        if ($Method -eq 'POST' -or $Url.Contains('/api/')) {
            if ($mode -eq 'ApiHttpRedirect') {
                return New-Response 301 @{ Location = $Url.Replace('http://', 'https://') }
            }
            return New-Response 403
        }
        if ($mode -eq 'Http200') { return New-Response 200 @{} 'SENSITIVE_FIXTURE_MARKER' }
        $location = $Url.Replace('http://', 'https://')
        if ($mode -eq 'WrongHost') { $location = 'https://wrong.example/?secret=SENSITIVE_FIXTURE_MARKER' }
        if ($mode -eq 'QueryLoss' -and $Url.Contains('?')) { $location = $location.Split('?')[0] }
        if ($mode -eq 'EncodingLoss') { $location = $location.Replace('%20', '%2520') }
        return New-Response 301 @{ Location = $location }
    }
    if ($Url.Contains('/api/')) {
        if ($mode -eq 'ApiHtml200') { return New-Response 200 @{ 'Content-Type' = 'text/html' } '<app-root></app-root>' }
        $resultHeaders = @{ 'WWW-Authenticate' = 'Bearer'; 'Strict-Transport-Security' = 'max-age=300' }
        if ($mode -eq 'Cors') { $resultHeaders['Access-Control-Allow-Origin'] = 'http://localhost:4200' }
        return New-Response 401 $resultHeaders
    }
    if ($Url.EndsWith('.js')) { return New-Response 200 @{ 'Content-Type' = 'text/javascript'; 'Strict-Transport-Security' = 'max-age=300' } 'const app = true;' }
    if ($mode -eq 'Cached') { return New-Response 304 }
    $body = '<html><app-root></app-root><script src="main-fixture.js"></script></html>'
    if ($mode -eq 'ForeignAsset') { $body = '<app-root></app-root><script src="https://wrong.example/a.js"></script>' }
    return New-Response 200 @{ 'Content-Type' = 'text/html; charset=utf-8'; 'Strict-Transport-Security' = 'max-age=300' } $body
}
$delay = { param($Seconds) Check ($Seconds -eq 5) 'readiness delay'; $script:state.Sleeps++ }
Invoke-Psh1ReleaseBAcceptance -Send $send -Delay $delay
Check ($script:state.Requests -lt 30) 'success request bound'
foreach ($case in @(
    @('Tls', 'PSH1_TLS'), @('Network', 'PSH1_READINESS_TIMEOUT'), @('TooLarge', 'PSH1_RESPONSE_SIZE'),
    @('Unavailable', 'PSH1_READINESS_TIMEOUT'), @('Http200', 'PSH1_REDIRECT_CONTRACT'),
    @('WrongHost', 'PSH1_REDIRECT_CONTRACT'), @('QueryLoss', 'PSH1_REDIRECT_CONTRACT'),
    @('EncodingLoss', 'PSH1_REDIRECT_CONTRACT'), @('ApiHttpRedirect', 'PSH1_HTTP_REJECTION'),
    @('ApiHtml200', 'PSH1_API_CONTRACT'), @('Cors', 'PSH1_API_CONTRACT'),
    @('Cached', 'PSH1_PAGE_CONTRACT'), @('ForeignAsset', 'PSH1_ASSET_REFERENCE')
)) {
    $script:state = @{ Mode = $case[0]; Requests = 0; Sleeps = 0 }
    Expect-Rejection { Invoke-Psh1ReleaseBAcceptance $send $delay } $case[0] $case[1]
    if ($case[0] -in @('Network', 'Unavailable')) {
        Check ($script:state.Requests -eq 6 -and $script:state.Sleeps -eq 5) 'retry bound'
    }
    if ($case[0] -eq 'Tls') { Check ($script:state.Requests -eq 1) 'TLS fails without retries' }
}
$script:state = @{ Mode = 'Startup'; Requests = 0; Sleeps = 0 }
Invoke-Psh1ReleaseBAcceptance $send $delay
Check ($script:state.Sleeps -eq 2) 'startup recovers after bounded retry'

$loop = { param($Method, $Url, $Headers) New-Response 301 @{ Location = 'https://myvocabularybuilder.org/login' } }
Expect-Rejection { Test-Psh1RedirectChain $loop 'http://myvocabularybuilder.org/login' } 'loop' 'PSH1_REDIRECT_LOOP'
$script:chainRequests = 0
$longChain = {
    param($Method, $Url, $Headers)
    $script:chainRequests++
    New-Response 301 @{ Location = ('https://myvocabularybuilder.org/login?hop=' + $script:chainRequests) }
}
Expect-Rejection { Test-Psh1RedirectChain $longChain 'http://myvocabularybuilder.org/login' } 'long chain' 'PSH1_REDIRECT_LIMIT'
Check ($script:chainRequests -eq 6) 'chain request bound'
foreach ($bad in @('http://myvocabularybuilder.org/login', 'https://wrong.example/login',
    'https://user:pass@myvocabularybuilder.org/login', 'https://myvocabularybuilder.org:444/login',
    'https://myvocabularybuilder.org/login#fragment')) {
    Expect-Rejection { Assert-Psh1CanonicalUri $bad } 'unsafe redirect target'
}

foreach ($value in @('max-age=300', " `tMAX-AGE `t= 300`t ")) {
    $headers = [Collections.Hashtable]::new([StringComparer]::Ordinal)
    $headers['sTrIcT-tRaNsPoRt-SeCuRiTy'] = $value
    Assert-Psh1Hsts (New-Response 200 $headers)
    Check $true 'valid HSTS with case-insensitive names and HTTP whitespace'
}
$baselineSend = $send
foreach ($case in @(
    @{ Values = $null; Category = 'PSH1_HSTS_MISSING' },
    @{ Values = 'max-age=301'; Category = 'PSH1_HSTS_POLICY' },
    @{ Values = 'max-age=300; includeSubDomains'; Category = 'PSH1_HSTS_POLICY' },
    @{ Values = 'max-age=300; PRELOAD'; Category = 'PSH1_HSTS_POLICY' },
    @{ Values = @('max-age=300', 'max-age=300'); Category = 'PSH1_HSTS_POLICY' },
    @{ Values = 'max-age=300, max-age=300'; Category = 'PSH1_HSTS_POLICY' },
    @{ Values = 'max-age=300; max-age=300'; Category = 'PSH1_HSTS_POLICY' },
    @{ Values = 'max-age=300; unknown'; Category = 'PSH1_HSTS_POLICY' },
    @{ Values = ''; Category = 'PSH1_HSTS_POLICY' },
    @{ Values = "max-age=300`n"; Category = 'PSH1_HSTS_POLICY' }
)) {
    # Exercise the complete gate independently for HTML, API, JS, and the final
    # HTTPS response of a chain. HTTP redirects deliberately carry no HSTS.
    foreach ($target in @('Page', 'Api', 'Asset', 'Chain')) {
        $script:state = @{ Mode = ''; Requests = 0; Sleeps = 0 }
        $script:pageRequests = 0
        $hstsSend = {
            param($Method, $Url, $Headers)
            $response = & $baselineSend $Method $Url $Headers
            if ($Url -eq 'https://myvocabularybuilder.org/login') { $script:pageRequests++ }
            $replace = $Url.StartsWith('https://') -and (
                ($target -eq 'Page' -and $script:state.Requests -eq 1) -or
                ($target -eq 'Api' -and $Url.Contains('/api/')) -or
                ($target -eq 'Asset' -and $Url.EndsWith('.js')) -or
                ($target -eq 'Chain' -and $script:pageRequests -eq 2))
            if ($replace) {
                $response.Headers.Remove('Strict-Transport-Security')
                if ($null -ne $case.Values) { $response.Headers['Strict-Transport-Security'] = $case.Values }
            }
            return $response
        }
        Expect-Rejection { Invoke-Psh1ReleaseBAcceptance $hstsSend $delay } "$target HSTS rejected" $case.Category
        Check ($script:state.Sleeps -eq 0) 'HSTS mismatch is not retried'
    }
}
$duplicateNames = [Collections.Hashtable]::new([StringComparer]::Ordinal)
$duplicateNames['Strict-Transport-Security'] = 'max-age=300'
$duplicateNames['strict-transport-security'] = 'max-age=300'
Expect-Rejection { Assert-Psh1Hsts (New-Response 200 $duplicateNames) } 'duplicate header names' 'PSH1_HSTS_POLICY'
Write-Host ("PSH-1 offline checks passed: {0}; failed: 0; no production requests." -f $script:passed)
