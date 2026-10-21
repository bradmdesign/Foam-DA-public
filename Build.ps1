param([string]$GamePath='C:\Program Files (x86)\KRU\Dark Ages',[switch]$SkipDependencies,[switch]$SkipLibrary)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
function Check-Exit([string]$Step) { if($LASTEXITCODE -ne 0){throw "$Step failed ($LASTEXITCODE)."} }
$hostRoot=Join-Path $PSScriptRoot 'prototype\widescreen'
$taskPythonDeps=Join-Path $hostRoot 'deps\python'
if(-not $SkipDependencies){
    python -m pip install --target $taskPythonDeps -r requirements.txt
    Check-Exit 'Python dependency installation'
}
$env:PYTHONPATH=$taskPythonDeps+[IO.Path]::PathSeparator+$env:PYTHONPATH
python "$hostRoot\build_client_patches.py" --source $GamePath
Check-Exit 'Local game recipe preparation'
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
