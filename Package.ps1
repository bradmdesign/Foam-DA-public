param([switch]$HostUpdate)
$ErrorActionPreference='Stop'
$taskArgs=@((Join-Path $PSScriptRoot 'package_lite.py'))
if($HostUpdate){$taskArgs+='--host-update'}
python @taskArgs
if($LASTEXITCODE -ne 0){throw 'Lite packaging failed.'}
