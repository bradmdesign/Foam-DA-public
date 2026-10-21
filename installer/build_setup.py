"""Build a standalone Windows installer from a verified complete Lite archive.

Build tools are required only on the publisher's computer. The installed player
application includes Node, the offline Library, updater and WebView2 prerequisite.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--webview2', type=Path, required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--commit', required=True)
    args = parser.parse_args()
    if not re.fullmatch(r'\d+\.\d+\.\d+', args.version) or not re.fullmatch(r'[a-f0-9]{40}', args.commit):
        parser.error('Use a three-part version and the published 40-character source commit.')
    source = args.archive.resolve(strict=True)
    prerequisite = args.webview2.resolve(strict=True)
    # Verify the real Microsoft runtime's Authenticode publisher before packaging.
    result = subprocess.run(['powershell', '-NoProfile', '-Command',
        '$s=Get-AuthenticodeSignature -LiteralPath $env:FOAM_SETUP_WEBVIEW; if($s.Status -ne "Valid" -or $s.SignerCertificate.Subject -notmatch "Microsoft Corporation"){exit 1}'],
        env=dict(os.environ,FOAM_SETUP_WEBVIEW=str(prerequisite)), capture_output=True)
    if result.returncode:
        raise RuntimeError('The WebView2 prerequisite must have a valid Microsoft Authenticode signature.')
    output = ROOT/'releases'/('Foam-DA-Setup-'+args.version+'.exe')
    if output.exists():
        raise RuntimeError('Preserve the existing immutable installer before rebuilding.')
    build = ROOT/'installer/build'
    build.mkdir(parents=True, exist_ok=True)
    signer = ROOT/'publisher/build/Foam-Publisher.exe'
    key = Path(os.environ['LOCALAPPDATA'])/'Foam-DA/Publisher/signing-key.dpapi'
    public_xml = ROOT/'publisher/build/public-key.xml'
    if not signer.is_file() or not key.is_file():
        raise RuntimeError('Initialize the existing publisher signing key first.')
    subprocess.run([str(signer), '--ensure', str(key), str(public_xml)], check=True)
    xml = public_xml.read_text(encoding='utf-8')
    (build/'SetupKey.cs').write_text('internal static class SetupKey { internal const string PublicXml = '+json.dumps(xml)+'; }\n', encoding='utf-8')
    (build/'Version.cs').write_text('[assembly:System.Reflection.AssemblyTitle("Foam DA Setup")]\n[assembly:System.Reflection.AssemblyFileVersion("'+args.version+'.0")]\n[assembly:System.Reflection.AssemblyVersion("'+args.version+'.0")]\n', encoding='utf-8')
    compiler = Path(os.environ['WINDIR'])/'Microsoft.NET/Framework/v4.0.30319/csc.exe'
    stub = build/'Setup-stub.exe'
    subprocess.run([str(compiler), '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
        '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll',
        '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll',
        '/out:'+str(stub), str(ROOT/'installer/Setup.cs'), str(build/'SetupKey.cs'), str(build/'Version.cs')], check=True)
    files = []
    report = {'schema':1, 'product':'Foam-DA-Lite', 'version':args.version, 'source_commit':args.commit, 'files':files}
    payload = build/'payload.zip'
    # Preserve the source archive's compression choices without needing a third-party extractor.
    with zipfile.ZipFile(source) as original, zipfile.ZipFile(payload, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as target:
        old = json.loads(original.read('BUILD-REPORT.json'))
        if old.get('kind') != 'complete-lite' or old.get('game_runtime_bundled') is not False:
            raise RuntimeError('Build a complete Lite archive; host-only updates cannot be installers.')
        inventory = {f['path']:f for f in old['files']}
        for entry in original.infolist():
            if entry.is_dir():
                raise RuntimeError('The source archive must contain files only.')
            with original.open(entry) as stream, target.open(entry.filename, 'w', force_zip64=True) as destination:
                digest = hashlib.sha256()
                size = 0
                while chunk := stream.read(1024*1024):
                    digest.update(chunk); size += len(chunk); destination.write(chunk)
            record = {'path':entry.filename, 'bytes':size, 'sha256':digest.hexdigest()}
            if entry.filename in inventory and record != inventory[entry.filename]:
                raise RuntimeError('Source archive file failed its inventory checksum: '+entry.filename)
            files.append(record)
        name = 'prerequisites/MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
        target.write(prerequisite, name)
        files.append({'path':name, 'bytes':prerequisite.stat().st_size,
                      'sha256':hashlib.file_digest(prerequisite.open('rb'),'sha256').hexdigest()})
        report_file = build/'setup-report.json'
        report_file.write_text(json.dumps(report, separators=(',',':'))+'\n', encoding='utf-8')
        signature = build/'setup-report.sig'
        subprocess.run([str(signer), '--sign', str(key), str(report_file), str(signature)], check=True)
        target.write(report_file, 'setup-report.json'); target.write(signature, 'setup-report.sig')
    partial = output.with_suffix('.exe.partial')
    with partial.open('xb') as destination, stub.open('rb') as stream:
        shutil.copyfileobj(stream, destination)
        start = destination.tell()
        digest = hashlib.sha256()
        with payload.open('rb') as stream:
            while chunk := stream.read(1024*1024):
                destination.write(chunk); digest.update(chunk)
        destination.write(struct.pack('<qq', start, payload.stat().st_size)+digest.digest()+b'FOAM-SETUP-V1!\r\n')
    partial.replace(output)
    checksum = hashlib.file_digest(output.open('rb'),'sha256').hexdigest()
    output.with_suffix('.exe.sha256').write_text(checksum+'  '+output.name+'\n', encoding='utf-8')
    print(json.dumps({'installer':str(output), 'bytes':output.stat().st_size, 'sha256':checksum,
                      'source_commit':args.commit, 'files':len(files)}, indent=2), flush=True)


if __name__ == '__main__':
    main()
