"""Real-Git recovery boundaries; simulated native output is complemented by a real Unity smoke."""
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

import GitFlowQueue as q
import GitFlowFolderMetadata as metadata


# These GUIDs belong only to disposable test artifacts, never an actual project.
def sidecar(guid):
    return ('fileFormatVersion: 2\nguid: ' + guid + '\n').encode()


NATIVE_FIXTURE = '''from pathlib import Path
import json,os,sys
sys.path.insert(0,SOURCE)
import GitFlowQueue as q
import GitFlowFolderMetadata as metadata
root=Path.cwd();evidence=Path(sys.argv[1]);candidate=sys.argv[2];run=sys.argv[3]
editor=Path(sys.argv[sys.argv.index('--unity')+1])
observation=metadata.begin_native(q,root/'Game',editor)
if observation:
    for index,item in enumerate(observation['paths']):
        (root/item['metadata']).write_text('fileFormatVersion: 2\\nguid: '+format(index+1000,'032x')+'\\n',encoding='utf-8')
metadata.finish_native(q,observation,editor,EXIT_CODE)
if EXIT_CODE:raise SystemExit('retained real compiler failure')
(evidence/'result.json').write_text(json.dumps(dict(run=run,candidate=candidate,status='passed',warnings=0,errors=0,skipped=0)))
'''


