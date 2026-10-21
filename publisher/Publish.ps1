param([ValidateSet('Init','Ads','Library','App')][string]$Component='Ads',[string]$Version,[long]$Revision=0,[string]$FeedUrl,[string]$HostExe)
$ErrorActionPreference='Stop'
if(-not $FeedUrl){$FeedUrl=(Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../prototype/widescreen/updater/channel.json') | ConvertFrom-Json).feed_url}
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$taskBuild=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $taskBuild -Force | Out-Null
$taskSigner=Join-Path $taskBuild 'Foam-Publisher.exe'
& $taskCompiler /nologo /target:exe /reference:System.Security.dll "/out:$taskSigner" (Join-Path $PSScriptRoot 'SigningKey.cs')
if($LASTEXITCODE -ne 0){throw 'Publisher signing tool compilation failed.'}
if($Component -eq 'Init'){
    & node (Join-Path $PSScriptRoot 'publish.mjs') init setup 0 $FeedUrl
}else{
    & node (Join-Path $PSScriptRoot 'publish.mjs') $Component.ToLowerInvariant() $Version $Revision $FeedUrl $HostExe
}
if($LASTEXITCODE -ne 0){throw 'Publishing preparation failed. Nothing was uploaded.'}
