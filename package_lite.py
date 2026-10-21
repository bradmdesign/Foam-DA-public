"""Package an explicit allowlist. Installed game data and user caches never ship."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent
HOST = ROOT / 'prototype/widescreen'


def permitted(name):
    path = Path(name)
    return (not path.is_absolute() and '..' not in path.parts and
            not set(part.lower() for part in path.parts) &
            {'runtime', 'sessions', 'prepared-profiles', 'webview-profile', 'characters', 'private', 'screenshots', 'skulled'} and
            path.name.lower() not in {'darkages.exe', 'setoa.dat'} and path.suffix.lower() != '.dat')


def package(host_update=False, output_name=None):
    files = {}

    def add(source, name):
        if not permitted(name):
            raise ValueError('Game data or a user cache cannot be distributed: '+name)
        source = Path(source)
        if not source.is_file():
            raise ValueError('Build Foam DA first; missing '+str(source.relative_to(ROOT)))
        files[name] = source

    for name in ['Foam-DA.exe', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll', 'host-dock.css']:
        add(HOST/name, name)
    for name in ['core.mjs', 'worker.mjs', 'channel.json']:
        add(HOST/'updater'/name, 'updater/'+name)
    for name in ['serve-local.mjs', 'companion-runtime.js', 'companion-runtime.css']:
        add(ROOT/'site/scripts'/name, 'library/scripts/'+name)
    for source in (HOST/'theme').iterdir():
        if source.is_file() and source.suffix.lower() in {'.png','.cur','.css','.ttf'}:
            add(source, 'theme/'+source.name)
    add(HOST/'client-patches/catalog.json', 'client-patches/catalog.json')
    for profile in ['960x600','1088x640','1152x680']:
        for name in ['client.patch.gz','layout.patch.gz']:
            add(HOST/'client-patches'/profile/name, 'client-patches/'+profile+'/'+name)
    add(ROOT/'THIRD-PARTY-NOTICES.txt', 'THIRD-PARTY-NOTICES.txt')
    for name in ['LICENSE.txt','NOTICE.txt']:
        add(HOST/'deps/webview2-1.0.4258.31'/name, 'licenses/WebView2-'+name)
    if not host_update:
        client = ROOT/'site/dist/client'
        if not (client/'index.html').is_file() and not (client/'index.html.gz').is_file():
            raise ValueError('Build the offline Library with Build.ps1 first. Use --host-update for a small update to an existing Foam DA installation.')
        node = shutil.which('node')
        if not node:
            raise ValueError('Node.js 24 or newer is required for packaging the local Library server.')
        version = subprocess.check_output([node,'--version'],text=True).strip()
        if not version.startswith('v') or not all(p.isdigit() for p in version[1:].split('.')):
            raise ValueError('Cannot determine the bundled Node runtime version.')
        license_path = HOST/'deps'/('Node-'+version+'-LICENSE.txt')
        if not license_path.is_file():
            with urllib.request.urlopen('https://raw.githubusercontent.com/nodejs/node/'+version+'/LICENSE',timeout=30) as response:
                license_path.write_bytes(response.read())
        add(license_path,'licenses/Node-LICENSE.txt')
        add(ROOT/'site/vendor/shadcn-tailwind-4.13.0.LICENSE.md','licenses/shadcn-tailwind-LICENSE.md')
        files['deps/node.exe'] = Path(node)
        for source in client.rglob('*'):
            if source.is_file():
                if any(part.startswith('.') for part in source.relative_to(client).parts):
                    continue
                if source.suffix != '.gz' and source.with_name(source.name+'.gz').is_file():
                    continue
                add(source, 'library/dist/client/'+source.relative_to(client).as_posix())

    readme = ("Foam DA Lite host update\n\nClose the Foam DA host. Extract this archive into the folder containing Foam-DA.exe, "
              "replacing the host files. Keep the existing Library and deps/host-runtime.json. Reopen Foam-DA.exe.\n" if host_update else
              "Foam DA Lite\n\nExtract this entire archive into a writable folder and open Foam-DA.exe.\n")
    readme += ("\nOn first launch, select your original Dark Ages 7.41 installation (the folder containing Darkages.exe). "
               "Change it later through Settings > Dark Ages folder. The game is installed separately; this archive contains no full game executable or game DAT archives.\n\n"
               "Foam DA reconstructs its widescreen files locally, then copies the selected installation's assets into private game sessions. "
               "This preserves the original installation and separates each character's writable files. Local sessions still require disk space; they are never included in downloads. "
               "The selected folder is remembered locally. No account data is copied.\n")
    report = {'kind':'host-update' if host_update else 'complete-lite', 'schema':1,
              'requires_existing_library':host_update, 'game_runtime_bundled':False,
              'full_game_profiles_bundled':False, 'local_preparation_recipe_bytes':sum(p.stat().st_size for n,p in files.items() if n.startswith('client-patches/')),
              'files':[{'path':n,'bytes':p.stat().st_size,'sha256':hashlib.file_digest(p.open('rb'),'sha256').hexdigest()} for n,p in sorted(files.items())]}
    if output_name and (Path(output_name).name != output_name or not output_name.endswith('.zip')):
        raise ValueError('Output must be a ZIP filename inside releases.')
    output = ROOT/'releases'/(output_name or ('Foam-DA-Lite-HostUpdate.zip' if host_update else 'Foam-DA-Lite.zip'))
    output.parent.mkdir(exist_ok=True)
    if output.exists():
        raise ValueError('A package already exists at '+str(output)+'. Preserve or rename it before building another.')
    temp = output.with_suffix('.zip.partial')
    try:
        with zipfile.ZipFile(temp,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9,allowZip64=True) as z:
            for name, source in sorted(files.items()):
                z.write(source,name)
            z.writestr('README.txt',readme)
            z.writestr('BUILD-REPORT.json',json.dumps(report,indent=2)+'\n')
            if not host_update:
                z.writestr('deps/host-runtime.json',json.dumps({'node':'deps/node.exe'}))
        with zipfile.ZipFile(temp) as z:
            if not all(permitted(n) for n in z.namelist()) or z.testzip() is not None:
                raise ValueError('Package inventory or integrity check failed')
            for item in report['files']:
                if hashlib.sha256(z.read(item['path'])).hexdigest()!=item['sha256']:
                    raise ValueError('Packaged file differs from the verified build')
        temp.rename(output)
    finally:
        temp.unlink(missing_ok=True)
    digest = hashlib.file_digest(output.open('rb'),'sha256').hexdigest()
    output.with_suffix('.zip.sha256').write_text(digest+'  '+output.name+'\n')
    print(json.dumps({'package':str(output),'zip_bytes':output.stat().st_size,'game_runtime_bundled':False,
                      'requires_existing_library':host_update,'sha256':digest},indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--host-update',action='store_true')
    parser.add_argument('--output',help='A new ZIP filename inside releases; existing packages are never overwritten.')
    args=parser.parse_args()
    package(args.host_update,args.output)
