"""Fetch pinned, checksum-verified Windows build dependencies. No pip install."""
import hashlib
from pathlib import Path
import urllib.request
import zipfile

DEPENDENCIES = (
    ('python-3.13.17-embed-amd64.zip',
     'https://www.python.org/ftp/python/3.13.17/python-3.13.17-embed-amd64.zip',
     '9e35319bffedab8b36ad44d8f377cb4f1733bcec257a0b6d04d94f01486ddb38'),
    ('hidapi-0.15.0-cp313-cp313-win_amd64.whl',
     'https://files.pythonhosted.org/packages/d9/00/7dd3f866a748b0c9eb4adedaa025ec8c533ed1946e9e918661c948a5c0f4/hidapi-0.15.0-cp313-cp313-win_amd64.whl',
     '04e0c1ed6d742bc6e1d00599394bec6b2afb8ee2fc5a0a183d3c4c2d4e315c34'),
)

def prepare():
    work = Path(__file__).resolve().parent.parent / 'work'
    work.mkdir(exist_ok=True)
    for name, url, expected in DEPENDENCIES:
        destination = work / name
        if not destination.exists():
            with urllib.request.urlopen(url, timeout=60) as response:
                payload = response.read()
            if hashlib.sha256(payload).hexdigest() != expected:
                raise ValueError('Download checksum mismatch: ' + name)
            destination.write_bytes(payload)
        if hashlib.sha256(destination.read_bytes()).hexdigest() != expected:
            raise ValueError('Cached dependency checksum mismatch: ' + name)
        if name.endswith('.whl'):
            with zipfile.ZipFile(destination) as archive:
                for entry in archive.namelist():
                    path = Path(entry)
                    if path.is_absolute() or '..' in path.parts or ':' in entry:
                        raise ValueError('Unsafe dependency archive path')
                archive.extractall(work / 'hidapi')
    print('Pinned runtime and HID dependency verified')

if __name__ == '__main__':
    prepare()
