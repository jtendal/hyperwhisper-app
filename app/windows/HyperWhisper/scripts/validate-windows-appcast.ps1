param([string]$Path = "$PSScriptRoot/../../updates/appcast-windows.xml")
$ErrorActionPreference = "Stop"
[xml]$feed = Get-Content -LiteralPath $Path -Raw
$ns = [System.Xml.XmlNamespaceManager]::new($feed.NameTable)
$ns.AddNamespace('s', 'http://www.andymatuschak.org/xml-namespaces/sparkle')
$items = @($feed.SelectNodes('/rss/channel/item'))
if ($items.Count -eq 0) { throw 'Update feed has no items' }
$versions = @{}
$previous = $null
foreach ($item in $items) {
    $version = $item.SelectSingleNode('s:version', $ns).InnerText
    $short = $item.SelectSingleNode('s:shortVersionString', $ns).InnerText
    $os = $item.SelectSingleNode('s:os', $ns).InnerText
    if ($version -notmatch '^\d+\.\d+\.\d+$' -or $version -ne $short) { throw "Invalid version: $version" }
    if ($os -notin @('windows-x64', 'windows-arm64')) { throw "Unexpected platform: $os" }
    $date = [DateTimeOffset]::ParseExact($item.pubDate, 'ddd, dd MMM yyyy HH:mm:ss zzz', [cultureinfo]::InvariantCulture)
    if ($date.Offset -ne [TimeSpan]::Zero) { throw 'pubDate must use UTC' }
    $description = $item.SelectSingleNode('description').InnerText
    if ($description -notmatch '(?is)<ul[\s>].*</ul>' -or $description -notmatch '(?is)<li[\s>].*?</li>') { throw "Missing release notes: $version" }
    $url = [uri]$item.enclosure.url
    $arch = $os.Replace('windows-', '')
    if ($url.Scheme -ne 'https' -or $url.Host -ne 'builds.hyperwhisper.com' -or $url.AbsolutePath -ne "/HyperWhisper-$short-$arch-Setup.exe") { throw "Invalid enclosure: $url" }
    if ([long]$item.enclosure.length -le 0) { throw "Invalid enclosure size: $version" }
    $signature = $item.enclosure.GetAttribute('edSignature', $ns.LookupNamespace('s'))
    if ($signature.Length -lt 10) { throw "Missing signature: $version" }
    if (-not $versions.ContainsKey($version)) {
        if ($null -ne $previous -and [version]$version -ge [version]$previous) { throw 'Versions must be newest first' }
        $versions[$version] = @()
        $previous = $version
    }
    if ($os -in $versions[$version]) { throw "Duplicate $os item for $version" }
    $versions[$version] += $os
}
foreach ($version in $versions.Keys) {
    if ($versions[$version].Count -ne 2) { throw "Missing architecture for $version" }
}
Write-Host "Validated $($items.Count) Windows update items across $($versions.Count) versions."
