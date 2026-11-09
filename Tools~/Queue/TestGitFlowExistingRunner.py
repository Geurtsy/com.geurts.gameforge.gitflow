"""Real Git provenance + owned protocol fixture checks; no live queue actions."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import GitFlowExistingRunner as adapter

RUNNER='''import argparse,json\nfrom pathlib import Path\ndef processor_status(): pass\ndef start_processor(): pass\ndef stop_processor(): pass\ndef processor_running(): pass\np=argparse.ArgumentParser();p.add_argument('--repo');p.add_argument('action');p.add_argument('--resume',action='store_true');p.add_argument('--operator',action='store_true');a=p.parse_args()\nc=Path(a.repo)/'.git/integration_processor';s=c/'processor-state.json';v=json.loads(s.read_text())\nif a.action=='processor-start':\n v['running']=True;v['status']='running';v['last_arguments']=[a.resume,a.operator];s.write_text(json.dumps(v))\nelif a.action=='processor-stop':\n (c/'stop').write_text('owned protocol fixture graceful stop')\nprint(json.dumps(v))\n'''

class ExistingRunnerAdapterTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='ggf-existing-');self.base=Path(self.temp.name);self.repo=self.base/'repo';self.repo.mkdir()
        def git(*args):subprocess.run(['git','-C',str(self.repo),*args],check=True,capture_output=True)
        self.git=git;git('init','--quiet');git('config','user.email','fixture@example.invalid');git('config','user.name','Owned fixture')
        self.source=self.repo/'Tools/IntegrationQueue/integration_queue.py';self.source.parent.mkdir(parents=True);self.source.write_text(RUNNER)
        git('add','.');git('commit','--quiet','-m','Owned protocol fixture');self.workspace=self.base/'workspace';git('worktree','add','--detach',str(self.workspace))
        self.control=self.repo/'.git/integration_processor';self.control.mkdir();self.config=self.control/'config.json';self.config.write_text(json.dumps(dict(repo=str(self.repo),runner=str(self.source),integration_root=str(self.workspace),codex_repair=True)))
        self.state=self.control/'processor-state.json';self.state.write_text(json.dumps(dict(running=False,status='stopped',pid=1,phase='Owned protocol fixture')))
        self.record=self.base/'private/adoption.json'
    def tearDown(self):self.temp.cleanup()
    def adopt(self):
        observation=adapter.inspect(self.repo,sys.executable)
        adapter.adopt(self.repo,sys.executable,self.record,observation['identity']['scopeFingerprint'])
    def test_readonly_inspection_then_fresh_adoption_preserves_existing_config(self):
        before=self.config.read_bytes();self.adopt();self.assertEqual(before,self.config.read_bytes());self.assertEqual(adapter.dispatch(self.record,'status')['owner']['running'],False)
        self.assertFalse((self.repo/'.git/gameforge_queue').exists());self.assertFalse(list(self.source.parent.glob('__pycache__')))
    def test_active_owner_and_changed_confirmation_refused_without_record(self):
        observation=adapter.inspect(self.repo,sys.executable)
        with self.assertRaisesRegex(ValueError,'scope changed'):adapter.adopt(self.repo,sys.executable,self.record,'0'*64)
        self.state.write_text(json.dumps(dict(running=True)))
        with self.assertRaisesRegex(ValueError,'publisher is active'):adapter.adopt(self.repo,sys.executable,self.record,observation['identity']['scopeFingerprint'])
        self.assertFalse(self.record.exists())
    def test_modified_config_source_and_adoption_are_preserved_and_refused(self):
        self.adopt();original=self.config.read_bytes();self.config.write_bytes(original+b' ')
        with self.assertRaisesRegex(ValueError,'changed'):adapter.dispatch(self.record,'status')
        self.config.write_bytes(original);self.source.write_text(RUNNER+'#custom\n')
        with self.assertRaisesRegex(ValueError,'changed/untracked'):adapter.dispatch(self.record,'status')
        self.source.write_text(RUNNER);value=json.loads(self.record.read_text());value['python']='unreviewed';self.record.write_text(json.dumps(value))
        with self.assertRaisesRegex(ValueError,'Modified adoption'):adapter.dispatch(self.record,'status')
    def test_resume_delegates_exact_supported_flags_and_job_starts_are_blocked(self):
        self.adopt()
        with self.assertRaisesRegex(ValueError,'operator'):adapter.dispatch(self.record,'resume')
        with patch.object(adapter,'enclosing_kill_job',return_value=True):
            with self.assertRaisesRegex(ValueError,'Independent host'):adapter.dispatch(self.record,'resume',True)
        self.assertFalse(json.loads(self.state.read_text())['running'])
        with patch.object(adapter,'enclosing_kill_job',return_value=False):result=adapter.dispatch(self.record,'resume',True)
        self.assertEqual(result['owner']['running'],True);self.assertEqual(json.loads(self.state.read_text())['last_arguments'],[True,True])
        adapter.dispatch(self.record,'request-stop',True);self.assertTrue((self.control/'stop').is_file())
    def test_modern_coexistence_and_untracked_source_fail_closed(self):
        modern=self.repo/'.git/gameforge_queue';modern.mkdir();(modern/'configuration.json').write_text('{}')
        with self.assertRaisesRegex(ValueError,'modern'):adapter.inspect(self.repo,sys.executable)
        (modern/'configuration.json').unlink();(self.source.parent/'untracked.py').write_text('# unowned')
        with self.assertRaisesRegex(ValueError,'changed/untracked'):adapter.inspect(self.repo,sys.executable)
    def test_host_request_refuses_record_for_different_selected_repository(self):
        self.adopt();other=self.base/'different-selected-project';other.mkdir()
        with self.assertRaisesRegex(ValueError,'Selected repository'):
            adapter.request_host(self.record,self.base/'no-host','resume',True,expected_repository=other)
        self.assertFalse((self.base/'no-host').exists())
    def test_host_request_preserves_selected_target_in_complete_protocol_message(self):
        self.adopt();host=self.base/'host';host.mkdir()
        with patch.object(adapter,'native_host_ready',return_value=host):
            result=adapter.request_host(self.record,host,'resume',True,expected_repository=self.repo)
        value=json.loads((host/'control-requests'/(result['requestId']+'.json')).read_text())
        self.assertEqual(value['repository'],str(self.repo));self.assertEqual(value['action'],'resume')
        self.assertEqual(value['runner'],str(self.source));self.assertEqual(result['state'],'host_request_dispatched')
        self.assertFalse((host/'control-receipts').exists())
    def test_unrelated_committed_game_change_does_not_rebind_runner(self):
        self.adopt();(self.repo/'game.txt').write_text('unrelated');self.git('add','game.txt');self.git('commit','--quiet','-m','Unrelated game change')
        self.assertEqual(adapter.dispatch(self.record,'status')['owner']['running'],False)

if __name__=='__main__':unittest.main(verbosity=2)
