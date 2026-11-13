"""Disposable real-Git acceptance for publication, loss recovery and storage risks.

These tests are necessary: source inspection cannot establish atomic remote
arbitration, unchanged-input reuse or recovery after a successful publication.
"""
import json
import re
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
        with mock.patch.object(queue, 'entries', wraps=queue.entries) as reads:
            result = queue.process_once(self.repository)
        self.assertEqual(reads.call_count, 1, 'No second queue scan is needed without dependencies')
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
        diagnostic = rows[value['id']]['diagnostic']
        self.assertEqual(diagnostic['stage'], 'required_check')
        self.assertEqual(diagnostic['cause_code'], 'required_check_exit')
        self.assertEqual(diagnostic['check_name'], 'compile_project')
        self.assertEqual(diagnostic['exit_code'], 1)
        self.assertFalse(diagnostic['automatic_repair'])
        owner = self.workspace_root / 'Evidence' / value['id']
        retained = queue.read_json(owner / 'diagnostic.json')
        self.assertEqual(retained['diagnostic'], diagnostic)
        self.assertTrue((owner / diagnostic['owner_failure_file']).is_file())
        for relative in diagnostic['evidence']:
            self.assertTrue((owner / relative).is_file())

    def test_published_pull_failure_resumes_without_retest_or_second_version(self):
        value = self.source()
        with mock.patch.object(queue, 'synchronize', side_effect=queue.QueueError('Injected post-publication pull failure')):
            self.assertRaises(queue.QueueError, queue.process_once, self.repository)
        published = queue.entries(self.repository, 'origin')[0][2]
        self.assertEqual(published['state'], 'published')
        self.assertEqual(published['diagnostic']['stage'], 'synchronization')
        self.assertEqual(published['diagnostic']['cause_code'], 'synchronization_pending')
        self.assertTrue(queue.ancestor(self.repository, value['source'], queue.remote_ref(self.repository, 'origin', 'refs/heads/main')))
        with mock.patch.object(queue, 'execute_gate', side_effect=AssertionError('Must not revalidate already published source')):
            result = queue.process_once(self.repository)
        self.assertEqual(result['state'], 'integrated')
        self.assertEqual(result['version'], published['version'])
        self.assertEqual(result['main_commit'], published['main_commit'])
        self.assertEqual(result['diagnostic']['state'], 'resolved')

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





    def test_conflict_only_guard_refuses_outside_edits_assertion_removal_and_attribute_loss(self):
        import GitFlowRepair as repair
        original = 'header\n<<<<<<< HEAD\nRequire(Mouse());\n=======\nRequire(Audio());\n>>>>>>> incoming\nfooter\n'
        good = 'header\nRequire(Mouse()); Require(Audio());\nfooter\n'
        repair.guard_conflict_resolution(queue, 'Assets/Editor/Check.cs', original, good)
        for updated in [good.replace('header', 'changed'), 'header\nPass();\nfooter\n', good.replace('Require(Audio());', 'Assert.Ignore("skip");')]:
            self.assertRaises(queue.QueueError, repair.guard_conflict_resolution, queue, 'Assets/Editor/Check.cs', original, updated)
        attributes = '# common\n<<<<<<< HEAD\na.cs text eol=crlf\n=======\nb.cs text eol=crlf\n>>>>>>> incoming\n'
        self.assertRaises(queue.QueueError, repair.guard_conflict_resolution, queue, '.gitattributes', attributes, '# common\na.cs text eol=crlf\n')
        repair.guard_conflict_resolution(queue, '.gitattributes', attributes, '# common\na.cs text eol=crlf\nb.cs text eol=crlf\n')

    def test_conflict_scope_change_is_stopped_guarded_and_preserves_ownership(self):
        prior = queue.configuration(self.repository)
        paths = ['.gitattributes', 'Assets/Editor/Check.cs']
        identity = queue.digest(prior)
        with mock.patch.object(queue, 'inspect_owner', return_value=dict(state='owned')):
            self.assertRaises(queue.QueueError, queue.set_repair_conflict_paths, self.repository, identity, paths)
        self.assertEqual(queue.configuration(self.repository), prior)
        self.assertRaises(queue.QueueError, queue.set_repair_conflict_paths, self.repository, '0' * 64, paths)
        self.assertRaises(queue.QueueError, queue.set_repair_conflict_paths, self.repository, identity, ['Gate.py'])
        result = queue.set_repair_conflict_paths(self.repository, identity, paths)
        revised = queue.configuration(self.repository)
        self.assertEqual(result['state'], 'repair_conflict_scope_updated')
        self.assertEqual(revised['policy']['repair']['conflict_paths'], paths)
        self.assertEqual(revised['policy']['checks'], prior['policy']['checks'])
        self.assertEqual(revised, queue.read_json(self.workspace_root / 'ownership.json'))
        self.assertEqual(len(list((queue.common(self.repository) / 'gameforge_queue').glob('conflict_scope_*.json'))), 1)
        for path in ['../bad.cs', 'Assets/../bad.cs', 'C:/bad.cs', 'Assets\\Editor\\Bad.cs', 'Assets/Editor/*.cs', 'Packages/Bad.cs', 'Tools/Bad.py', '.git/config', 'Assets/Editor/Bad.cs.']:
            with self.assertRaises(queue.QueueError):
                queue.validate_policy(dict(self.policy, repair=dict(conflict_paths=[path])))




    def test_native_diagnostics_require_actionable_messages_without_a_size_ceiling(self):
        import GitFlowRepair as repair
        folder = self.root / 'NativeEvidence'; folder.mkdir()
        native = folder / 'Unity.log'; native.write_text('No recognized failure message\n')
        self.assertRaisesRegex(queue.QueueError, 'owner must supply diagnostics.json', repair.native_diagnostics, queue, folder)
        native.write_bytes(b'x' * (9 * 1024 * 1024) + b'\nCompiler.cs(1): error CS1234: actual failure\n')
        self.assertIn('error CS1234', repair.native_diagnostics(queue, folder)['complete_matching_diagnostics'][0])

    def test_diagnosis_transport_accepts_larger_context_and_bounded_reply(self):
        import GitFlowModels as models
        context = '\u00e9' * (1024 * 1024)
        catalogue = dict(account_state='signed_in', models=[dict(model='selected-model', efforts=['high'])])
        response = dict(diagnosis='', proposal='', limitations='', edits=[])
        baseline = json.dumps(response).encode()
        response['diagnosis'] = 'd' * (300 * 1024)
        encoded = json.dumps(response).encode()
        outputs = [encoded, encoded + b' ']
        def run(arguments, **options):
            self.assertIn(context.encode(), options['input'])
            Path(arguments[arguments.index('-o') + 1]).write_bytes(outputs.pop(0))
            return subprocess.CompletedProcess(arguments, 0)
        with mock.patch.object(models, 'discover', return_value=catalogue), mock.patch.object(models, 'executable', return_value='fixture'), mock.patch.object(models, 'process_run', side_effect=run) as transport:
            result = models.diagnose('fixture', 'selected-model', 'high', context, propose_edits=True)
            self.assertEqual(result['proposal'], response)
            self.assertEqual(models.diagnose('fixture', 'selected-model', 'high', context + 'x', propose_edits=True)['proposal'], response)
            self.assertEqual(transport.call_count, 2)

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

    def test_deployed_repair_and_native_helper_survive_package_cache_removal(self):
        cache = self.root / 'PackageCache'
        cache.mkdir()
        for path in Path(queue.__file__).parent.glob('GitFlow*.py'):
            (cache / path.name).write_bytes(path.read_bytes())
        # A controlled proposal transport exercises the real late import and exact-edit
        # guard without paid inference. Every other runner/gate path remains real.
        models = cache / 'GitFlowModels.py'
        with models.open('a', encoding='utf-8') as output:
            output.write('\ndef repair_project(settings, context, workspace, evidence, maximum_seconds, review=False):\n'
                '    if review: return dict(model_invoked=False, proposal=dict(approved=True, reason="Source defect fixed; gate intact", limitations=""))\n'
                '    path=workspace/"Assets/Game.cs"; path.write_bytes(path.read_bytes().replace(b"speed = 3", b"speed = 4"))\n'
                '    return dict(model_invoked=False, proposal=dict(status="changed", summary="Corrected speed", limitations=""))\n')
        gate = self.repository / 'Gate.py'
        (self.repository / '.gitattributes').write_bytes(b'*.cs text eol=crlf\n')
        gate.write_text('import json,sys,shutil,subprocess\nfrom pathlib import Path\n'
            'cache=Path(' + repr(str(cache)) + ').resolve()\n'
            'assert cache.parent==Path.cwd().parent.parent and cache.name=="PackageCache"\n'
            'if cache.exists(): shutil.rmtree(cache)\n'
            'subprocess.run([sys.executable,sys.argv[4],"--help"],check=True,stdout=subprocess.DEVNULL)\n'
            'if "speed = 3" in Path("Assets/Game.cs").read_text(): raise SystemExit("Source correction required")\n'
            'folder=Path(sys.argv[1]); report=dict(run=sys.argv[3],candidate=sys.argv[2],status="passed",warnings=0,errors=0,skipped=0)\n'
            '(folder/"result.json").write_text(json.dumps(report))\n')
        queue.git(self.repository, 'add', 'Gate.py', '.gitattributes')
        queue.git(self.repository, 'commit', '-m', 'Independent cache-removal gate fixture')
        queue.git(self.repository, 'push', 'origin', 'main')
        self.base = queue.git(self.repository, 'rev-parse', 'HEAD')
        config = queue.configuration(self.repository)
        config['policy']['checks'][0]['command'].append(str(cache / 'GitFlowUnity.py'))
        config['policy']['repair'] = dict(enabled=True, executable='fixture', model='selected-model', effort='high', window_seconds=10800)
        config['policy_hash'] = queue.digest(config['policy'])
        control = self.repository / '.git/gameforge_queue/configuration.json'
        queue.durable(control, config)
        queue.durable(self.workspace_root / 'ownership.json', config)
        cli = [sys.executable, str(cache / 'GitFlowQueue.py'), '--repository', str(self.repository)]
        queue.run([*cli, 'upgrade-runner', '--operator', '--expected-configuration', queue.digest(config)], self.root)
        deployed = queue.read_json(control)
        self.assertEqual(deployed['workspace_root'], config['workspace_root'])
        self.assertEqual(deployed['policy']['repair'], config['policy']['repair'])
        self.assertEqual(Path(deployed['policy']['checks'][0]['command'][-1]).parent, Path(deployed['runner']).parent)
        (self.repository / 'Assets').mkdir()
        value = self.source('Assets/Game.cs', 'class Game { int speed = 3; }\n' + '// complete source\n' * 2200)
        result = json.loads(queue.run([*cli, 'process', '--operator'], self.root))
        self.assertEqual(result['state'], 'integrated')
        self.assertFalse(cache.exists())
        self.assertEqual(len(result['repair']['attempts']), 1)
        self.assertFalse(result['repair']['attempts'][0]['model_invoked'])
        self.assertTrue(queue.ancestor(self.repository, value['source'], result['main_commit']))
        self.assertIn('speed = 4', (self.workspace_root / 'Workspace/Assets/Game.cs').read_text())
        self.assertTrue(Path(deployed['runner']).is_file())
        # A direct restart from the preserved owner needs no original package folder.
        idle = json.loads(queue.run([sys.executable, deployed['runner'], '--repository', str(self.repository), 'process', '--operator'], self.root))
        self.assertEqual(idle['state'], 'idle')

    def test_incomplete_or_changed_deployment_is_refused_before_claim(self):
        config = queue.configuration(self.repository)
        repair = Path(config['runner']).with_name('GitFlowRepair.py')
        repair.write_bytes(repair.read_bytes() + b'\n# changed after adoption\n')
        before = queue.remote_ref(self.repository, 'origin', 'refs/heads/main')
        self.assertRaisesRegex(queue.QueueError, 'Configured runner differs', queue.process_once, self.repository)
        self.assertEqual(queue.remote_ref(self.repository, 'origin', 'refs/heads/main'), before)
        self.assertEqual(queue.inspect_owner(self.repository, 'origin')['state'], 'not_configured')

    def test_deployed_service_keeps_pid_and_explicit_stop_resume_guards(self):
        import time
        from GitFlowProcess import child_process
        config = queue.configuration(self.repository)
        control = self.repository / '.git/gameforge_queue'
        cli = [sys.executable, str(Path(queue.__file__)), '--repository', str(self.repository)]
        with child_process([*cli, 'serve', '--operator', '--expected-configuration', queue.digest(config)],
                cwd=self.root, stdout=subprocess.PIPE, stderr=subprocess.PIPE) as process:
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                if (control / 'state.json').exists():
                    state = queue.read_json(control / 'state.json')
                    if state['state'] == 'idle': break
                if process.poll() is not None: self.fail('Deployed service exited before idle')
                time.sleep(0.1)
            else: self.fail('Deployed service never reported idle')
            self.assertEqual(state['pid'], process.pid, 'Startup forwarding must retain the real owner PID')
            queue.run([*cli, 'request-stop', '--operator'], self.root)
            process.communicate(timeout=20)
            self.assertEqual(process.returncode, 0)
        stopped = (control / 'stop.json').read_bytes()
        self.assertRaisesRegex(queue.QueueError, 'reviewed publisher configuration changed',
            queue.resume_service, self.repository, '0' * 64)
        self.assertEqual((control / 'stop.json').read_bytes(), stopped)
        self.assertEqual(queue.resume_service(self.repository, queue.digest(config))['state'], 'ready_to_start')
        self.assertFalse((control / 'stop.json').exists())


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

    def heartbeat_case(self, mode):
        queue.git(self.repository, 'update-ref', 'refs/remotes/origin/main', self.base)
        now = [0.0]
        lease = queue.leases.Lease(queue, self.repository, 'origin', clock=lambda: now[0]).acquire()
        held, deadline = lease.oid, lease.deadline
        real = queue.leases.transport
        pushes = []
        def rejected(q, root, *arguments, **kwargs):
            if arguments[0] != 'push':
                result = real(q, root, *arguments, **kwargs)
                if mode == 'late': now[0] = deadline
                return result
            pushes.append(arguments)
            if len(pushes) == 1 or mode == 'twice':
                if mode == 'accepted': real(q, root, *arguments, **kwargs)
                if mode == 'successor':
                    metadata = lease.metadata('owned', 'other owner')
                    metadata['owner'] = 'f' * 32
                    child = queue.object_commit(root, held, metadata, 'Competing successor')
                    real(q, root, 'push', 'origin', child + ':' + queue.LOCK)
                raise queue.leases.Rejected('Injected completed heartbeat rejection')
            return real(q, root, *arguments, **kwargs)
        with mock.patch.object(queue.leases, 'transport', side_effect=rejected):
            if mode in {'successor', 'twice', 'late'}:
                self.assertRaises(queue.leases.LeaseError, lease.advance)
                self.assertTrue(lease.stop.is_set())
                self.assertRaises(queue.leases.LeaseError, lease.advance)
            else:
                lease.advance()
                self.assertFalse(lease.stop.is_set())
        self.assertEqual(len(pushes), 2 if mode in {'retry', 'twice'} else 1)
        if mode in {'accepted', 'retry'}:
            self.assertEqual(queue.remote_ref(self.repository, 'origin', queue.LOCK), lease.oid)
            receipt = queue.read_json(queue.common(self.repository)/'gameforge_queue'/('heartbeat_'+lease.oid+'.json'))
            self.assertEqual(receipt['state'], 'verified')
            self.assertEqual(receipt['retried'], mode == 'retry')
            lease.close()
        else:
            self.assertEqual(lease.deadline, deadline)

    def test_rejected_heartbeat_retries_once_after_exact_held_owner_readback(self):
        self.heartbeat_case('retry')

    def test_rejected_but_applied_heartbeat_is_verified_without_another_push(self):
        self.heartbeat_case('accepted')

    def test_rejected_heartbeat_with_changed_owner_is_permanently_fenced(self):
        self.heartbeat_case('successor')

    def test_repeated_heartbeat_rejection_is_bounded_and_permanently_fenced(self):
        self.heartbeat_case('twice')

    def test_heartbeat_readback_cannot_revive_the_old_deadline(self):
        self.heartbeat_case('late')

    def test_completed_delivery_rejection_never_retries_as_a_heartbeat(self):
        queue.git(self.repository,'update-ref','refs/remotes/origin/main',self.base)
        lease=queue.leases.Lease(queue,self.repository,'origin').acquire()
        real=queue.leases.transport
        pushes=[]
        def reject(q,root,*arguments,**kwargs):
            if arguments[0]=='push':
                pushes.append(arguments)
                raise queue.leases.Rejected('Injected delivery rejection')
            return real(q,root,*arguments,**kwargs)
        with mock.patch.object(queue.leases,'transport',side_effect=reject):
            self.assertRaises(queue.leases.LeaseError,lease.publish,self.repository,'origin',[self.base+':refs/heads/gameforge/rejected'])
        self.assertEqual(len(pushes),1)
        self.assertTrue(lease.stop.is_set())
        self.assertIsNone(queue.remote_ref(self.repository,'origin','refs/heads/gameforge/rejected'))

    def test_native_timeout_restores_only_declared_parent_owned_outputs(self):
        session = self.repository/'session.meta'
        data = b'\xef\xbb\xbfNative original\r\n'
        session.write_bytes(data)
        script = self.repository/'Native.py'
        script.write_text('from pathlib import Path\nimport time\n'
                          'Path("session.meta").write_bytes(b"native session output")\n'
                          'Path("unknown.txt").write_bytes(b"preserve unknown")\n'
                          'time.sleep(60)\n')
        queue.git(self.repository,'add','session.meta','Native.py')
        queue.git(self.repository,'commit','-m','Native timeout fixture')
        candidate=queue.git(self.repository,'rev-parse','HEAD')
        index=Path(queue.git(self.repository,'rev-parse','--path-format=absolute','--git-path','index'))
        before=index.read_bytes()
        check=dict(self.policy['checks'][0],command=['{python}','Native.py'],timeout_seconds=2,session_outputs=['session.meta'])
        self.assertRaises(queue.GateFailure,queue.execute_gate,self.repository,self.workspace_root/'Evidence',check,candidate,'policy',{})
        self.assertEqual(session.read_bytes(),data)
        self.assertEqual(index.read_bytes(),before)
        self.assertEqual((self.repository/'unknown.txt').read_bytes(),b'preserve unknown')
        receipts=list((self.workspace_root/'Evidence').glob('*/session-restoration.json'))
        self.assertEqual(queue.read_json(receipts[0])['restored'],['session.meta'])
        import GitFlowSession
        for names in [['../source'],['*.meta'],['.git/index'],['x','x'],[{}]]:
            self.assertRaises(queue.QueueError,GitFlowSession.validate,queue,names)

    def test_native_session_divergence_is_preserved_before_any_restoration(self):
        import GitFlowSession
        path=self.repository/'owned.meta';path.write_bytes(b'original')
        queue.git(self.repository,'add','owned.meta');queue.git(self.repository,'commit','-m','Owned session output')
        folder=self.workspace_root/'Session';folder.mkdir()
        snapshot=GitFlowSession.Snapshot(queue,self.repository,folder,['owned.meta'])
        path.write_bytes(b'changed and staged')
        queue.git(self.repository,'add','owned.meta')
        self.assertRaises(queue.QueueError,snapshot.restore)
        self.assertEqual(path.read_bytes(),b'changed and staged')
        self.assertTrue((folder/'session-originals.json').exists())
        queue.git(self.repository,'reset','--','owned.meta')
        path.unlink()
        self.assertRaises(queue.QueueError,snapshot.restore)
        self.assertFalse(path.exists())

    def test_native_session_batches_keep_native_filters_unicode_and_raw_originals(self):
        import GitFlowSession
        (self.repository/'.gitattributes').write_text('*.meta text eol=crlf\n*.filtered filter=canonical -text\n')
        (self.repository/'Filter.py').write_text('import sys\nsys.stdout.buffer.write(sys.stdin.buffer.read().replace(b"working",b"canonical"))\n')
        queue.git(self.repository,'config','filter.canonical.clean','"'+sys.executable.replace('\\','/')+'" Filter.py')
        originals={f'Native {number} café.meta':b'native original\r\n' if number%2 else b'native original\n' for number in range(70)}
        originals['Original.filtered']=b'working bytes\r\n'
        for name,data in originals.items():(self.repository/name).write_bytes(data)
        queue.git(self.repository,'add','.');queue.git(self.repository,'commit','-m','Native filter and batch boundary fixture')
        folder=self.workspace_root/'Session';folder.mkdir()
        snapshot=GitFlowSession.Snapshot(queue,self.repository,folder,list(originals))
        for name in ('Original.filtered','Native 0 café.meta'):
            blob=queue.run(['git','hash-object','--stdin','--path='+name],self.repository,data=originals[name]).decode().strip()
            self.assertEqual(queue.read_json(folder/'session-originals.json')['files'][name]['blob'],blob)
            (self.repository/name).write_bytes(b'native session output')
        snapshot.restore()
        self.assertEqual({name:(self.repository/name).read_bytes() for name in originals},originals)

    def test_native_session_capture_race_preserves_the_new_bytes(self):
        import GitFlowSession
        path=self.repository/'owned.meta';path.write_bytes(b'original')
        queue.git(self.repository,'add','owned.meta');queue.git(self.repository,'commit','-m','Capture race fixture')
        folder=self.workspace_root/'Session';folder.mkdir();original=queue.run
        def race(arguments,*args,**kwargs):
            result=original(arguments,*args,**kwargs)
            if arguments[:2]==['git','hash-object']:path.write_bytes(b'concurrent change')
            return result
        with mock.patch.object(queue,'run',side_effect=race):
            self.assertRaisesRegex(queue.QueueError,'changed during capture',GitFlowSession.Snapshot,queue,self.repository,folder,['owned.meta'])
        self.assertEqual(path.read_bytes(),b'concurrent change')
        self.assertEqual((folder/'session-original-0.bin').read_bytes(),b'original')

    def test_repair_model_selection_is_explicit_stopped_and_preserves_gates(self):
        import GitFlowModels
        config=queue.configuration(self.repository)
        config['policy']['repair']=dict(enabled=True,executable='fixture',model='configured',effort='max',window_seconds=10800)
        config['policy_hash']=queue.digest(config['policy'])
        control=queue.common(self.repository)/'gameforge_queue'
        queue.durable(control/'configuration.json',config)
        queue.durable(self.workspace_root/'ownership.json',config)
        catalogue=dict(account_state='signed_in',models=[dict(model='configured',efforts=['high','max'])])
        with mock.patch.object(GitFlowModels,'discover',return_value=catalogue):
            self.assertRaises(queue.QueueError,queue.set_repair_model,self.repository,'0'*64,'configured','high')
            self.assertRaises(queue.QueueError,queue.set_repair_model,self.repository,queue.digest(config),'other','high')
            lease=queue.leases.Lease(queue,self.repository,'origin').acquire()
            self.assertRaises(queue.QueueError,queue.set_repair_model,self.repository,queue.digest(config),'configured','high')
            self.assertRaises(queue.QueueError,queue.set_native_session_outputs,self.repository,queue.digest(config),'compile_project',['session.meta'])
            lease.close()
            result=queue.set_repair_model(self.repository,queue.digest(config),'configured','high')
        updated=queue.configuration(self.repository)
        self.assertEqual(result['effort'],'high')
        self.assertEqual(updated['policy']['checks'],config['policy']['checks'])
        self.assertEqual(updated['policy']['repair'],dict(config['policy']['repair'],effort='high'))
        self.assertRaises(queue.QueueError,queue.set_native_session_outputs,self.repository,queue.digest(updated),'unknown',['session.meta'])
        self.assertRaises(queue.QueueError,queue.set_native_session_outputs,self.repository,queue.digest(updated),'compile_project',['../source'])
        queue.set_native_session_outputs(self.repository,queue.digest(updated),'compile_project',['session.meta'])
        final=queue.configuration(self.repository)
        self.assertEqual(final['policy']['repair'],updated['policy']['repair'])
        self.assertEqual(final['policy']['checks'],[dict(updated['policy']['checks'][0],session_outputs=['session.meta'])])

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


