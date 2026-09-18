# Strict artifact contract, independent of the source web.config being validated.
# Dot-sourcing defines functions only; no network or deployment action.
function Assert-Psh1WebConfig {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $Path)

    $expectedText = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <rewrite>
        <rules>
          <rule name="PSH1-Reject-InsecureApi" stopProcessing="true">
            <match url="^api(?:/|$)" ignoreCase="true" />
            <conditions logicalGrouping="MatchAll">
              <add input="{HTTPS}" pattern="^OFF$" ignoreCase="true" />
            </conditions>
            <action type="CustomResponse" statusCode="403" statusReason="Forbidden" statusDescription="HTTPS required" />
          </rule>
          <rule name="PSH1-Reject-InsecureMethods" stopProcessing="true">
            <match url=".*" ignoreCase="true" />
            <conditions logicalGrouping="MatchAll">
              <add input="{HTTPS}" pattern="^OFF$" ignoreCase="true" />
              <add input="{REQUEST_METHOD}" pattern="^(?:GET|HEAD)$" negate="true" />
            </conditions>
            <action type="CustomResponse" statusCode="403" statusReason="Forbidden" statusDescription="HTTPS required" />
          </rule>
          <rule name="PSH1-Redirect-To-Canonical-Https" stopProcessing="true">
            <match url=".*" ignoreCase="true" />
            <conditions logicalGrouping="MatchAll">
              <add input="{HTTPS}" pattern="^OFF$" ignoreCase="true" />
              <add input="{REQUEST_METHOD}" pattern="^(?:GET|HEAD)$" />
            </conditions>
            <action type="Redirect" url="https://myvocabularybuilder.org{UNENCODED_URL}" redirectType="Permanent" appendQueryString="false" />
          </rule>
        </rules>
      </rewrite>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" arguments=".\VocabularyApp.WebApi.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess" />
    </system.webServer>
  </location>
</configuration>
'@
    function Read-SafeXml([string] $Text) {
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $reader = [Xml.XmlReader]::Create([IO.StringReader]::new($Text), $settings)
        try {
            $document = [Xml.XmlDocument]::new()
            $document.XmlResolver = $null
            $document.Load($reader)
            return ,$document
        } finally { $reader.Dispose() }
    }
    function Compare-Element([Xml.XmlElement] $Actual, [Xml.XmlElement] $Expected) {
        if ($Actual.Name -cne $Expected.Name -or $Actual.Attributes.Count -ne $Expected.Attributes.Count) {
            throw 'PSH-1 web.config element or attribute contract mismatch.'
        }
        foreach ($attribute in $Expected.Attributes) {
            if (-not $Actual.HasAttribute($attribute.Name) -or
                $Actual.GetAttribute($attribute.Name) -cne $attribute.Value) {
                throw 'PSH-1 web.config attribute contract mismatch.'
            }
        }
        $actualChildren = @($Actual.ChildNodes | Where-Object NodeType -EQ Element)
        $expectedChildren = @($Expected.ChildNodes | Where-Object NodeType -EQ Element)
        if ($actualChildren.Count -ne $expectedChildren.Count) {
            throw 'PSH-1 web.config child element contract mismatch.'
        }
        foreach ($node in $Actual.ChildNodes) {
            if ($node.NodeType -notin @('Element', 'Comment', 'Whitespace', 'SignificantWhitespace') -and
                -not [string]::IsNullOrWhiteSpace($node.Value)) {
                throw 'PSH-1 web.config contains unexpected content.'
            }
        }
        for ($i = 0; $i -lt $expectedChildren.Count; $i++) {
            Compare-Element $actualChildren[$i] $expectedChildren[$i]
        }
    }
    try {
        $actual = Read-SafeXml ([IO.File]::ReadAllText($Path))
        $expected = Read-SafeXml $expectedText
        Compare-Element $actual.DocumentElement $expected.DocumentElement
    } catch {
        # XML, paths and attribute values may contain unreviewed sensitive content.
        throw 'PSH-1 published web.config validation failed; expected IIS policy or ANCM configuration is missing or altered.'
    }
}
