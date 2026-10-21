param([string]$OutputName='Foam-DA.exe',[switch]$Staged,[string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
# Nested Windows PowerShell launches can inherit another PowerShell edition's
# module path. Load the host's own utility module when its hash helper is absent.
if(-not (Get-Command Get-FileHash -ErrorAction SilentlyContinue)) {
    Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -Force
}
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName -or -not $OutputName.EndsWith('.exe')) { throw 'OutputName must be an executable filename inside the prototype folder.' }
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$taskRoot = $PSScriptRoot
$taskOutputFolder = if ($Staged) { Join-Path $taskRoot 'build\staged' } else { $taskRoot }
if($OutputDirectory) {
    $taskOutputFolder=[IO.Path]::GetFullPath($OutputDirectory)
    $taskStagedRoot=[IO.Path]::GetFullPath((Join-Path $taskRoot 'build\staged'))
    if(-not $taskOutputFolder.StartsWith($taskStagedRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'A candidate output directory must be inside build/staged.'}
}
New-Item -ItemType Directory -Path $taskOutputFolder -Force | Out-Null
$taskWebView = Join-Path $taskRoot 'deps\webview2-1.0.4258.31'
if (-not (Test-Path -LiteralPath "$taskWebView\lib\net462\Microsoft.Web.WebView2.Core.dll")) {
    New-Item -ItemType Directory -Path "$taskRoot\deps" -Force | Out-Null
    $taskPackage = Join-Path $taskRoot 'deps\webview2-1.0.4258.31.zip'
    if (-not (Test-Path -LiteralPath $taskPackage)) {
        Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.4258.31/microsoft.web.webview2.1.0.4258.31.nupkg' -OutFile $taskPackage
    }
    if ((Get-FileHash -LiteralPath $taskPackage -Algorithm SHA256).Hash -ne '56f7f4b8bf9aee4b8efefbbdd4f67d5f74ebd1b100ed0806da71bf76af481aa9') {
        throw 'WebView2 SDK checksum mismatch; no dependency files were installed.'
    }
    Expand-Archive -LiteralPath $taskPackage -DestinationPath $taskWebView -Force
}
foreach ($taskDependency in @("$taskWebView\lib\net462\Microsoft.Web.WebView2.Core.dll","$taskWebView\lib\net462\Microsoft.Web.WebView2.WinForms.dll","$taskWebView\runtimes\win-x86\native\WebView2Loader.dll")) {
    $taskDestination = Join-Path $taskRoot ([IO.Path]::GetFileName($taskDependency))
    if (-not (Test-Path -LiteralPath $taskDestination) -or (Get-FileHash -LiteralPath $taskDependency).Hash -ne (Get-FileHash -LiteralPath $taskDestination).Hash) {
        Copy-Item -LiteralPath $taskDependency -Destination $taskDestination -Force
    }
    if ($Staged) { Copy-Item -LiteralPath $taskDependency -Destination (Join-Path $taskOutputFolder ([IO.Path]::GetFileName($taskDependency))) -Force }
}
$taskNodeCommand = Get-Command node -ErrorAction Stop
$taskNode = $taskNodeCommand.Source
@{node=$taskNode} | ConvertTo-Json | Set-Content -LiteralPath "$taskRoot\deps\host-runtime.json" -Encoding utf8
if (-not (Test-Path -LiteralPath "$taskRoot\theme\assets.css")) { throw 'Run build_host_theme.py once to extract the local game skin.' }
$taskEditionArgs=@("$taskRoot\FramePresenter.cs","$taskRoot\MinimapRepaintChecks.cs","$taskRoot\MacroSchedulingChecks.cs")
& $taskCompiler /nologo /target:winexe /platform:x86 /unsafe+ /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Security.dll /reference:System.Web.Extensions.dll "/reference:$taskRoot\Microsoft.Web.WebView2.Core.dll" "/reference:$taskRoot\Microsoft.Web.WebView2.WinForms.dll" "/out:$taskOutputFolder\$OutputName" @taskEditionArgs "$taskRoot\Launcher.cs" "$taskRoot\GameSurface.cs" "$taskRoot\GameHost.cs" "$taskRoot\GameSessions.cs" "$taskRoot\DATheme.cs" "$taskRoot\GameTools.cs" "$taskRoot\CompanionPanel.cs" "$taskRoot\SimpleMacro.cs" "$taskRoot\ToolChecks.cs" "$taskRoot\NativeLayers.cs" "$taskRoot\ServerProfile.cs" "$taskRoot\WorldZoom.cs" "$taskRoot\WorldZoomChecks.cs" "$taskRoot\MapFeatures.cs" "$taskRoot\MapFeatureChecks.cs" "$taskRoot\Hotkeys.cs" "$taskRoot\HotkeyChecks.cs" "$taskRoot\HostPreferences.cs" "$taskRoot\CharacterVault.cs" "$taskRoot\CharacterAppearance.cs" "$taskRoot\QuickLogin.cs" "$taskRoot\MapOverview.cs" "$taskRoot\HostFeatureChecks.cs" "$taskRoot\SkullHistory.cs" "$taskRoot\SkullChecks.cs" "$taskRoot\CaptureStorage.cs" "$taskRoot\SkillListStorage.cs" "$taskRoot\GameInstallation.cs" "$taskRoot\GameInstallationChecks.cs" "$taskRoot\HostUpdater.cs"
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed' }
