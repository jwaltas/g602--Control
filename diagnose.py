"""G602 read-only HID++ diagnostic. No configuration setters exist in this tool."""
import argparse
import datetime
import json
from pathlib import Path
import sys
import subprocess
import time

# Function numbers are specific to each feature, never globally safe.
READS = {0x0000: {0, 1}, 0x0001: {0, 1}, 0x0003: {0, 1},
         0x0005: {0, 1, 2}, 0x8100: {0, 2, 4, 5, 11}, 0x8080: {2, 3, 5}}

def receiver_firmware(device):
    """Fixed HID++ 1.0 get-register F1 requests; no caller-provided command."""
    result = {'trace': []}
    for selector, label in ((1, 'version'), (2, 'build'), (4, 'bootloader')):
        packet = bytes([0x10, 0xFF, 0x81, 0xF1, selector, 0, 0])
        entry = {'tx': packet.hex()}
        result['trace'].append(entry)
        if device.write(packet) != 7:
            entry['error'] = 'incomplete request'
            break
        deadline = time.monotonic() + 1
        while time.monotonic() < deadline:
            r = bytes(device.read(64, 100))
            if len(r) >= 7 and r[1:5] == packet[1:5]:
                entry['rx'] = r.hex()
                result[label] = r[5:7].hex().upper()
                break
            if len(r) >= 6 and r[1] == 0xFF and r[2] == 0x8F and r[3:5] == packet[2:4]:
                entry['error_reply'] = r.hex()
                break
        else:
            entry['error'] = 'timeout; firmware unavailable'
    return result

