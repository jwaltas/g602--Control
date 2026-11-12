import struct
import unittest
from capture_input import decode_payload, is_target
from analyze_input import transitions, analyze

class RawInputTests(unittest.TestCase):
    def test_mouse_motion_is_discarded(self):
        raw = bytearray(24)
        struct.pack_into('<ii', raw, 12, 42, -10)
        self.assertIsNone(decode_payload(0, raw))

    def test_side_button_transition_is_kept(self):
        raw = bytearray(24)
        struct.pack_into('<HH', raw, 4, 0x40, 0)
        self.assertEqual(decode_payload(0, raw)['button_flags'], 0x40)

    def test_keyboard_down_and_up(self):
        for flags in (0, 1):
            raw = struct.pack('<HHHHII', 2, flags, 0, 0x31, 0x100+flags, 0)
            result = decode_payload(1, raw)
            self.assertEqual(result['virtual_key'], 0x31)
            self.assertEqual(result['is_up'], bool(flags))

    def test_hid_batch_preserved(self):
        result = decode_payload(2, struct.pack('<II', 2, 2)+b'\x10\x20\x30\x40')
        self.assertEqual(result['reports'], ['1020', '3040'])

    def test_truncated_packets_rejected(self):
        for kind, raw in [(0, bytes(23)), (1, bytes(15)), (2, bytes(7)),
                          (2, struct.pack('<II', 20, 2)+bytes(20))]:
            with self.subTest(kind=kind), self.assertRaises(ValueError):
                decode_payload(kind, raw)

    def test_other_keyboard_filtered(self):
        self.assertFalse(is_target('HID#VID_046D&PID_C232'))
        self.assertTrue(is_target('hid#vid_046d&pid_c537&mi_01'))

    def test_report80_high_byte_and_release(self):
        state, edges = transitions(0, bytes.fromhex('8000018000'))
        self.assertEqual(state, 0x100)
        self.assertEqual(edges, [{'bit': 8, 'mask': '0100', 'action': 'down'}])
        self.assertEqual(transitions(state, bytes.fromhex('8000018000'))[1], [])
        self.assertEqual(transitions(state, bytes.fromhex('8000008000'))[1][0]['action'], 'up')

    def test_passive_sequence_does_not_claim_verified_physical_labels(self):
        events = []
        for i, mask in enumerate((8,16,32,64,128,256)):
            for value in (mask, 0):
                events.append({'kind': 'hid', 'device': 'HID#VID_046D&PID_C537&MI_01&Col06',
                    'elapsed_seconds': i, 'reports': [(b'\x80'+value.to_bytes(2,'little')+b'\x80\0').hex()]})
        result = analyze({'status':'capture_complete','events':events})
        self.assertTrue(result['six_distinct_candidates_observed'])
        self.assertTrue(result['candidate_order_matches_request'])
        self.assertFalse(result['physical_labels_verified'])
        self.assertFalse(result['suppression_verified'])

if __name__ == '__main__':
    unittest.main()
