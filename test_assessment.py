import copy
import unittest
from assess_onboard import assess, decode_profile

class AssessmentTests(unittest.TestCase):
    def inputs(self, features=('8080',)):
        hardware = {'probes': [{'result': {'features': [{'id': f} for f in features]}}]}
        macro = {'slot': 'g602_g4_m1', 'name': 'line', 'key_down_order': ['L','Enter'],
                 'warnings': [], 'original_macro': {'onboardable': True, 'sequence': {
                     'simpleSequence': {'components': [
                         {'keyboard': {'hidUsage': '15', 'isDown': True}},
                         {'delay': {'durationMs': 25}},
                         {'keyboard': {'hidUsage': '15'}},
                         {'keyboard': {'hidUsage': '40', 'isDown': True}},
                         {'keyboard': {'hidUsage': '40'}}]}}}}
        return hardware, {'profiles': [{'macros': [macro]}]}

    def test_onboardable_does_not_override_missing_encoder(self):
        hardware, settings = self.inputs()
        before = copy.deepcopy(settings)
        result = assess(hardware, settings)
        self.assertEqual(result['macros'][0]['onboard_status'], 'no verified encoding')
        self.assertFalse(result['firmware_impossibility_proven'])
        self.assertEqual(settings, before)
        self.assertEqual(result['macros'][0]['original_macro'], settings['profiles'][0]['macros'][0]['original_macro'])

    def test_modern_feature_alone_does_not_prove_compatibility(self):
        result = assess(*self.inputs(('8100',)))
        self.assertTrue(result['feature_8100_present'])
        self.assertIn('unverified', result['modern_macro_compatibility'])

    def test_unread_hardware_cannot_be_assessed(self):
        for hardware in ({}, {'probes': [{'error': 'timeout'}]}):
            with self.assertRaises(ValueError):
                assess(hardware, {'profiles': []})

    def test_unknown_binding_is_not_reinterpreted_as_sequence(self):
        raw = bytearray(45)
        raw[12:15] = b'\xff\x12\x34'
        result = decode_profile(raw.hex())
        self.assertEqual(result['slots'][0]['kind'], 'uninterpreted')
        self.assertEqual(result['slots'][0]['raw'], 'ff1234')
        self.assertEqual(result['raw'], raw.hex())

    def test_incomplete_profile_rejected(self):
        with self.assertRaises(ValueError):
            decode_profile('00' * 44)

if __name__ == '__main__':
    unittest.main()
