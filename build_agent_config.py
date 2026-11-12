"""Compile preserved macro events into the dry-run handler's portable config."""
import argparse
import json
from pathlib import Path

def compile_config(analysis):
    buttons = {}
    for profile in analysis['profiles']:
        for macro in profile['macros']:
            slot = macro['slot']
            if slot not in [f'g602_g{i}_m1' for i in range(4,10)]:
                continue
            button = int(slot.split('_')[1][1:])
            if str(button) in buttons:
                raise ValueError('Ambiguous duplicate macro for ' + slot)
            source = macro['original_macro']
            seq = source['sequence']
            if not seq.get('useSimpleActions'):
                raise ValueError('Only verified simple event sequences are supported')
            if any(seq.get(key) for key in ('heldSequence','pressSequence','releaseSequence','toggleSequence')):
                raise ValueError('Additional sequence sections require explicit support')
            events, held = [], set()
            for component in seq['simpleSequence']['components']:
                if set(component) == {'keyboard'}:
                    key = component['keyboard']
                    usage = int(key['hidUsage'])
                    down = bool(key.get('isDown', False))
                    if not 0 <= usage <= 255:
                        raise ValueError('Invalid keyboard HID usage')
                    if down:
                        if usage in held:
                            raise ValueError('Duplicate key down')
                        held.add(usage)
                    else:
                        if usage not in held:
                            raise ValueError('Unmatched key release')
                        held.remove(usage)
                    events.append({'kind':'key','usage':usage,'down':down})
                elif set(component) == {'delay'}:
                    delay = component['delay']['durationMs']
                    if not isinstance(delay,int) or not 0 <= delay <= 10000:
                        raise ValueError('Invalid explicit delay')
                    events.append({'kind':'delay','milliseconds':delay})
                else:
                    raise ValueError('Unsupported macro component; refusing to drop it')
            if held:
                raise ValueError('Macro ends with held keys')
            buttons[str(button)] = {'name':macro['name'], 'mask':1 << (button-1),
                'events':events, 'original_macro':source,
                'default_delay_ms':seq.get('defaultDelay'),
                'use_default_delay':seq.get('useDefaultDelay',False),
                'timing_status':'explicit delays preserved; default-delay playback semantics unverified',
                'warnings':macro.get('warnings',[])}
    if set(buttons) != {str(i) for i in range(4,10)}:
        raise ValueError('Exactly six G4–G9 sequences are required')
    return {'schema_version':1, 'mode':'dry-run-only',
            'mapping_status':'candidate masks inferred from ordered physical capture',
            'source_database_sha256':analysis['database_sha256'], 'buttons':buttons}

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('analysis',type=Path)
    parser.add_argument('--output',type=Path,required=True)
    args = parser.parse_args()
    config = compile_config(json.loads(args.analysis.read_text(encoding='utf-8')))
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(config,indent=2),encoding='utf-8')
    print('Compiled six exact event sequences; playback disabled')
