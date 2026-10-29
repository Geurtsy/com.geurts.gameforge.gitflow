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
import datetime
from concurrent.futures import ThreadPoolExecutor
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

    def test_saved_inspector_capture_preserves_live_branch_index_and_unrelated_work(self):
        assets = self.repository / 'Assets' / 'Game'
        assets.mkdir(parents=True)
        (assets / 'Enemy.asset').write_bytes(b'MovementSpeed: 3\r\n')
        (assets / 'Enemy.asset.meta').write_bytes(b'guid: 12345678901234567890123456789012\r\n')
        (self.repository / 'Assets/Game.meta').write_text('folderAsset: yes\n')
        queue.git(self.repository, 'add', 'Assets')
        queue.git(self.repository, 'commit', '-m', 'Existing inspector asset')
        queue.git(self.repository, 'push', 'origin', 'main')
        head = queue.git(self.repository, 'rev-parse', 'HEAD')
        (assets / 'Enemy.asset').write_bytes(b'MovementSpeed: 4\r\n')
        (self.repository / 'feature.txt').write_text('unrelated staged work\n')
        queue.git(self.repository, 'add', 'feature.txt')
        (self.repository / 'feature.txt').write_text('unrelated later working edit\n')
        index = (self.repository / '.git/index').read_bytes()
        status = queue.git(self.repository, 'status', '--porcelain')
        value = queue.queue_local_changes(self.repository, 'origin', ['Assets/Game'], 'Enemy speed', 'patch', 'Raise speed from 3 to 4')
        again = queue.queue_local_changes(self.repository, 'origin', ['Assets/Game'], 'Same capture', 'patch', 'Same saved bytes')
        self.assertEqual(value['id'], again['id'])
        self.assertEqual(value['state'], 'queued')
        self.assertEqual(queue.git(self.repository, 'rev-parse', 'HEAD'), head)
        self.assertEqual(queue.git(self.repository, 'branch', '--show-current'), 'main')
        self.assertEqual((self.repository / '.git/index').read_bytes(), index)
        self.assertEqual(queue.git(self.repository, 'status', '--porcelain'), status)
        self.assertEqual(queue.git(self.repository, 'show', value['source'] + ':Assets/Game/Enemy.asset'), 'MovementSpeed: 4')
        self.assertEqual(queue.git(self.repository, 'show', value['source'] + ':feature.txt'), 'base')
        self.assertIn('Assets/Game/Enemy.asset.meta', value['capture']['files'])
        self.assertIn('Assets/Game.meta', value['capture']['files'])

    def test_capture_exclusions_and_changed_bytes_stop_without_submission(self):
        import GitFlowCapture as capture
        assets = self.repository / 'Assets'
        (assets / 'Game').mkdir(parents=True)
        (assets / 'Plugins').mkdir()
        (assets / 'Game/Enemy.asset').write_text('speed: 3\n')
        (assets / 'Plugins/vendor.dll').write_bytes(b'licensed fixture')
        original = capture.signature
        calls = []
        def changing_signature(*args):
            result = original(*args)
            calls.append(1)
            if len(calls) == 1:
                (assets / 'Game/Enemy.asset').write_text('speed: 4\n')
            return result
        with mock.patch.object(capture, 'signature', side_effect=changing_signature):
            self.assertRaisesRegex(queue.QueueError, 'changed during capture', queue.queue_local_changes,
                self.repository, 'origin', ['Assets'], 'Capture', 'patch', 'Fixture')
        self.assertEqual(queue.entries(self.repository, 'origin'), [])
        self.assertEqual((assets / 'Game/Enemy.asset').read_text(), 'speed: 4\n')
        plan = capture.inventory(queue, self.repository, ['Assets'])
        self.assertNotIn('Assets/Plugins/vendor.dll', plan['paths'])
        self.assertIn('Assets/Plugins/vendor.dll', plan['excluded'])

    def test_main_cannot_publish_while_gate_runs_or_overwrite_new_main(self):
        self.source()
        execute = queue.execute_gate
        moved = []
        def gate(*args, **kwargs):
            self.assertEqual(queue.remote_ref(self.repository, 'origin', 'refs/heads/main'), self.base)
            result = execute(*args, **kwargs)
            external = queue.git(self.repository, 'commit-tree', self.base + '^{tree}', '-p', self.base, '-m', 'Independent main movement')
            queue.git(self.repository, 'push', 'origin', external + ':refs/heads/main')
            moved.append(external)
            return result
        with mock.patch.object(queue, 'execute_gate', side_effect=gate):
            self.assertRaisesRegex(queue.QueueError, 'Main moved', queue.process_once, self.repository)
        self.assertEqual(queue.remote_ref(self.repository, 'origin', 'refs/heads/main'), moved[0])
        self.assertEqual(queue.entries(self.repository, 'origin')[0][2]['state'], 'validation_failed')

    def test_repair_no_progress_stops_and_original_three_hour_window_survives(self):
        import GitFlowRepair as repair
        (self.repository / 'Assets').mkdir()
        value = self.source('Assets/Game.cs', 'class Game { int speed = 3; }\n')
        policy = dict(self.policy, repair=dict(enabled=True, window_seconds=10800, executable='fixture', model='selected-model', effort='high'))
        before = (self.repository / 'Assets/Game.cs').read_bytes()
        def transport(executable, model, effort, context, **kwargs):
            content = json.loads(context)
            item = content['allowed_files']['Assets/Game.cs']
            return dict(model_invoked=True, proposal=dict(edits=[dict(path='Assets/Game.cs', expected_sha256=item['sha256'], old_text=' }', new_text=' }  ')]))
        lease = mock.Mock()
        self.assertRaisesRegex(queue.QueueError, 'No meaningful progress', repair.repair, queue, self.repository, value,
            policy, queue.QueueError('compile failure'), self.workspace_root / 'Evidence', lambda: None, lease, transport)
        self.assertEqual((self.repository / 'Assets/Game.cs').read_bytes(), before)
        started = value['repair']['first_started_utc']
        policy['repair']['model'] = 'different-model'
        self.assertRaisesRegex(queue.QueueError, 'No meaningful progress', repair.repair, queue, self.repository, value,
            policy, queue.QueueError('same failure'), self.workspace_root / 'Evidence', lambda: None, lease, transport)
        self.assertEqual(value['repair']['first_started_utc'], started)
        start = datetime.datetime.fromisoformat(started)
        self.assertEqual(repair.remaining(value['repair'], start + datetime.timedelta(hours=3)), 0)

    def test_stopped_runner_adoption_preserves_history_and_rejects_busy_owner(self):
        self.source()
        prior = queue.configuration(self.repository)
        identity = queue.digest(prior)
        queue.git(self.repository, 'update-ref', 'refs/remotes/origin/main', self.base)
        lease = queue.leases.Lease(queue, self.repository, 'origin').acquire()
        self.assertRaisesRegex(queue.QueueError, 'not verifiably released', queue.upgrade_runner, self.repository, identity)
        self.assertEqual(queue.configuration(self.repository), prior)
        lease.close()
        before = queue.entries(self.repository, 'origin')
        result = queue.upgrade_runner(self.repository, identity)
        self.assertEqual(result['state'], 'runner_adopted')
        self.assertEqual(queue.entries(self.repository, 'origin'), before)
        revised = queue.configuration(self.repository)
        self.assertEqual(revised['workspace_root'], prior['workspace_root'])
        self.assertEqual(revised['runner_history'][0]['runner_hash'], prior['runner_hash'])
        self.assertRaisesRegex(queue.QueueError, 'Configuration changed', queue.upgrade_runner, self.repository, identity)

    def test_repair_stops_when_changed_source_has_same_independent_failure(self):
        import GitFlowRepair as repair
        (self.repository / 'Assets').mkdir()
        value = self.source('Assets/Game.cs', 'class Game { int speed = 3; }\n')
        policy = dict(self.policy, repair=dict(enabled=True, window_seconds=10800, executable='fixture', model='selected-model', effort='high'))
        calls = []
        def transport(executable, model, effort, context, **kwargs):
            calls.append(1)
            item = json.loads(context)['allowed_files']['Assets/Game.cs']
            return dict(model_invoked=True, proposal=dict(edits=[dict(path='Assets/Game.cs', expected_sha256=item['sha256'], old_text='speed = 3', new_text='speed = 4')]))
        lease = mock.Mock()
        repair.repair(queue, self.repository, value, policy, queue.QueueError('same independent compiler failure'),
            self.workspace_root / 'Evidence', lambda: None, lease, transport)
        self.assertIn('speed = 4', (self.repository / 'Assets/Game.cs').read_text())
        self.assertRaisesRegex(queue.QueueError, 'same failure', repair.repair, queue, self.repository, value, policy,
            queue.QueueError('same independent compiler failure'), self.workspace_root / 'Evidence', lambda: None, lease, transport)
        self.assertEqual(len(calls), 1)
        self.assertEqual(value['repair']['attempts'][-1]['state'], 'no_progress_after_validation')

    def test_concurrent_claims_have_one_winner_and_renewal_resets_observation(self):
        queue.git(self.repository, 'update-ref', 'refs/remotes/origin/main', self.base)
        contenders = [queue.leases.Lease(queue, self.repository, 'origin') for _ in range(2)]
        def claim(owner):
            try:
                owner.acquire()
                return owner
            except queue.leases.Contended:
                return None
        with ThreadPoolExecutor(max_workers=2) as threads:
            results = list(threads.map(claim, contenders))
        winners = [item for item in results if item]
        self.assertEqual(len(winners), 1)
        winner = winners[0]
        clock = [0.0]
        observer = queue.leases.Lease(queue, self.repository, 'origin', clock=lambda: clock[0])
        self.assertRaises(queue.leases.Contended, observer.acquire)
        first = winner.oid
        winner.advance(); winner.advance()
        self.assertNotEqual(first, winner.oid)
        clock[0] = queue.leases.SECONDS + 1
        self.assertRaises(queue.leases.Contended, observer.acquire)
        self.assertEqual(queue.remote_ref(self.repository, 'origin', queue.LOCK), winner.oid)
        winner.close()

    def test_uncertain_ack_fences_owner_without_repeating_accepted_atomic_write(self):
        queue.git(self.repository, 'update-ref', 'refs/remotes/origin/main', self.base)
        lease = queue.leases.Lease(queue, self.repository, 'origin').acquire()
        real = queue.leases.transport
        pushes = []
        target = 'refs/heads/gameforge/uncertain-fixture'
        def uncertain(q, root, *arguments, **kwargs):
            result = real(q, root, *arguments, **kwargs)
            if arguments[0] == 'push':
                pushes.append(arguments)
                raise subprocess.TimeoutExpired('injected lost acknowledgement', 300)
            return result
        with mock.patch.object(queue.leases, 'transport', side_effect=uncertain):
            self.assertRaises(queue.leases.LeaseError, lease.publish, self.repository, 'origin', [self.base + ':' + target])
            self.assertRaises(queue.leases.LeaseError, lease.publish, self.repository, 'origin', [self.base + ':' + target])
        self.assertEqual(len(pushes), 1)
        self.assertEqual(queue.remote_ref(self.repository, 'origin', target), self.base)
        self.assertTrue(lease.stop.is_set())
        self.assertRaises(queue.leases.LeaseError, lease.close)

    def test_unknown_and_legacy_owner_records_are_never_treated_as_available(self):
        queue.git(self.repository, 'update-ref', 'refs/remotes/origin/main', self.base)
        parent = self.base
        for metadata in [dict(legacy=True), dict(lease_protocol=99), dict(lease_protocol=1, state='owned')]:
            child = queue.object_commit(self.repository, parent, metadata, 'Unsupported fixture owner')
            queue.git(self.repository, 'push', 'origin', child + ':' + queue.LOCK)
            lease = queue.leases.Lease(queue, self.repository, 'origin')
            self.assertRaises(queue.leases.LeaseError, lease.acquire)
            self.assertEqual(queue.remote_ref(self.repository, 'origin', queue.LOCK), child)
            self.assertEqual(queue.inspect_owner(self.repository, 'origin')['state'], 'unknown')
            parent = child

    def test_specialist_reuses_only_identical_declared_inputs_and_preserves_provenance(self):
        check = dict(self.policy['checks'][0], kind='specialist', inputs=['feature.txt', 'Gate.py'])
        cache = {}
        evidence = self.workspace_root / 'Evidence'
        first = queue.execute_gate(self.repository, evidence, check, self.base, 'fixture-policy', cache)
        unrelated = self.source('unrelated.txt', 'unrelated\n')
        second = queue.execute_gate(self.repository, evidence, check, unrelated['source'], 'fixture-policy', cache)
        self.assertTrue(second['reused'])
        self.assertEqual(second['evidence_candidate'], self.base)
        self.assertEqual(second['candidate'], unrelated['source'])
        (self.repository / 'feature.txt').write_text('changed declared input\n')
        queue.git(self.repository, 'add', 'feature.txt'); queue.git(self.repository, 'commit', '-m', 'Changed specialist input')
        head = queue.git(self.repository, 'rev-parse', 'HEAD')
        third = queue.execute_gate(self.repository, evidence, check, head, 'fixture-policy', cache)
        self.assertFalse(third['reused'])
        self.assertNotEqual(third['key'], first['key'])


if __name__ == '__main__':
    unittest.main(verbosity=2)
