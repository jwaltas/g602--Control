"""Offline capability assessment. Never opens HID or emits device commands."""
import argparse
import json
from pathlib import Path

SOURCES = {
    'g602_documented_assignments': 'https://support.logi.com/hc/en-ch/articles/360023258354-Configuring-G602-gaming-mouse-on-board-memory',
    'g602_legacy_research': 'https://github.com/libratbag/libratbag/pull/1837',
    'modern_macro_work': 'https://github.com/libratbag/libratbag/pull/1850',
}

def decode_profile(raw_hex):
    raw = bytes.fromhex(raw_hex)
    if len(raw) != 45:
        raise ValueError('Legacy profile must contain exactly 45 bytes')
    slots = []
    for offset in range(12, 45, 3):
        binding = raw[offset:offset+3]
        item = {'index': len(slots), 'raw': binding.hex(), 'physical_button': 'unverified'}
        if binding == bytes(3):
            item['kind'] = 'empty_or_disabled'
        elif binding[:2] == bytes(2):
            item.update(kind='single_key', hid_usage=binding[2])
        elif binding[1:] == bytes(2):
            item.update(kind='action', action_code=binding[0])
        else:
            item['kind'] = 'uninterpreted'
        slots.append(item)
    return {'raw': raw.hex(), 'slots': slots,
            'sequence_encoding': 'not established; unknown bindings remain uninterpreted'}

def assess(hardware, settings):
    probes = [p for p in hardware.get('probes', []) if p.get('result')]
    if len(probes) != 1:
        raise ValueError('Assessment requires exactly one successful hardware probe')
    device = probes[0]['result']
    features = {f['id'].upper() for f in device.get('features', [])}
    legacy = device.get('legacy_onboard', {})
    result = {'hardware_capture_timestamp': hardware.get('timestamp'),
        'sources': SOURCES, 'feature_8080_present': '8080' in features,
        'feature_8100_present': '8100' in features,
        'documented_g602_assignments': ['mouse function', 'keystroke with modifiers'],
        'conclusion': 'No verified onboard sequence encoder; no device write is justified',
        'firmware_impossibility_proven': False,
        'modern_macro_compatibility': 'unverified even if feature 8100 is present',
        'macros': []}
    if '8100' not in features:
        result['modern_macro_compatibility'] = 'required modern 8100 interface is absent'
    if legacy.get('raw_profile_hex'):
        result['decoded_profile'] = decode_profile(legacy['raw_profile_hex'])
    for profile in settings.get('profiles', []):
        for macro in profile.get('macros', []):
            original = macro['original_macro']
            components = original.get('sequence', {}).get('simpleSequence', {}).get('components', [])
            keydowns = [c['keyboard'] for c in components
                        if c.get('keyboard', {}).get('isDown', False)]
            nonmodifiers = [k for k in keydowns if not 224 <= int(k['hidUsage']) <= 231]
            reason = ('Multiple ordinary key presses require ordered sequence playback'
                      if len(nonmodifiers) > 1 else
                      'Exact events require separate validation; no automatic simplification')
            result['macros'].append({'slot': macro['slot'], 'name': macro['name'],
                'key_down_order': macro['key_down_order'], 'reason': reason,
                'onboard_status': 'no verified encoding',
                'event_count': len(components),
                'explicit_delay_ms': sum(c.get('delay', {}).get('durationMs', 0) for c in components),
                'warnings': macro['warnings'],
                'onboardable_metadata': original.get('onboardable'),
                'original_macro': original})
    result['fallback'] = {
        'candidate': 'native event-driven agent, preferably active only with Rhino',
        'status': 'not implemented; no runtime or startup entry installed',
        'prerequisites': [
            'Confirm distinguishable input from all six physical buttons without enabling diversion',
            'Confirm input suppression so fallback digits do not reach Rhino alongside the macro',
            'Preserve exact key-down/key-up order and delays; release held keys on cancellation',
            'Check actual Rhino 8 playback and measure idle resource use'],
        'constraint': 'No device reconfiguration is authorized by this assessment'}
    return result

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('hardware', type=Path)
    parser.add_argument('settings', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = assess(json.loads(args.hardware.read_text(encoding='utf-8')),
                    json.loads(args.settings.read_text(encoding='utf-8')))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(result['conclusion'])
    print(len(result['macros']), 'original macro sequences preserved')