class FolderMetadataAcceptance(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='gfm_', dir='C:/' if os.name == 'nt' else None)
        self.base = Path(self.temporary.name);self.root=self.base/'Source';self.root.mkdir()
        q.git(self.root,'init','-b','main');q.git(self.root,'config','user.name','Native metadata acceptance');q.git(self.root,'config','user.email','test@example.invalid');q.git(self.root,'config','core.autocrlf','false')
        (self.root/'Game/ProjectSettings').mkdir(parents=True)
        (self.root/'Game/ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.6.8f1\n')
        (self.root/'version.txt').write_text('1.0.0\n')
        for path,guid in [('Game/Assets/_Generated.meta','f'*32),('Game/Assets/_Generated/Terrain.meta','e'*32)]:
            (self.root/path).parent.mkdir(parents=True,exist_ok=True);(self.root/path).write_bytes(sidecar(guid))
        for index in range(7):
            leaf=self.root/('Game/Assets/_Generated/Terrain/Source'+str(index)+'/Revision/data.txt');leaf.parent.mkdir(parents=True);leaf.write_text('tracked material source\n')
            leaf.with_suffix('.txt.meta').write_bytes(sidecar(format(index+2000,'032x')))
        q.git(self.root,'add','.');q.git(self.root,'commit','-m','Source assets with missing folder sidecars')
        self.head=q.git(self.root,'rev-parse','HEAD');self.editor=self.base/'Unity.exe';self.editor.touch()
        self.policy=dict(schema=1,version_file='version.txt',checks=[],repair=dict(enabled=True,native_folder_metadata=True),native_version_command=['native','--unity',str(self.editor)])
        self.evidence=self.base/'Evidence';self.evidence.mkdir()

    def tearDown(self):self.temporary.cleanup()

    def simulate(self, returncode=0):
        plan=metadata.prepare(q,self.root,self.evidence,self.policy);self.assertEqual(len(plan['paths']),14)
        with mock.patch.dict(os.environ,metadata.environment(self.evidence,plan)):
            observation=metadata.begin_native(q,self.root/'Game',self.editor)
            for index,item in enumerate(plan['paths']):(self.root/item['metadata']).write_bytes(sidecar(format(index+1000,'032x')))
            metadata.finish_native(q,observation,self.editor,returncode)
        return plan

    def test_native_failure_recovers_all_fourteen_without_passing_compilation(self):
        plan=self.simulate(1);before={p['metadata']:(self.root/p['metadata']).read_bytes() for p in plan['paths']}
        proof=metadata.harvest(q,self.root,self.evidence,plan)
        self.assertEqual(proof['native_returncode'],1);self.assertFalse(proof['model_invoked']);self.assertFalse((self.evidence/'result.json').exists())
        self.assertEqual(set(q.git(self.root,'diff-tree','--no-commit-id','--name-only','-r',self.head,proof['candidate']).splitlines()),set(before))
        q.clean(self.root)
        for path,raw in before.items():self.assertEqual((self.root/path).read_bytes(),raw)

    def test_preexisting_untracked_and_opt_out_are_not_adopted(self):
        path=self.root/'Game/Assets/_Generated/Terrain/Source0.meta';path.write_bytes(sidecar('a'*32))
        self.assertIsNone(metadata.prepare(q,self.root,self.evidence,self.policy));self.assertEqual(path.read_bytes(),sidecar('a'*32))
        path.unlink();self.policy['repair']['native_folder_metadata']=False
        self.assertIsNone(metadata.prepare(q,self.root,self.evidence,self.policy));self.assertEqual(q.git(self.root,'rev-parse','HEAD'),self.head)

    def test_proof_tamper_unknown_content_and_guid_collisions_never_stage(self):
        plan=self.simulate();proof=q.read_json(self.evidence/metadata.PROOF);original=(self.evidence/metadata.PROOF).read_bytes();first=self.root/plan['paths'][0]['metadata'];raw=first.read_bytes()
        for kind in ('nonce','bytes','unknown','tracked','collision','malformed'):
            with self.subTest(kind=kind):
                if kind=='nonce':q.durable(self.evidence/metadata.PROOF,dict(proof,invocation='different'))
                elif kind=='bytes':first.write_bytes(sidecar('c'*32))
                elif kind=='unknown':(self.root/'unrelated.txt').write_text('preserve me')
                elif kind=='tracked':(self.root/'version.txt').write_text('9.0.0\n')
                elif kind=='collision':
                    first.write_bytes(sidecar('e'*32));changed=json.loads(q.encoded(proof));item=changed['outputs'][0];item.update(guid='e'*32,raw_sha256=q.digest(first.read_bytes()))
                    import base64
                    item['raw_base64']=base64.b64encode(first.read_bytes()).decode();q.durable(self.evidence/metadata.PROOF,changed)
                else:(self.evidence/metadata.PROOF).write_bytes(b'not JSON')
                self.assertRaises(metadata.Blocked,metadata.harvest,q,self.root,self.evidence,plan)
                self.assertEqual(q.git(self.root,'rev-parse','HEAD'),self.head);self.assertFalse(q.git(self.root,'diff','--cached','--name-only'))
                first.write_bytes(raw);(self.evidence/metadata.PROOF).write_bytes(original)
                (self.root/'unrelated.txt').unlink(missing_ok=True);(self.root/'version.txt').write_text('1.0.0\n')

    def test_native_version_failure_commits_only_proven_sidecars_then_retains_failure(self):
        helper=self.base/'NativeFixture.py';helper.write_text(NATIVE_FIXTURE.replace('SOURCE',repr(str(Path(q.__file__).parent))).replace('EXIT_CODE','1'))
        self.policy['native_version_command']=[sys.executable,str(helper),'{evidence}','fixture','fixture','--unity',str(self.editor)]
        callbacks=[]
        self.assertRaises(q.GateFailure,q.stamp_version,self.root,self.policy,'1.0.1',self.evidence,callbacks.append)
        self.assertEqual(len(callbacks),1);self.assertEqual(callbacks[0]['native_returncode'],1);q.clean(self.root)
        self.assertEqual((self.root/'version.txt').read_text(),'1.0.0\n')

    def test_gate_recovery_discards_old_candidate_result_and_requires_new_run(self):
        helper=self.base/'NativeFixture.py';helper.write_text(NATIVE_FIXTURE.replace('SOURCE',repr(str(Path(q.__file__).parent))).replace('EXIT_CODE','0'))
        check=dict(name='native_compilation',kind='compilation',inputs=['.'],timeout_seconds=30,command=[sys.executable,str(helper),'{evidence}','{candidate}','{run}','--unity',str(self.editor)])
        self.policy['checks']=[check];cache={}
        with self.assertRaises(metadata.Recovered) as result:q.execute_gate(self.root,self.evidence,check,self.head,q.digest(self.policy),cache,self.policy)
        self.assertFalse(cache);candidate=result.exception.proof['candidate'];self.assertNotEqual(candidate,self.head)
        passed=q.execute_gate(self.root,self.evidence,check,candidate,q.digest(self.policy),cache,self.policy)
        self.assertEqual(passed['candidate'],candidate);self.assertFalse(passed['reused']);q.clean(self.root)


class FolderMetadataReportAcceptance(unittest.TestCase):
    def test_recovery_projection_retains_counts_and_omits_raw_metadata(self):
        from GitFlowObserve import projection
        value=projection(dict(native_folder_metadata_recoveries=[dict(candidate='a'*40,recovered_folders=14,model_invoked=False,outputs=[dict(raw_base64='private native content')])]))
        self.assertEqual(value['native_folder_metadata_recoveries'][0],dict(candidate='a'*40,recovered_folders=14,model_invoked=False))
        self.assertEqual(projection(dict(recovered_folders='secret',model_invoked='secret')),{})
        self.assertEqual(q.failure_diagnostic(metadata.Blocked('proof is stale'),'version_assignment')['cause_code'],'native_folder_metadata_unproven')


if __name__=='__main__':unittest.main(verbosity=2)
