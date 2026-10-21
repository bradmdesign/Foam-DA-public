"""Publish Foam DA through the user's existing Git Credential Manager login.

Credentials remain in memory and are sent only to GitHub's API/upload hosts.
This helper is restricted to the Foam-DA-public repository.
"""
import argparse
import hashlib
import http.client
import json
import os
from pathlib import Path
import subprocess
import urllib.error
import urllib.parse
import urllib.request

REPO = 'bradmdesign/Foam-DA-public'
API = 'https://api.github.com/repos/' + REPO


def token():
    env = dict(os.environ, GIT_TERMINAL_PROMPT='0', GCM_INTERACTIVE='never')
    result = subprocess.run(['git', 'credential', 'fill'], input='protocol=https\nhost=github.com\n\n',
                            capture_output=True, text=True, env=env, check=True)
    fields = dict(line.split('=', 1) for line in result.stdout.splitlines() if '=' in line)
    if not fields.get('password'):
        raise RuntimeError('GitHub authentication is unavailable.')
    return fields['password']


def request(secret, method, suffix, body=None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(API + suffix, data=data, method=method, headers={
        'Authorization': 'Bearer ' + secret, 'Accept': 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28', 'Content-Type': 'application/json', 'User-Agent': 'Foam-DA-Publisher'})
    try:
        with urllib.request.urlopen(req, timeout=60) as response:
            raw = response.read()
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        if error.code == 404 and method == 'GET':
            return None
        raise RuntimeError('GitHub API returned HTTP ' + str(error.code)) from None


def upload(secret, release, source):
    source = Path(source).resolve(strict=True)
    expected = hashlib.file_digest(source.open('rb'), 'sha256').hexdigest()
    existing = next((a for a in release['assets'] if a['name'] == source.name), None)
    if existing:
        if existing.get('digest') == 'sha256:' + expected:
            print('Already verified: ' + source.name, flush=True)
            return
        if source.name not in ('feed.json', 'feed.json.sig'):
            raise RuntimeError('Refusing to replace an immutable release asset: ' + source.name)
        request(secret, 'DELETE', '/releases/assets/' + str(existing['id']))
    url = urllib.parse.urlsplit(release['upload_url'].split('{')[0])
    if url.hostname != 'uploads.github.com' or not url.path.startswith('/repos/' + REPO + '/releases/'):
        raise RuntimeError('Unexpected release upload destination.')
    connection = http.client.HTTPSConnection(url.hostname, timeout=600)
    try:
        connection.putrequest('POST', url.path + '?' + urllib.parse.urlencode({'name': source.name}))
        for key, value in {'Authorization': 'Bearer ' + secret, 'Content-Type': 'application/octet-stream',
                           'Content-Length': str(source.stat().st_size), 'User-Agent': 'Foam-DA-Publisher',
                           'Accept': 'application/vnd.github+json'}.items():
            connection.putheader(key, value)
        connection.endheaders()
        with source.open('rb') as stream:
            while chunk := stream.read(1024 * 1024):
                connection.send(chunk)
        response = connection.getresponse()
        raw = response.read()
        if response.status != 201:
            raise RuntimeError('GitHub upload returned HTTP ' + str(response.status))
        asset = json.loads(raw)
        if asset['size'] != source.stat().st_size or asset.get('digest') != 'sha256:' + expected:
            raise RuntimeError('GitHub uploaded asset checksum does not match: ' + source.name)
        print('Uploaded and verified: ' + source.name, flush=True)
    finally:
        connection.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['status', 'make-public', 'release'])
    parser.add_argument('--tag')
    parser.add_argument('--commit')
    parser.add_argument('--title')
    parser.add_argument('--notes', type=Path)
    parser.add_argument('--prerelease', action='store_true')
    parser.add_argument('--asset', type=Path, action='append', default=[])
    args = parser.parse_args()
    secret = token()
    if args.action == 'make-public':
        repo = request(secret, 'PATCH', '', {'private': False})
        if repo['private']:
            raise RuntimeError('Repository is still private.')
        print(json.dumps({'repository': repo['full_name'], 'visibility': repo['visibility'], 'url': repo['html_url']}))
    elif args.action == 'status':
        repo = request(secret, 'GET', '')
        print(json.dumps({'repository': repo['full_name'], 'private': repo['private'], 'branch': repo['default_branch']}))
    else:
        if not args.tag or not args.commit or not args.notes or not args.title:
            parser.error('release needs --tag, --commit, --title and --notes')
        notes = args.notes.read_text(encoding='utf-8')
        release = request(secret, 'GET', '/releases/tags/' + urllib.parse.quote(args.tag, safe=''))
        if release is None:
            release = request(secret, 'POST', '/releases', {'tag_name': args.tag, 'target_commitish': args.commit,
                'name': args.title, 'body': notes, 'draft': True, 'prerelease': args.prerelease})
        # Immutable component files first; feed signature then feed last.
        for source in sorted(args.asset, key=lambda p: (p.name == 'feed.json', p.name == 'feed.json.sig', p.name)):
            upload(secret, release, source)
        release = request(secret, 'PATCH', '/releases/' + str(release['id']),
                          {'name': args.title, 'body': notes, 'draft': False, 'prerelease': args.prerelease})
        print(json.dumps({'release': release['html_url'], 'tag': release['tag_name'], 'assets': len(release['assets'])}))


if __name__ == '__main__':
    main()
