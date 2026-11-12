import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from setup_profile import BASELINE, proposed_profile, write_packets, transact

class Reader:
    indices={0x8080:14}

class SetupTests(unittest.TestCase):
    def test_only_six_binding_slots_change(self):
        target=proposed_profile(BASELINE)
        self.assertEqual(target[:18],BASELINE[:18])
        self.assertEqual(target[36:],BASELINE[36:])
        for i in range(6):self.assertEqual(target[18+i*3:21+i*3],bytes([0,0,0x68+i]))

    def test_changed_baseline_is_refused(self):
        changed=bytearray(BASELINE);changed[4]+=1
        with self.assertRaises(ValueError):proposed_profile(bytes(changed))

    def test_packet_builder_has_only_reviewed_transaction(self):
        target=proposed_profile(BASELINE);packets=write_packets(14,target)
        self.assertEqual([len(p) for p in packets],[7,20,20,20,7])
        self.assertEqual(packets[0].hex(),'10010e2d812a00')
        self.assertEqual(packets[-1].hex(),'10010e4d820100')
        self.assertEqual(b''.join(p[5:] for p in packets[1:4]),target)
        with self.assertRaises(ValueError):write_packets(14,bytes(45))

    def test_failed_backup_prevents_first_write(self):
        with tempfile.TemporaryDirectory() as folder:
            backup=Path(folder)/'backup.json';backup.write_text('existing')
            with patch('setup_profile.exchange') as exchange:
                with self.assertRaises(FileExistsError):transact(Reader(),None,None,BASELINE,proposed_profile(BASELINE),backup,[])
                exchange.assert_not_called()

    def test_success_requires_complete_readback_and_backup_first(self):
        with tempfile.TemporaryDirectory() as folder:
            backup=Path(folder)/'backup.json';target=proposed_profile(BASELINE);sent=[]
            def send(s,l,packet,trace):
                self.assertTrue(backup.exists());sent.append(packet)
            with patch('setup_profile.exchange',side_effect=send),patch('setup_profile.read_profile',return_value=target),patch('setup_profile.time.sleep'):
                status=transact(Reader(),None,None,BASELINE,target,backup,[])
            self.assertEqual(status,'configured_and_readback_verified');self.assertEqual(len(sent),5)

    def test_transfer_failure_never_commits_or_resets(self):
        with tempfile.TemporaryDirectory() as folder:
            sent=[]
            def fail(s,l,packet,trace):
                sent.append(packet)
                if len(sent)==2:raise TimeoutError('simulated')
            with patch('setup_profile.exchange',side_effect=fail):
                with self.assertRaises(TimeoutError):transact(Reader(),None,None,BASELINE,proposed_profile(BASELINE),Path(folder)/'b.json',[])
            self.assertEqual(len(sent),2)
            self.assertFalse(any(p[3]==0x4D for p in sent))

    def test_readback_mismatch_does_not_trigger_more_writes(self):
        with tempfile.TemporaryDirectory() as folder:
            with patch('setup_profile.exchange') as send,patch('setup_profile.read_profile',return_value=BASELINE) as read,patch('setup_profile.time.sleep'):
                with self.assertRaises(OSError):transact(Reader(),None,None,BASELINE,proposed_profile(BASELINE),Path(folder)/'b.json',[])
                self.assertEqual(send.call_count,5);self.assertEqual(read.call_count,3)

    def test_existing_target_needs_no_write(self):
        target=proposed_profile(BASELINE)
        with patch('setup_profile.exchange') as send:
            self.assertEqual(transact(Reader(),None,None,target,target,Path('unused'),[]),'already_configured')
            send.assert_not_called()

    def test_explicit_restore_preserves_entire_baseline(self):
        with tempfile.TemporaryDirectory() as folder:
            target=proposed_profile(BASELINE)
            with patch('setup_profile.exchange') as send,patch('setup_profile.read_profile',return_value=BASELINE),patch('setup_profile.time.sleep'):
                status=transact(Reader(),None,None,target,BASELINE,Path(folder)/'backup.json',[])
                self.assertEqual(status,'configured_and_readback_verified')
                packets=[call.args[2] for call in send.call_args_list]
                self.assertEqual(b''.join(p[5:] for p in packets[1:4]),BASELINE)

if __name__=='__main__':unittest.main()
