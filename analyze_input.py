"""Offline analysis of passive G602 vendor report 80; no input injection."""
import argparse
import json
from pathlib import Path

def transitions(previous, report):
    if len(report) != 5 or report[0] != 0x80:
        raise ValueError('Expected a five-byte G602 report 80')
    current = int.from_bytes(report[1:3], 'little')
    changed = current ^ previous
    return current, [{'bit': bit, 'mask': f'{1 << bit:04X}',
                       'action': 'down' if current & (1 << bit) else 'up'}
                      for bit in range(16) if changed & (1 << bit)]

def analyze(capture):
    result = {'capture_status': capture.get('status'), 'transitions': [],
              'physical_labels_verified': False,
              'label_basis': 'candidate labels from requested G4 through G9 order only',
              'suppression_verified': False, 'macro_playback_tested': False,
              'errors': list(capture.get('errors', []))}
    state = {}
    for event in capture.get('events', []):
        device = event.get('device', '')
        if event.get('kind') != 'hid' or 'VID_046D&PID_C537' not in device.upper() or '&COL06' not in device.upper():
            continue
        for raw in event.get('reports', []):
            report = bytes.fromhex(raw)
            if len(report) != 5 or report[0] != 0x80:
                continue
            current, changes = transitions(state.get(device, 0), report)
            state[device] = current
            for change in changes:
                change.update(elapsed_seconds=event['elapsed_seconds'], raw=raw)
                result['transitions'].append(change)
    downs = [e for e in result['transitions'] if e['action'] == 'down']
    result['observed_down_masks'] = [e['mask'] for e in downs]
    expected = [f'{1 << bit:04X}' for bit in range(3, 9)]
    # Additional left clicks can precede the requested sequence. Do not infer
    # physical identity from timing alone or turn candidates into verified labels.
    side = [e for e in downs if e['mask'] in expected]
    result['six_distinct_candidates_observed'] = {e['mask'] for e in side} == set(expected)
    result['candidate_order_matches_request'] = [e['mask'] for e in side] == expected
    result['candidate_mapping'] = {f'G{i+4}': expected[i] for i in range(6)} if result['candidate_order_matches_request'] else {}
    result['all_observed_buttons_released'] = all(value == 0 for value in state.values())
    result['usable_without_reconfiguration'] = 'six distinct passive signals observed; labeling and runtime behavior still need validation' if result['six_distinct_candidates_observed'] else 'not established'
    return result

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = analyze(json.loads(args.capture.read_text(encoding='utf-8')))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(result['usable_without_reconfiguration'])
