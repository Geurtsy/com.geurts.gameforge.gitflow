"""Disposable real-Git acceptance for publication, loss recovery and storage risks.

These tests are necessary: source inspection cannot establish atomic remote
arbitration, unchanged-input reuse or recovery after a successful publication.
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

import GitFlowQueue as queue


class QueueAcceptance(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='gq_', dir='C:/' if os.name == 'nt' else None)
        self.root = Path(self.temporary.name)
        self.remote = self.root / 'Remote.git'
        self.repository = self.root / 'Source'
        self.repository.mkdir()
        queue.git(self.root, 'init', '--bare', str(self.remote))
        queue.git(self.repository, 'init', '-b', 'main')
        queue.git(self.repository, 'config', 'user.name', 'GitFlow disposable acceptance')
        queue.git(self.repository, 'config', 'user.email', 'acceptance@example.invalid')
        queue.git(self.repository, 'config', 'core.autocrlf', 'false')
        (self.repository / '.gitignore').write_text('Library/\n')
        (self.repository / 'version.txt').write_text('1.0.0\n')
        (self.repository / 'feature.txt').write_text('base\n')
        (self.repository / 'Gate.py').write_text(
            'import json,sys\nfrom pathlib import Path\n'
            'folder=Path(sys.argv[1]); candidate=sys.argv[2]; run=sys.argv[3]\n'
            'if Path("fail.txt").exists(): raise SystemExit("required gate failed")\n'
            'report=dict(run=run,candidate=candidate,status="passed",warnings=0,errors=0,skipped=0)\n'
            '(folder/"result.json").write_text(json.dumps(report))\n')
        queue.git(self.repository, 'add', '.')
        queue.git(self.repository, 'commit', '-m', 'Initial project')
        queue.git(self.repository, 'remote', 'add', 'origin', str(self.remote))
        queue.git(self.repository, 'push', 'origin', 'main')
        self.base = queue.git(self.repository, 'rev-parse', 'HEAD')
        self.workspace_root = self.root / 'Processor'
        self.policy = dict(schema=1, version_file='version.txt', checks=[dict(name='compile_project', kind='compilation',
            command=['{python}', 'Gate.py', '{evidence}', '{candidate}', '{run}'], inputs=['.'], timeout_seconds=30)])
        queue.configure(self.repository, 'origin', self.workspace_root, self.policy)

    def tearDown(self):
        # Only a temporary root created by this test is cleaned; never user workspaces.
        if not self.root.name.startswith('gq_') or self.root.parent != Path(self.temporary.name).parent:
            raise AssertionError('Unexpected fixture cleanup root')
        self.temporary.cleanup()

    def source(self, name='feature.txt', text='changed\n'):
        queue.git(self.repository, 'switch', '-c', 'worker_' + str(len(queue.entries(self.repository, 'origin'))))
        (self.repository / name).write_text(text)
        queue.git(self.repository, 'add', name)
        queue.git(self.repository, 'commit', '-m', 'Task source')
        return queue.submit(self.repository, 'origin', 'Task', 'patch', 'Focused acceptance change')

    def test_exact_source_is_idempotent_and_delivery_retains_ancestry(self):
        value = self.source()
        again = queue.submit(self.repository, 'origin', 'Other label', 'minor', 'Does not create a duplicate')
        self.assertEqual(value['id'], again['id'])
        self.assertEqual(len(queue.entries(self.repository, 'origin')), 1)
        result = queue.process_once(self.repository)
        self.assertEqual(result['state'], 'integrated')
        self.assertEqual(result['version'], '1.0.1')
        self.assertTrue(queue.ancestor(self.repository, value['source'], result['main_commit']))
        self.assertEqual(result['pulls']['publisher'], 'passed')
        self.assertEqual(result['pulls']['original'], 'not_on_main')
        self.assertEqual(queue.process_once(self.repository)['state'], 'idle')

    def test_failure_blocks_following_work_and_never_changes_main(self):
        value = self.source('fail.txt', 'fail\n')
        self.assertRaises(queue.QueueError, queue.process_once, self.repository)
        self.assertEqual(queue.remote_ref(self.repository, 'origin', 'refs/heads/main'), self.base)
        second = self.source('later.txt', 'later\n')
        result = queue.process_once(self.repository)
        self.assertEqual(result['id'], value['id'])
        self.assertEqual(result['state'], 'validation_failed')
        rows = {row[2]['id']: row[2] for row in queue.entries(self.repository, 'origin')}
        self.assertEqual(rows[second['id']]['state'], 'queued')
        self.assertEqual(len(rows[value['id']]['attempts']), 1)

    def test_published_pull_failure_resumes_without_retest_or_second_version(self):
        value = self.source()
        with mock.patch.object(queue, 'synchronize', side_effect=queue.QueueError('Injected post-publication pull failure')):
            self.assertRaises(queue.QueueError, queue.process_once, self.repository)
        published = queue.entries(self.repository, 'origin')[0][2]
        self.assertEqual(published['state'], 'published')
        self.assertTrue(queue.ancestor(self.repository, value['source'], queue.remote_ref(self.repository, 'origin', 'refs/heads/main')))
        with mock.patch.object(queue, 'execute_gate', side_effect=AssertionError('Must not revalidate already published source')):
            result = queue.process_once(self.repository)
        self.assertEqual(result['state'], 'integrated')
        self.assertEqual(result['version'], published['version'])
        self.assertEqual(result['main_commit'], published['main_commit'])

    def test_dirty_user_checkout_preserved_and_record_stays_published(self):
        value = self.source()
        queue.git(self.repository, 'switch', 'main')
        (self.repository / 'feature.txt').write_bytes(b'user unsaved changes\r\n')
        self.assertRaises(queue.QueueError, queue.process_once, self.repository)
        self.assertEqual((self.repository / 'feature.txt').read_bytes(), b'user unsaved changes\r\n')
        self.assertEqual(queue.entries(self.repository, 'origin')[0][2]['state'], 'published')
        self.assertEqual(queue.git(self.repository, 'rev-parse', 'HEAD'), self.base)

    def test_stale_owner_atomic_publication_is_rejected(self):
        queue.git(self.repository, 'update-ref', 'refs/remotes/origin/main', self.base)
        first = queue.leases.Lease(queue, self.repository, 'origin').acquire()
        now = [0.0]
        second = queue.leases.Lease(queue, self.repository, 'origin', clock=lambda: now[0])
        self.assertRaises(queue.leases.Contended, second.acquire)
        now[0] = queue.leases.SECONDS + 1
        second.acquire()
        target = 'refs/heads/gameforge/stale-attempt'
        self.assertRaises(queue.leases.LeaseError, first.publish, self.repository, 'origin', [self.base + ':' + target])
        self.assertIsNone(queue.remote_ref(self.repository, 'origin', target))
        second.close()

    def test_shared_workspace_and_library_are_reused(self):
        self.source()
        queue.process_once(self.repository)
        workspace = self.workspace_root / 'Workspace'
        library = workspace / 'Library'
        library.mkdir()
        (library / 'fixture_cache.bin').write_bytes(b'cache' * 1024)
        identity = (library / 'fixture_cache.bin').stat().st_ino
        queue.git(self.repository, 'switch', 'main')
        queue.git(self.repository, 'pull', '--ff-only', 'origin', 'main')
        self.source('second.txt', 'second source\n')
        result = queue.process_once(self.repository)
        self.assertEqual(result['version'], '1.0.2')
        self.assertEqual((library / 'fixture_cache.bin').stat().st_ino, identity)
        self.assertEqual(queue.common(workspace), queue.common(self.repository))
        self.assertEqual(len(list(self.workspace_root.glob('Workspace*'))), 1)


if __name__ == '__main__':
    unittest.main(verbosity=2)
