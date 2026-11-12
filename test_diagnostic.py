"""Offline safety checks: forbidden commands must fail before any HID I/O."""
import unittest
import json
from pathlib import Path
from diagnose import Reader

class NoIO:
    def write(self, packet):
        raise AssertionError('Forbidden command reached hardware transport')

class SafetyTests(unittest.TestCase):
    def setUp(self):
        self.reader = Reader(NoIO(), short_device=NoIO())
        self.reader.indices.update({0x8080: 14, 0x8100: 15})

    def test_all_legacy_write_and_commit_functions_rejected(self):
        for function in set(range(16)) - {2, 3, 5}:
            with self.subTest(function=function), self.assertRaises(ValueError):
                self.reader.query(0x8080, function)

    def test_legacy_write_mode_and_chunk_payload_rejected(self):
        for payload in (b'\x81\x2a\0', b'', b'\x80\x2a\0\0'):
            with self.assertRaises(ValueError):
                self.reader.query(0x8080, 2, payload)
        self.reader.legacy_read_started = True
        for payload in (bytes(16), b'\x04\0\0', b'\x01\x02\0', b'\x01'):
            with self.assertRaises(ValueError):
                self.reader.query(0x8080, 3, payload)

    def test_chunk_requires_read_session(self):
        with self.assertRaises(ValueError):
            self.reader.query(0x8080, 3, b'\x01\0\0')

    def test_modern_setters_and_reset_rejected(self):
        for function in (1, 3, 6, 7, 8, 12):
            with self.assertRaises(ValueError):
                self.reader.query(0x8100, function)
        with self.assertRaises(ValueError):
            self.reader.query(0x0020)

    def test_protocol_challenge_reply(self):
        class Fake:
            def write(self, packet):
                self.packet = packet
                return len(packet)
            def read(self, size, timeout):
                return list(bytes([0x11, 1, 0, 0x1D, 2, 0, 0xA5]).ljust(20,b'\0'))
        fake = Fake()
        reply = Reader(fake).query(0, 1, b'\0\0\xA5')
        self.assertEqual(reply[:3], b'\x02\0\xA5')

    def test_replay_real_windows_capture(self):
        # Recorded hardware responses exercise decoding and both HID endpoints
        # without touching a physical device during tests.
        rows = json.loads(Path(__file__).with_name('test_capture.json').read_text())
        class Replay:
            def __init__(self):
                self.position = 0
                self.pending = None
            def write(inner, packet):
                row = rows[inner.position]
                self.assertEqual(packet.hex(), row['tx'])
                inner.pending = bytes.fromhex(row['rx'])
                inner.position += 1
                return len(packet)
            def read(inner, size, timeout):
                reply, inner.pending = inner.pending, None
                return list(reply or b'')
        replay = Replay()
        result = Reader(replay, short_device=replay).collect()
        self.assertEqual(result['protocol'], '2.0')
        self.assertEqual(len(result['features']), 22)
        self.assertEqual(result['firmware'][0]['prefix'], 'RQM')
        self.assertEqual(result['legacy_onboard']['dpi_stages'], [500,1000,1500,2000,2500])
        self.assertEqual(result['legacy_onboard']['binding_slots'][5:8], ['00001e','00001f','000020'])
        self.assertEqual(replay.position, len(rows))

if __name__ == '__main__':
    unittest.main()
