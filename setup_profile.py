"""Guarded, user-authorized G602 trigger setup. Never resets or flashes.

Only the six reviewed side-button binding slots may change. Every other byte
must match the captured baseline. All writes require --apply explicitly.
"""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import sys
import subprocess
import time
from diagnose import Reader

BASELINE = bytes.fromhex('01010202020406080a0100000200000300000400000500001a000000001e00001f000020000028000029000000')

def proposed_profile(current, scope='six'):
    expected = bytearray(BASELINE)
    buttons = range(4,10) if scope == 'six' else [7]
    if scope not in ('six','g7'):
        raise ValueError('Unapproved scope')
    for button in buttons:
        offset = 12 + (button-2)*3
        expected[offset:offset+3] = bytes([0,0,0x68+button-4])
    target = bytes(expected)
    if current not in (BASELINE,target):
        raise ValueError('Current profile differs from the reviewed baseline; nothing was written')
    return target

def read_profile(reader):
    reader.query(0x8080,2,b'\x80\x2a\0')
    chunks=[]
    for i in (1,2,3):
        reply=reader.query(0x8080,3,bytes([i,0,0]))
        if len(reply)!=16 or reply[0]!=i:
            raise ValueError('Invalid profile reply')
        chunks.append(reply[1:])
    return b''.join(chunks)

def write_packets(index, profile):
    # Pure packet builder: it cannot open a device or transmit anything.
    if len(profile)!=45:
        raise ValueError('Profile must be exactly 45 bytes')
    valid = [proposed_profile(BASELINE,scope) for scope in ('six','g7')]
    if profile not in valid and profile != BASELINE:
        raise ValueError('Not an approved trigger profile')
    return [bytes([0x10,1,index,0x2D,0x81,42,0])] + [
        bytes([0x11,1,index,0x3D,i])+profile[(i-1)*15:i*15] for i in (1,2,3)
    ] + [bytes([0x10,1,index,0x4D,0x82,1,0])]

def exchange(short,long,packet,trace):
    entry={'tx':packet.hex()};trace.append(entry)
    target=short if packet[0]==0x10 else long
    if target.write(packet)!=len(packet):
        raise OSError('Incomplete HID transfer; stopped without reset or retry')
    deadline=time.monotonic()+2
    while time.monotonic()<deadline:
        reply=bytes(long.read(64,100))
        if len(reply)==20 and reply[0]==0x11 and reply[1:4]==packet[1:4]:
            entry['rx']=reply.hex();return
        if len(reply)>=6 and reply[1]==1 and reply[2] in (0xFF,0x8F) and reply[3:5]==packet[2:4]:
            entry['error']=reply.hex();raise OSError('Device rejected transfer: '+reply.hex())
    raise TimeoutError('Profile transfer timed out; no automatic recovery write attempted')

def transact(reader,short,long,current,target,backup_path,trace):
    if current==target:
        return 'already_configured'
    approved=[proposed_profile(BASELINE,s) for s in ('six','g7')]
    if not ((current==BASELINE and target in approved) or (current in approved and target==BASELINE)):
        raise ValueError('Refusing an unreviewed transition')
    backup={'created_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
            'profile_hex':current.hex(),'sha256':hashlib.sha256(current).hexdigest(),
            'target_hex':target.hex(),'policy':'only reviewed side-button binding changes'}
    backup_path.parent.mkdir(parents=True,exist_ok=True)
    with backup_path.open('x',encoding='utf-8') as stream:
        json.dump(backup,stream,indent=2);stream.flush();os.fsync(stream.fileno())
    # Backup must exist durably before any write-mode packet is sent.
    for packet in write_packets(reader.indices[0x8080],target):
        exchange(short,long,packet,trace)
    for attempt in range(3):
        time.sleep(0.3)
        verified=read_profile(reader)
        if verified==target:
            return 'configured_and_readback_verified'
    raise OSError('Readback did not match the full target profile; stopped. Backup retained; no automatic restore')

