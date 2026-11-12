"""Package the local prototype with its existing Windows HID dependency."""
import json
import hashlib
from pathlib import Path
import shutil
import sys
import zipfile

def package(repo,outputs,library):
    version='0.3.1'
    destination=outputs/f'G602-Control-{version}'
    app=destination/'App'
    seed=destination/'Seed'
    app.mkdir(parents=True,exist_ok=True)
    seed.mkdir(parents=True,exist_ok=True)
    config=json.loads((repo/'default-macros.json').read_text(encoding='utf-8'))
    config['mode']='automatic-default-profile'
    config['activation']='Saved default profile activates after successful read-only readiness checks'
    config['double_tap_ms']=300
    config['typing_delay_ms']=5
    keys=[ord(letter.lower())-ord('a')+4 for letter in 'DimAligned']+[40]
    events=[]
    for i,key in enumerate(keys):
        events.extend([{'kind':'key','usage':key,'down':True},{'kind':'delay','milliseconds':25},{'kind':'key','usage':key,'down':False}])
        if i<len(keys)-1:events.append({'kind':'delay','milliseconds':25})
    config['double_tap_macros']={'7':{'name':'DimAligned','events':events,'use_default_delay':False,'default_delay_ms':25,
        'timing_status':'new double-tap command: explicit 25 ms event delays'}}
    (outputs/'control-macros.json').write_text(json.dumps(config,indent=2),encoding='utf-8')
    for name in ('G602Control.exe','G602PlaybackCore.dll','control-macros.json'):
        shutil.copy2(outputs/name,app/name)
    for name in ('setup_profile.py','diagnose.py'):
        shutil.copy2(repo/name,app/name)
    shutil.copytree(library,app/'hidapi',dirs_exist_ok=True)
    runtime=repo.parent/'work'/'python-3.13.17-embed-amd64.zip'
    with zipfile.ZipFile(runtime) as archive:
        archive.extractall(app/'python')
    (app/'python'/'python313._pth').write_text('python313.zip\n.\n..\n',encoding='utf-8')
    (app/'runtime.json').write_text(json.dumps({'python':'python/python.exe'},indent=2),encoding='utf-8')
    (app/'installed-layout.txt').write_text('G602 Control: settings are in ../Data\n',encoding='utf-8')
    shutil.copy2(outputs/'G602Install.exe',destination/'Install or Update G602 Control.exe')
    shutil.copy2(outputs/'G602Open.exe',destination/'Open G602 Control.exe')
    profile={'format':'g602-control-profile','format_version':1,'app_version':version,'name':'Default','macros':{'schema_version':1,'buttons':config['buttons'],'double_tap_macros':config['double_tap_macros']},'timing':{'double_tap_ms':300,'typing_delay_ms':5}}
    (seed/'default-profile.json').write_text(json.dumps(profile,indent=2),encoding='utf-8')
    shutil.copy2(repo/'README-user.txt',destination/'README.txt')
    digest=lambda path:hashlib.sha256(path.read_bytes()).hexdigest()
    manifest={'application':'g602-control','version':version,'seed_sha256':digest(seed/'default-profile.json'),
              'launcher_sha256':digest(destination/'Open G602 Control.exe'),
              'files':{p.relative_to(app).as_posix():digest(p) for p in sorted(app.rglob('*')) if p.is_file() and '__pycache__' not in p.parts}}
    (destination/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    with zipfile.ZipFile(outputs/f'G602-Control-{version}.zip','w',zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(destination.rglob('*')):
            if path.is_file() and 'logs' not in path.relative_to(destination).parts and '__pycache__' not in path.parts and 'Data' not in path.relative_to(destination).parts:
                archive.write(path,(Path(destination.name)/path.relative_to(destination)).as_posix())
    return destination

if __name__=='__main__':
    repo=Path(__file__).resolve().parent
    outputs=repo.parent/'outputs'
    print(package(repo,outputs,repo.parent/'work'/'hidapi'))