class ReferenceReadAcceptance(unittest.TestCase):
    """Real Git batching, fresh-ref and preservation risks need observable integration checks."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='gqr_')
        self.root = Path(self.temporary.name)
        self.remote = self.root / 'Remote.git'
        self.writer = self.root / 'Writer'
        self.reader = self.root / 'Reader'
        self.addCleanup(self.temporary.cleanup)
        # Some Windows sandboxes inherit Administrator-owned directories. Trust
        # only these three disposable paths, through this test's environment.
        environment = {'GIT_CONFIG_COUNT': '3'}
        for index, path in enumerate((self.writer, self.reader, self.remote)):
            environment['GIT_CONFIG_KEY_' + str(index)] = 'safe.directory'
            environment['GIT_CONFIG_VALUE_' + str(index)] = path.as_posix()
        settings = mock.patch.dict(os.environ, environment)
        settings.start()
        self.addCleanup(settings.stop)
        queue.git(self.root, 'init', '--bare', str(self.remote))
        queue.git(self.root, 'init', '-b', 'main', str(self.writer))
        queue.git(self.writer, 'config', 'user.name', 'Reference read fixture')
        queue.git(self.writer, 'config', 'user.email', 'fixture@example.invalid')
        (self.writer / 'tracked.txt').write_bytes(b'base\r\n')
        queue.git(self.writer, 'add', '.')
        queue.git(self.writer, 'commit', '-m', 'Fixture base')
        queue.git(self.writer, 'remote', 'add', 'origin', str(self.remote))
        queue.git(self.writer, 'push', 'origin', 'main')
        queue.git(self.root, 'clone', '--branch', 'main', str(self.remote), str(self.reader))
        self.base = queue.git(self.writer, 'rev-parse', 'HEAD')

    def tearDown(self):
        self.temporary.cleanup()

    def records(self, count):
        values = []
        tree = queue.git(self.writer, 'rev-parse', 'HEAD^{tree}')
        for index in range(count):
            source = queue.git(self.writer, 'commit-tree', tree, '-p', self.base, '-m', 'Source ' + str(index))
            value = dict(schema=1, id='request_' + source, source=source, state='queued', submitted_utc=str(count - index).zfill(3))
            identity = queue.object_commit(self.writer, source, value, 'Request ' + str(index))
            queue.git(self.writer, 'update-ref', queue.PREFIX + value['id'], identity)
            values.append((identity, value))
        queue.git(self.writer, 'push', 'origin', 'refs/heads/gameforge/queue/*:refs/heads/gameforge/queue/*')
        return values

    def test_batch_reuses_objects_preserves_checkout_and_observes_changed_refs(self):
        records = self.records(18)  # Cross a native blob-batch boundary.
        (self.reader / 'tracked.txt').write_bytes(b'staged\r\n')
        queue.git(self.reader, 'add', 'tracked.txt')
        (self.reader / 'tracked.txt').write_bytes(b'working\r\n')
        fetch_head = self.reader / '.git/FETCH_HEAD'
        fetch_head.write_bytes(b'original FETCH_HEAD\n')
        index = (self.reader / '.git/index').read_bytes()
        references = queue.git(self.reader, 'show-ref')
        with mock.patch.object(queue, 'run', wraps=queue.run) as calls:
            values = queue.entries(self.reader, 'origin')
        commands = [call.args[0] for call in calls.call_args_list]
        self.assertEqual(sum(command[1] == 'fetch' for command in commands), 1)
        self.assertEqual(sum(command[1] == 'ls-remote' for command in commands), 1)
        self.assertEqual(sum(command[1:] == ['cat-file', '--batch'] for command in commands), 2)
        self.assertEqual([row[2]['submitted_utc'] for row in values], sorted(value['submitted_utc'] for _, value in records))
        with mock.patch.object(queue, 'run', wraps=queue.run) as calls:
            self.assertEqual(queue.entries(self.reader, 'origin'), values)
            self.assertEqual(queue.remote_ref(self.reader, 'origin', 'refs/heads/main'), self.base)
        self.assertFalse(any(call.args[0][1] == 'fetch' for call in calls.call_args_list))
        self.assertEqual(sum(call.args[0][1] == 'ls-remote' for call in calls.call_args_list), 2)
        selected_ids = [records[0][1]['id'], records[1][1]['id']]
        with mock.patch.object(queue, 'run', wraps=queue.run) as calls:
            selected = queue.entries(self.reader, 'origin', selected_ids)
            self.assertEqual(queue.entries(self.reader, 'origin', []), [])
        self.assertEqual({row[2]['id'] for row in selected}, set(selected_ids))
        commands = [call.args[0] for call in calls.call_args_list]
        self.assertEqual(sum(command[1] == 'ls-remote' for command in commands), 1)
        self.assertFalse(any(command[1] == 'fetch' for command in commands))
        self.assertFalse(any(queue.PREFIX + '*' in command for command in commands))
        original, value = records[0]
        changed = dict(value, state='blocked')
        successor = queue.object_commit(self.writer, original, changed, 'Changed request')
        queue.git(self.writer, 'push', 'origin', successor + ':' + queue.PREFIX + value['id'])
        with mock.patch.object(queue, 'run', wraps=queue.run) as calls:
            current = {row[2]['id']: row for row in queue.entries(self.reader, 'origin')}
        self.assertEqual(sum(call.args[0][1] == 'fetch' for call in calls.call_args_list), 1)
        self.assertEqual(current[value['id']][0], successor)
        self.assertEqual(current[value['id']][2]['state'], 'blocked')
        self.assertEqual(fetch_head.read_bytes(), b'original FETCH_HEAD\n')
        self.assertEqual((self.reader / '.git/index').read_bytes(), index)
        self.assertEqual((self.reader / 'tracked.txt').read_bytes(), b'working\r\n')
        self.assertEqual(queue.git(self.reader, 'rev-parse', 'HEAD'), self.base)
        self.assertEqual(queue.git(self.reader, 'show-ref'), references)

    def test_batch_refuses_invalid_objects_without_a_control_blob_size_ceiling(self):
        identity, _ = self.records(1)[0]
        queue.commit_objects(self.reader, 'origin', [identity])
        with mock.patch.object(queue, 'MAXIMUM_BYTES', 8), mock.patch.object(queue, 'run', wraps=queue.run) as calls:
            self.assertEqual(len(queue.entries(self.reader, 'origin')), 1)
        self.assertTrue(any(call.args[0][1:] == ['cat-file', '--batch'] for call in calls.call_args_list))
        blob = queue.git(self.reader, 'rev-parse', 'HEAD:tracked.txt')
        with mock.patch.object(queue, 'run', wraps=queue.run) as calls:
            self.assertRaisesRegex(queue.QueueError, 'not a commit', queue.commit_objects, self.reader, 'origin', [blob])
            self.assertRaisesRegex(queue.QueueError, 'exact SHA-1', queue.commit_objects, self.reader, 'origin', ['unknown'])
        self.assertFalse(any(call.args[0][1] == 'fetch' for call in calls.call_args_list))
        diagnostic = queue.failure_diagnostic(queue.QueueError('token=DO_NOT_DISPLAY arbitrary-private-output'), 'password=DO_NOT_DISPLAY')
        self.assertNotIn('DO_NOT_DISPLAY', queue.encoded(diagnostic))
        self.assertNotIn('arbitrary-private-output', queue.encoded(diagnostic))
        self.assertEqual(diagnostic['stage'], 'unknown')
        from GitFlowObserve import projection
        visible = projection(dict(diagnostic=diagnostic, failure='token=DO_NOT_DISPLAY'))
        self.assertEqual(visible['diagnostic']['cause_code'], 'operation_failed')
        self.assertNotIn('DO_NOT_DISPLAY', queue.encoded(visible))


if __name__ == '__main__':
    unittest.main(verbosity=2)