def setup(library,apply,scope,output,restore=False):
    processes=subprocess.run(['tasklist','/FO','CSV','/NH'],capture_output=True,text=True,timeout=10,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
    if processes.returncode:
        raise OSError('Cannot verify Logitech software is closed; no profile access attempted')
    if any(('"'+name+'"') in processes.stdout.lower() for name in ('lghub.exe','lghub_agent.exe','lcore.exe')):
        raise OSError('Close G HUB and Logitech Gaming Software before profile access')
    sys.path.insert(0,str(library.resolve()))
    import hid
    devices=hid.enumerate(0x046D,0xC537)
    longs=[d for d in devices if d['usage_page']==0xFF00 and d['usage']==2]
    if len(longs)!=1:
        raise OSError('Expected exactly one accessible G602 receiver. Close G HUB and run this app locally; no settings changed')
    info=longs[0]
    shorts=[d for d in devices if d['usage_page']==0xFF00 and d['usage']==1 and d['path'].split(b'&Col')[0]==info['path'].split(b'&Col')[0]]
    if len(shorts)!=1:
        raise OSError('Matching receiver endpoint is unavailable')
    short,long=hid.device(),hid.device()
    result={'scope':scope,'apply_requested':apply,'write_trace':[]}
    try:
        short.open_path(shorts[0]['path']);long.open_path(info['path'])
        reader=Reader(long,short_device=short)
        protocol=reader.query(0,1,b'\0\0\xA5')
        if protocol[:3]!=b'\x02\0\xA5' or not reader.discover(0x8080):
            raise OSError('Expected HID++ 2 G602 legacy interface not found')
        current=read_profile(reader);target=proposed_profile(current,scope)
        if restore:target=BASELINE
        result.update(current_profile_hex=current.hex(),target_profile_hex=target.hex())
        if apply:
            backup=output.parent/('profile-backup-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')+'.json')
            result['backup_path']=str(backup)
            result['status']=transact(reader,short,long,current,target,backup,result['write_trace'])
            if restore:result['status']='original_profile_restored_and_verified'
        else:
            result['status']='verified_existing_setup' if current==target else 'setup_required'
        result['read_trace']=reader.trace
    except Exception as exc:
        result.update(status='failed',error=str(exc))
        raise
    finally:
        short.close();long.close()
        output.parent.mkdir(parents=True,exist_ok=True)
        output.write_text(json.dumps(result,indent=2),encoding='utf-8')
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--hid-library',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    p.add_argument('--scope',choices=['six','g7'],default='six')
    p.add_argument('--apply',action='store_true')
    p.add_argument('--restore',action='store_true',help='Explicitly restore only the reviewed original bindings; never automatic')
    args=p.parse_args()
    if args.restore and not args.apply:p.error('--restore requires --apply')
    mutex=None
    try:
        import ctypes
        kernel=ctypes.WinDLL('kernel32',use_last_error=True)
        kernel.CreateMutexW.argtypes=[ctypes.c_void_p,ctypes.c_bool,ctypes.c_wchar_p]
        kernel.CreateMutexW.restype=ctypes.c_void_p
        kernel.ReleaseMutex.argtypes=[ctypes.c_void_p]
        kernel.CloseHandle.argtypes=[ctypes.c_void_p]
        mutex=kernel.CreateMutexW(None,True,'Local\\G602ProfileSetup')
        if not mutex:raise OSError('Could not lock profile access')
        if ctypes.get_last_error()==183:
            kernel.CloseHandle(mutex);mutex=None
            raise OSError('Another profile setup is already running')
        result=setup(args.hid_library,args.apply,args.scope,args.output,args.restore)
        print(result['status'])
    except Exception as exc:
        args.output.parent.mkdir(parents=True,exist_ok=True)
        if not args.output.exists():args.output.write_text(json.dumps({'status':'failed','error':str(exc)}),encoding='utf-8')
        print(str(exc),file=sys.stderr);sys.exit(1)
    finally:
        if mutex:kernel.ReleaseMutex(mutex);kernel.CloseHandle(mutex)
