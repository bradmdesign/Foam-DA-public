param([string]$GamePath='C:\Program Files (x86)\KRU\Dark Ages',[switch]$SkipDependencies,[switch]$SkipLibrary)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
& (Join-Path $PSScriptRoot 'Restore-Resources.ps1')
function Check-Exit([string]$Step) { if($LASTEXITCODE -ne 0){throw "$Step failed ($LASTEXITCODE)."} }
if(-not $SkipDependencies){
    python -m pip install -r requirements.txt
    Check-Exit 'Python dependency installation'
}
$hostRoot=Join-Path $PSScriptRoot 'prototype\widescreen'
python "$hostRoot\build_prototype.py" --source $GamePath --width 960 --height 600
Check-Exit 'Game preparation'
python "$hostRoot\build_prototype.py" --source $GamePath --prepare-host-profiles
Check-Exit 'Display profile preparation'
python -c "import sys;sys.path.insert(0,sys.argv[1]);import build_host_theme as theme;from pathlib import Path;theme.SOURCE_DEFAULT=Path(sys.argv[2]);theme.build()" $hostRoot $GamePath
Check-Exit 'Theme preparation'
if(-not $SkipLibrary){
    Push-Location (Join-Path $PSScriptRoot 'site')
    try{
        if(-not $SkipDependencies){npm ci;Check-Exit 'JavaScript dependency installation'}
        npm run build
        Check-Exit 'Reference library build'
    }finally{Pop-Location}
}
& "$hostRoot\build-launcher.ps1"
Write-Output 'Built Foam DA. Open Foam DA.cmd starts the desktop companion.'
