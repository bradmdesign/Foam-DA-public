$ErrorActionPreference='Stop'
$resources=Join-Path $PSScriptRoot 'resources'
if(-not(Test-Path -LiteralPath $resources)){return}
$archives=@(Get-ChildItem -LiteralPath $resources -Filter 'reference-*.zip' -File | Sort-Object Name)
foreach($archive in $archives){
    Write-Output "Restoring $($archive.Name)..."
    Expand-Archive -LiteralPath $archive.FullName -DestinationPath $PSScriptRoot -Force
}
Write-Output 'Offline reference resources restored.'
