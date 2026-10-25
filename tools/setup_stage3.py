"""Download checksum-verified portable analysis tools into ignored third_party/."""
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import shutil
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'third_party' / 'stage3'


def request(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'TagForce-local-research'}), timeout=120)


def api(url):
    with request(url) as stream:
        return json.load(stream)


def github(repo, tag, suffix):
    release = api(f'https://api.github.com/repos/{repo}/releases/tags/{tag}')
    asset = next(a for a in release['assets'] if a['name'].endswith(suffix))
    digest = asset.get('digest', '')
    if not digest.startswith('sha256:'):
        raise ValueError('Upstream SHA-256 is required')
    return dict(name=asset['name'], url=asset['browser_download_url'],
                sha256=digest[7:], release=tag)


def install(item):
    archive = DEST / 'downloads' / item['name']
    archive.parent.mkdir(parents=True, exist_ok=True)
    if not archive.exists():
        print('Downloading ' + item['name'], flush=True)
        partial = archive.with_suffix('.partial')
        with request(item['url']) as source, partial.open('wb') as target:
            shutil.copyfileobj(source, target)
        partial.replace(archive)
    with archive.open('rb') as stream:
        actual = hashlib.file_digest(stream, 'sha256').hexdigest()
    if actual != item['sha256']:
        raise ValueError('Checksum mismatch: ' + item['name'])
    destination = DEST / item['tool']
    marker = destination / '.installed-sha256'
    if marker.exists() and marker.read_text() == actual:
        return
    if destination.exists():
        raise ValueError('Incomplete or different install: ' + str(destination))
    print('Extracting ' + item['name'], flush=True)
    with zipfile.ZipFile(archive) as z:
        for member in z.infolist():
            path = (destination / member.filename).resolve()
            if not path.is_relative_to(destination.resolve()):
                raise ValueError('Unsafe archive member')
        z.extractall(destination)
    marker.write_text(actual)
    print('Verified ' + item['tool'], flush=True)


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    lock = DEST / 'toolchain-lock.json'
    if lock.exists():
        items = json.loads(lock.read_text())
    else:
        # Match the extension build exactly, rather than assume latest Ghidra compatibility.
        items = [github('NationalSecurityAgency/ghidra', 'Ghidra_12.1.3_build', '.zip'),
                 github('kotcrab/ghidra-allegrex', 'v21.4', '.zip'),
                 github('hrydgard/ppsspp', 'v1.20.4', '-Windows-x64.zip')]
        jdk = api('https://api.adoptium.net/v3/assets/latest/25/hotspot?architecture=x64&image_type=jdk&os=windows&vendor=eclipse')[0]
        package = jdk['binary']['package']
        items.append(dict(name=package['name'], url=package['link'],
                          sha256=package['checksum'], release=jdk['release_name']))
        for item, tool in zip(items, ['ghidra', 'allegrex', 'ppsspp', 'jdk']):
            item['tool'] = tool
        lock.write_text(json.dumps(items, indent=2) + '\n')
    with ThreadPoolExecutor(max_workers=4) as pool:
        list(pool.map(install, items))
    print('Portable tools verified. Lock: ' + str(lock))


if __name__ == '__main__':
    main()
