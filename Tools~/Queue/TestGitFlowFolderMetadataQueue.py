"""End-to-end real remote queue recovery without a model call or duplicate version."""
from pathlib import Path
import sys
import unittest
from unittest import mock

import GitFlowQueue as q
from TestGitFlowQueue import QueueAcceptance
from TestGitFlowFolderMetadata import NATIVE_FIXTURE, sidecar


class FolderMetadataQueueAcceptance(QueueAcceptance):
    def test_queue_revalidates_native_metadata_progress_without_ai_or_renumbering(self):
        project=self.repository/'Game'
        (project/'ProjectSettings').mkdir(parents=True)
        (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.6.8f1\n')
        terrain=project/'Assets/_Generated/Terrain';terrain.mkdir(parents=True)
        (project/'Assets/_Generated.meta').write_bytes(sidecar('f'*32))
        (project/'Assets/_Generated/Terrain.meta').write_bytes(sidecar('e'*32))
        (self.repository/'Gate.py').write_text(NATIVE_FIXTURE.replace('SOURCE',repr(str(Path(q.__file__).parent))).replace('EXIT_CODE','0'))
        q.git(self.repository,'add','.');q.git(self.repository,'commit','-m','Native fixture baseline');q.git(self.repository,'push','origin','main')
        self.base=q.git(self.repository,'rev-parse','HEAD')
        editor=self.root/'Unity.exe';editor.touch()
        config=q.configuration(self.repository)
        config['policy']['checks'][0]['command'] += ['--unity',str(editor)]
        config['policy']['repair']=dict(enabled=True,executable='must-not-run',model='fixture-model',effort='high',window_seconds=10800)
        config['policy_hash']=q.digest(config['policy']);q.validate_policy(config['policy'])
        q.durable(q.common(self.repository)/'gameforge_queue/configuration.json',config);q.durable(self.workspace_root/'ownership.json',config)
        leaf=terrain/'Added/Revision/data.txt';leaf.parent.mkdir(parents=True);leaf.write_text('new tracked source\n');leaf.with_suffix('.txt.meta').write_bytes(sidecar('b'*32))
        q.git(self.repository,'add','Game')
        original=self.source()
        with mock.patch('GitFlowRepair.repair',side_effect=AssertionError('Native metadata must not invoke a paid model')):
            result=q.process_once(self.repository)
        self.assertEqual(result['state'],'integrated');self.assertEqual(result['id'],original['id']);self.assertEqual(result['version'],'1.0.1')
        self.assertEqual(len(result['attempts']),2)
        self.assertEqual(result['attempts'][0]['outcome'],'native_metadata_recovered_requires_validation')
        self.assertEqual(len(result['native_folder_metadata_recoveries']),1)
        self.assertEqual(len(result['native_folder_metadata_recoveries'][0]['outputs']),2)
        self.assertFalse(result['native_folder_metadata_recoveries'][0]['model_invoked']);self.assertNotIn('repair',result)
        self.assertEqual(result['receipt']['checks'][0]['candidate'],result['main_commit'])
        self.assertTrue(q.ancestor(self.repository,original['source'],result['main_commit']))
        self.assertEqual(q.process_once(self.repository)['state'],'idle')


if __name__=='__main__':unittest.main(verbosity=2)
