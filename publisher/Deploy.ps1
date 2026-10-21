param(
    [Parameter(Mandatory=$true)][string]$SshHost,
    [Parameter(Mandatory=$true)][string]$RemoteDirectory,
    [ValidateRange(1,65535)][int]$Port=22,
    [string]$IdentityFile,
    [string]$KnownHostsFile,
    [switch]$Preview
)
$ErrorActionPreference='Stop'
if($SshHost -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._@-]*$'){throw 'Use an existing SSH alias or user@hostname.'}
if($RemoteDirectory -notmatch '^/[a-zA-Z0-9_/-]{8,}$' -or $RemoteDirectory.Contains('..') -or $RemoteDirectory.Contains('//')){throw 'Use an absolute, dedicated public update directory; spaces and traversal are not accepted.'}
$RemoteDirectory=$RemoteDirectory.TrimEnd('/')
if($RemoteDirectory -notmatch '/(foam-da-updates|foam-da/updates)$'){throw 'The remote target must end in /foam-da-updates or /foam-da/updates, keeping releases separate from the website root.'}
$taskOutput=Join-Path $PSScriptRoot 'output'
& node (Join-Path $PSScriptRoot 'check-channel.mjs')
if($LASTEXITCODE -ne 0){throw 'Release verification failed; nothing was uploaded.'}
$taskFiles=@(Get-ChildItem -LiteralPath $taskOutput -File | Where-Object {$_.Name -match '^(feed.json(?:.sig)?|(?:ads|app|library)-[0-9]+-[A-Za-z0-9._-]+\.(?:fup|json))$'})
if($taskFiles.Count -lt 4){throw 'Prepare a signed release first.'}
$taskFeed=$taskFiles | Where-Object Name -eq 'feed.json'
$taskSignature=$taskFiles | Where-Object Name -eq 'feed.json.sig'
if(-not $taskFeed -or -not $taskSignature){throw 'The signed feed is missing.'}
if($Preview){
    Write-Output "Upload $($taskFiles.Count) public files to ${SshHost}:$RemoteDirectory, then activate the feed. No server files changed."
    $taskFiles | Select-Object Name,Length
    return
}
Get-Command ssh,scp -ErrorAction Stop | Out-Null
$taskNonce=[Guid]::NewGuid().ToString('N')
$taskStage="$RemoteDirectory/.upload-$taskNonce"
$taskSshArgs=@('-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-p',"$Port",$SshHost)
if($IdentityFile){$taskKey=(Resolve-Path -LiteralPath $IdentityFile -ErrorAction Stop).Path;$taskSshArgs=@('-i',$taskKey,'-o','IdentitiesOnly=yes')+$taskSshArgs}
if($KnownHostsFile){$taskKnown=(Resolve-Path -LiteralPath $KnownHostsFile -ErrorAction Stop).Path;$taskSshArgs=@('-o',"UserKnownHostsFile=$taskKnown")+$taskSshArgs}
# SSH and scp use existing local keys/agent and known_hosts. No secrets in arguments.
& ssh @taskSshArgs "umask 022; mkdir -p '$taskStage'"
if($LASTEXITCODE -ne 0){throw 'Could not create the dedicated staging directory. Check SSH access and directory ownership.'}
$taskScpArgs=@('-B','-o','StrictHostKeyChecking=yes','-P',"$Port")
if($IdentityFile){$taskScpArgs=@('-i',$taskKey,'-o','IdentitiesOnly=yes')+$taskScpArgs}
if($KnownHostsFile){$taskScpArgs=@('-o',"UserKnownHostsFile=$taskKnown")+$taskScpArgs}
$taskScpArgs+=@($taskFiles | ForEach-Object FullName)
$taskScpArgs+="${SshHost}:$taskStage/"
& scp @taskScpArgs
if($LASTEXITCODE -ne 0){throw "Upload interrupted. Current updates remain active. Public staging files are at $taskStage."}
$taskScript=New-Object System.Collections.Generic.List[string]
$taskScript.Add('set -eu')
foreach($taskFile in $taskFiles){
    $taskHash=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $taskScript.Add("printf '%s  %s\n' '$taskHash' '$taskStage/$($taskFile.Name)' | sha256sum -c -")
}
foreach($taskFile in $taskFiles | Where-Object {$_.Name -ne 'feed.json' -and $_.Name -ne 'feed.json.sig'}){
    $taskName=$taskFile.Name
    $taskHash=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $taskScript.Add("if [ -f '$RemoteDirectory/$taskName' ]; then printf '%s  %s\n' '$taskHash' '$RemoteDirectory/$taskName' | sha256sum -c -; else mv '$taskStage/$taskName' '$RemoteDirectory/$taskName'; fi")
}
$taskScript.Add("mv '$taskStage/feed.json.sig' '$RemoteDirectory/feed.json.sig'")
$taskScript.Add("mv '$taskStage/feed.json' '$RemoteDirectory/feed.json'")
# Retire only known duplicate public upload files; never recurse or touch site files.
foreach($taskFile in $taskFiles){$taskScript.Add("if [ -f '$taskStage/$($taskFile.Name)' ]; then rm '$taskStage/$($taskFile.Name)'; fi")}
$taskScript.Add("rmdir '$taskStage'")
($taskScript -join "`n") | & ssh @taskSshArgs "tr -d '\015' | sh -s"
if($LASTEXITCODE -ne 0){throw 'Remote validation or activation failed. Existing immutable files were preserved; inspect the public staging directory.'}
Write-Output 'Signed updates deployed. Verify the public feed URL before distributing the app.'