class Reader:
    def __init__(self, device, report_id=0x11, short_device=None):
        self.device = device
        self.report_id = report_id
        self.indices = {0: 0}
        self.trace = []
        self.short_device = short_device
        self.legacy_read_started = False

    def query(self, feature, function=0, params=b''):
        if function not in READS.get(feature, set()):
            raise ValueError('Command is not an allowlisted read')
        if feature not in self.indices:
            raise ValueError('Feature has not been discovered')
        if feature == 0x8080:
            if function == 2 and params != b'\x80\x2a\0':
                raise ValueError('Only legacy read mode is permitted')
            if function == 3 and (not self.legacy_read_started or params not in (b'\x01\0\0', b'\x02\0\0', b'\x03\0\0')):
                raise ValueError('Only short read-chunk requests are permitted')
            if function == 5 and params:
                raise ValueError('Live DPI read takes no payload')
        index = self.indices[feature]
        legacy = feature == 0x8080 and function in (2, 3)
        if legacy and self.short_device is None:
            raise ValueError('Legacy reads require the short-report endpoint')
        report_id = 0x10 if legacy else self.report_id
        size = 20 if report_id == 0x11 else 7
        if len(params) > size - 4:
            raise ValueError('Request exceeds report size')
        packet = bytes([report_id, 1, index, function * 16 + 0xD]) + params
        packet = packet.ljust(size, b'\0')
        entry = {'feature': f'{feature:04X}', 'function': function, 'tx': packet.hex()}
        self.trace.append(entry)
        writer = self.short_device if legacy else self.device
        if writer.write(packet) != size:
            raise OSError('Incomplete HID request')
        deadline = time.monotonic() + 1
        while time.monotonic() < deadline:
            reply = bytes(self.device.read(64, 100))
            if len(reply) < 4 or reply[1] != 1:
                continue
            if reply[2:4] == packet[2:4]:
                entry['rx'] = reply.hex()
                if feature == 0x8080 and function == 2:
                    self.legacy_read_started = True
                return reply[4:]
            if (reply[2] in (0x8F, 0xFF) and len(reply) >= 6
                    and reply[3:5] == packet[2:4]):
                entry['error_reply'] = reply.hex()
                raise OSError('HID++ error: ' + reply.hex())
        entry['error'] = 'timeout'
        raise TimeoutError('No matching HID++ response')

    def discover(self, feature):
        r = self.query(0, 0, feature.to_bytes(2, 'big'))
        if r[0]:
            self.indices[feature] = r[0]
        return r[0]

    def collect(self):
        result = {}
        r = self.query(0, 1, b'\0\0\xA5')
        if len(r) < 3 or r[0] < 2 or r[2] != 0xA5:
            raise ValueError('HID++ 2 protocol challenge failed')
        result['protocol'] = f'{r[0]}.{r[1]}'
        if self.discover(1):
            count = self.query(1)[0]
            result['features'] = []
            for i in range(1, count + 1):
                r = self.query(1, 1, bytes([i]))
                fid = int.from_bytes(r[:2], 'big')
                self.indices[fid] = i
                result['features'].append({'index': i, 'id': f'{fid:04X}', 'flags': r[2], 'version': r[3] if len(r)>3 else None})
        if self.discover(3):
            result['firmware'] = []
            for i in range(min(self.query(3)[0], 16)):
                r = self.query(3, 1, bytes([i]))
                result['firmware'].append({'raw': r.hex(), 'kind': r[0] & 15,
                    'prefix': r[1:4].decode('ascii', errors='replace'),
                    'version': f'{r[4]:02X}.{r[5]:02X}', 'build': r[6:8].hex()})
        if self.discover(5):
            length = self.query(5)[0]
            name = b''
            while len(name) < length:
                fragment = self.query(5, 1, bytes([len(name)]))
                if not fragment:
                    break
                name += fragment[:length-len(name)]
            result['name'] = name.decode(errors='replace')
        if self.discover(0x8100):
            result['onboard'] = {}
            for name, fn in [('descriptor',0), ('mode',2), ('current_profile',4), ('dpi_index',11)]:
                try:
                    result['onboard'][name] = self.query(0x8100, fn).hex()
                except (OSError, TimeoutError) as exc:
                    result['onboard'][name] = {'error': str(exc)}
        else:
            result['onboard'] = {'feature_8100': 'not advertised; does not prove no onboard capability'}
        if self.discover(0x8080):
            legacy = result['legacy_onboard'] = {}
            try:
                self.query(0x8080, 2, b'\x80\x2a\0')
                chunks = []
                for i in (1, 2, 3):
                    r = self.query(0x8080, 3, bytes([i, 0, 0]))
                    if len(r) != 16 or r[0] != i:
                        raise ValueError('Invalid legacy chunk response')
                    chunks.append(r[1:])
                profile = b''.join(chunks)
                legacy.update(raw_profile_hex=profile.hex(),
                    report_interval_ms=profile[1], stored_dpi_stage=profile[3],
                    dpi_stages=[v * 250 for v in profile[4:9]],
                    binding_slots=[profile[i:i+3].hex() for i in range(12,45,3)])
                legacy['live_dpi_stage'] = self.query(0x8080, 5)[0]
            except (OSError, TimeoutError, ValueError) as exc:
                legacy['error'] = str(exc)
        return result

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--hid-library', type=Path)
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args()
    if args.hid_library:
        sys.path.insert(0, str(args.hid_library.resolve()))
    import hid
    devices = hid.enumerate(0x046D, 0)
    report = {'timestamp': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'policy': 'read-only allowlisted queries; mouse index 1; receiver firmware F1 getters; C537 only',
              'devices': devices, 'probes': []}
    try:
        tasks = subprocess.run(['tasklist', '/FO', 'CSV', '/NH'], capture_output=True, text=True, timeout=10, creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
        report['relevant_processes'] = [line for line in tasks.stdout.splitlines() if any(s in line.lower() for s in ('lghub', 'rhino', 'lcore'))]
        report['process_inventory_error'] = tasks.stderr.strip() or None
    except (OSError, subprocess.TimeoutExpired) as exc:
        report['process_inventory_error'] = str(exc)
    for info in sorted(devices, key=lambda x: x['usage'] != 2):
        if info['product_id'] != 0xC537 or info['usage_page'] != 0xFF00 or info['usage'] not in (1, 2):
            continue
        probe = {'path': info['path'], 'usage': info['usage']}
        report['probes'].append(probe)
        d = hid.device()
        short = None
        reader = Reader(d, 0x11 if info['usage'] == 2 else 0x10)
        try:
            d.open_path(info['path'])
            short_info = next((x for x in devices if x['product_id'] == 0xC537 and x['usage_page'] == 0xFF00 and x['usage'] == 1 and x['path'].split(b'&Col')[0] == info['path'].split(b'&Col')[0]), None)
            if short_info and info['usage'] == 2:
                short = hid.device()
                short.open_path(short_info['path'])
                reader.short_device = short
            probe['result'] = reader.collect()
            if short:
                probe['receiver_firmware'] = receiver_firmware(short)
        except Exception as exc:
            probe['error'] = f'{type(exc).__name__}: {exc}'
        finally:
            probe['trace'] = reader.trace
            d.close()
            if short:
                short.close()
        if 'result' in probe:
            break
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, default=lambda v: v.decode(errors='replace') if isinstance(v,bytes) else str(v)), encoding='utf-8')
    print(json.dumps(report['probes'], indent=2, default=str))

if __name__ == '__main__':
    main()
