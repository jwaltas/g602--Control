"""Verify the ZIP, then test its runtime from a relocated directory with spaces.

Does not open hardware, emit keys, install the app, or change startup settings.
"""
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import zipfile

def verify(archive_path):
    archive_path = Path(archive_path).resolve()
    work = Path(__file__).resolve().parent.parent / 'work'
    work.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='package relocation ', dir=work) as folder:
        root = Path(folder)
        with zipfile.ZipFile(archive_path) as archive:
            if archive.testzip() is not None:
                raise ValueError('ZIP integrity failure')
            for entry in archive.namelist():
                path = Path(entry)
                if path.is_absolute() or '..' in path.parts or ':' in entry:
                    raise ValueError('Unsafe ZIP path')
                if any(part in ('Data', 'logs', '__pycache__') for part in path.parts):
                    raise ValueError('Private/runtime data in public archive')
            archive.extractall(root)
        release = next(root.iterdir())
        manifest = json.loads((release / 'manifest.json').read_text())
        for name, expected in manifest['files'].items():
            if hashlib.sha256((release / 'App' / name).read_bytes()).hexdigest() != expected:
                raise ValueError('Payload checksum failure: ' + name)
        for name, field in (('Seed/default-profile.json', 'seed_sha256'),
                            ('Open G602 Control.exe', 'launcher_sha256')):
            if hashlib.sha256((release / name).read_bytes()).hexdigest() != manifest[field]:
                raise ValueError('Release checksum failure: ' + name)
        app = release / 'App'
        runtime = json.loads((app / 'runtime.json').read_text())
        if Path(runtime['python']).is_absolute():
            raise ValueError('Machine-specific runtime path')
        program = app / runtime['python']
        subprocess.run([str(program), '-c',
            "import sys, pathlib, diagnose, setup_profile; "
            "sys.path.insert(0,str(pathlib.Path(diagnose.__file__).parent/'hidapi')); "
            "import hid; print('Relocated bundled runtime: '+sys.version.split()[0])"],
            cwd=root, check=True, timeout=20)
        print('ZIP, all payload checksums, public data separation and relocated runtime passed')
    digest = hashlib.sha256(archive_path.read_bytes()).hexdigest()
    archive_path.with_suffix('.zip.sha256').write_text(digest+'  '+archive_path.name+'\n')

if __name__ == '__main__':
    import sys
    verify(sys.argv[1])
