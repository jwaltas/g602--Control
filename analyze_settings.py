"""Analyze a G HUB ZIP without modifying the archive or its SQLite database."""
import argparse
from contextlib import closing
import hashlib
import json
from pathlib import Path
import sqlite3
import tempfile
import zipfile

def analyze(source):
    raw = Path(source).read_bytes()
    with zipfile.ZipFile(source) as archive:
        info = archive.getinfo('settings.db')
        if info.file_size > 64 * 1024 * 1024:
            raise ValueError('Settings database exceeds analysis size limit')
        database = archive.read(info)
    with tempfile.TemporaryDirectory() as folder:
        path = Path(folder) / 'settings.db'
        path.write_bytes(database)
        with closing(sqlite3.connect(path.as_uri() + '?mode=ro', uri=True)) as db:
            row = db.execute('SELECT file FROM data ORDER BY _id DESC LIMIT 1').fetchone()
        if not row:
            raise ValueError('No G HUB settings record')
        data = json.loads(row[0])
    cards = {c['id']: c for c in data.get('cards', {}).get('cards', [])}
    apps = {a['applicationId']: a for a in data.get('applications', {}).get('applications', [])}
    result = {'zip_sha256': hashlib.sha256(raw).hexdigest(),
              'database_sha256': hashlib.sha256(database).hexdigest(), 'profiles': []}
    for profile in data.get('profiles', {}).get('profiles', []):
        out = {'name': profile.get('name'),
               'application': apps.get(profile.get('applicationId'), {}).get('name'), 'macros': []}
        for assignment in profile.get('assignments', []):
            card = cards.get(assignment.get('cardId'), {})
            macro = card.get('macro')
            if not macro:
                continue
            components = macro.get('sequence', {}).get('simpleSequence', {}).get('components', [])
            pressed, warnings, keydowns = set(), [], []
            for component in components:
                key = component.get('keyboard')
                if not key:
                    continue
                usage = key.get('hidUsage')
                if key.get('isDown', False):
                    if pressed:
                        warnings.append('Overlapping key-down events preserved')
                    pressed.add(usage)
                    keydowns.append(key.get('displayName', usage))
                else:
                    if usage not in pressed:
                        warnings.append('Key-up without matching key-down')
                    pressed.discard(usage)
            if pressed:
                warnings.append('Sequence ends with keys held')
            out['macros'].append({'slot': assignment.get('slotId'), 'name': card.get('name'),
                'key_down_order': keydowns, 'warnings': sorted(set(warnings)),
                'onboard_status': 'unverified G HUB metadata', 'original_macro': macro})
        result['profiles'].append(out)
    return result

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = analyze(args.archive)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print('Analyzed', sum(len(p['macros']) for p in result['profiles']), 'macros; originals preserved')
