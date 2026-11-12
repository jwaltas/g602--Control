import copy
import json
from pathlib import Path
import unittest
from build_agent_config import compile_config

class ConfigTests(unittest.TestCase):
    def fixture(self):
        macros=[]
        for button in range(4,10):
            macros.append({'slot':f'g602_g{button}_m1','name':str(button),'original_macro':{'sequence':{
                'useSimpleActions':True,'useDefaultDelay':True,'defaultDelay':50,
                'simpleSequence':{'components':[
                    {'keyboard':{'hidUsage':'15','isDown':True}},
                    {'delay':{'durationMs':1}},
                    {'keyboard':{'hidUsage':'12','isDown':True}},
                    {'keyboard':{'hidUsage':'15'}},
                    {'keyboard':{'hidUsage':'12'}}]}}}})
        return {'database_sha256':'fixture','profiles':[{'macros':macros}]}

    def test_overlap_delay_and_default_flag_preserved(self):
        data=self.fixture(); original=copy.deepcopy(data)
        config=compile_config(data)
        macro=config['buttons']['4']
        self.assertEqual([e.get('usage') for e in macro['events'] if e['kind']=='key'],[15,12,15,12])
        self.assertEqual(macro['events'][1]['milliseconds'],1)
        self.assertEqual(macro['default_delay_ms'],50)
        self.assertTrue(macro['use_default_delay'])
        self.assertEqual(data,original)

    def test_unsupported_events_not_silently_dropped(self):
        data=self.fixture()
        data['profiles'][0]['macros'][0]['original_macro']['sequence']['simpleSequence']['components'].append({'mouse':{}})
        with self.assertRaises(ValueError): compile_config(data)

    def test_incomplete_or_ambiguous_six_button_config_rejected(self):
        data=self.fixture();data['profiles'][0]['macros'].pop()
        with self.assertRaises(ValueError): compile_config(data)
        data=self.fixture();data['profiles'][0]['macros'].append(copy.deepcopy(data['profiles'][0]['macros'][0]))
        with self.assertRaises(ValueError): compile_config(data)

    def test_unbalanced_key_sequence_rejected(self):
        data=self.fixture()
        data['profiles'][0]['macros'][0]['original_macro']['sequence']['simpleSequence']['components'].pop()
        with self.assertRaises(ValueError): compile_config(data)

if __name__=='__main__': unittest.main()
