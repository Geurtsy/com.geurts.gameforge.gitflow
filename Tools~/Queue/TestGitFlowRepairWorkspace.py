"""Real-Git checks for broad repair, preserved candidates and independent validation."""
import datetime
import json
from pathlib import Path
import subprocess
import unittest
from unittest import mock

import GitFlowQueue as queue
import GitFlowRepair as repair
import GitFlowModels as models
import TestGitFlowQueue as fixtures


class ProjectRepairAcceptance(unittest.TestCase):
    setUp = fixtures.QueueAcceptance.setUp
    tearDown = fixtures.QueueAcceptance.tearDown
    source = fixtures.QueueAcceptance.source

    def inputs(self):
        (self.repository / '.gitattributes').write_bytes(b'* text=auto\n')
        queue.git(self.repository, 'add', '.gitattributes')
        queue.git(self.repository, 'commit', '-m', 'Declare fixture text normalization')
        value = self.source('failure.txt', 'failed\n')
        policy = dict(self.policy, repair=dict(enabled=True, executable='fixture', model='selected-model',
                                              effort='high', window_seconds=10800))
        evidence = self.workspace_root / 'Evidence' / value['id']
        return value, policy, evidence

    @staticmethod
    def response(review=False, **values):
        proposal = dict(approved=True, reason='Necessary repair; checks unchanged.', limitations='') if review else dict(status='changed', summary='Saved repair.', limitations='')
        proposal.update(values)
        return dict(model='selected-model', effort='high', model_invoked=False, proposal=proposal)

    def test_large_project_files_create_delete_rename_binary_and_more_than_sixteen_edits(self):
        value, policy, evidence = self.inputs()
        original = (self.repository / 'feature.txt').read_bytes()
        payload = b'// complete large shader\n' + b'x' * (2 * 1024 * 1024)
        paths = ['Assets/Editor/Fixed.cs', 'Assets/_ThirdParty/Fixed.shader', 'Packages/local/Fixed.cs',
                 'ProjectSettings/Local.json', 'Assets/Testing/Regression.cs']
        paths += ['Assets/Extra' + str(i) + '.shader' for i in range(20)]
        def transport(settings, context, workspace, folder, budget, review=False):
            self.assertGreater(budget, 0)
            if review:
                self.assertEqual((self.repository / 'feature.txt').read_bytes(), original)
                return self.response(True)
            for relative in paths:
                path = workspace / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(payload if relative.endswith('.shader') else b'complete replacement\n')
            (workspace / 'feature.txt').rename(workspace / 'Renamed.txt')
            (workspace / 'failure.txt').unlink()
            (workspace / 'Binary.bin').write_bytes(b'\0binary replacement\xff')
            return self.response()
        head = repair.repair(queue, self.repository, value, policy, queue.QueueError('shader failure'), evidence, lambda: None, mock.Mock(), transport)
        self.assertEqual(queue.git(self.repository, 'rev-parse', 'HEAD'), head)
        self.assertEqual((self.repository / paths[1]).read_bytes(), payload)
        self.assertEqual((self.repository / 'Renamed.txt').read_bytes(), original)
        self.assertFalse((self.repository / 'failure.txt').exists())
        self.assertEqual((self.repository / 'Binary.bin').read_bytes(), b'\0binary replacement\xff')
        self.assertGreater(len(value['repair']['attempts'][0]['paths']), 16)
        self.assertEqual(queue.git(self.repository, 'status', '--porcelain'), '')

    def test_protected_gate_changes_are_rejected_before_original_mutation(self):
        value, policy, evidence = self.inputs()
        before = (self.repository / 'Gate.py').read_bytes()
        def transport(settings, context, workspace, folder, budget, review=False):
            (workspace / 'Gate.py').write_text('raise SystemExit(0)\n')
            return self.response()
        self.assertRaisesRegex(queue.QueueError, 'independent validation', repair.repair_attempt,
                               queue, self.repository, value, policy, queue.QueueError('failure'), evidence, lambda: None, mock.Mock(), transport)
        self.assertEqual((self.repository / 'Gate.py').read_bytes(), before)

    def test_independent_review_rejection_keeps_candidate_and_artifacts(self):
        value, policy, evidence = self.inputs()
        head = queue.git(self.repository, 'rev-parse', 'HEAD')
        def transport(settings, context, workspace, folder, budget, review=False):
            if review:
                return self.response(True, approved=False, reason='Unrelated deletion.')
            (workspace / 'feature.txt').unlink()
            return self.response()
        self.assertRaises(repair.RetryableNoProgress, repair.repair_attempt, queue, self.repository,
                          value, policy, queue.QueueError('failure'), evidence, lambda: None, mock.Mock(), transport)
        self.assertEqual(queue.git(self.repository, 'rev-parse', 'HEAD'), head)
        self.assertEqual((self.repository / 'feature.txt').read_text(), 'base\n')
        self.assertTrue((evidence / 'repair_1/review.json').exists())

    def test_concurrent_candidate_change_is_preserved_and_not_overwritten(self):
        value, policy, evidence = self.inputs()
        def transport(settings, context, workspace, folder, budget, review=False):
            if review:
                (self.repository / 'feature.txt').write_text('concurrent author\n')
                return self.response(True)
            (workspace / 'feature.txt').write_bytes(b'repair\n')
            return self.response()
        self.assertRaisesRegex(queue.QueueError, 'Candidate changed', repair.repair_attempt, queue, self.repository,
                               value, policy, queue.QueueError('failure'), evidence, lambda: None, mock.Mock(), transport)
        self.assertEqual((self.repository / 'feature.txt').read_text(), 'concurrent author\n')

    def test_more_than_three_no_progress_attempts_keep_original_budget(self):
        value, policy, evidence = self.inputs()
        calls = []
        def transport(settings, context, workspace, folder, budget, review=False):
            if not review:
                calls.append(budget)
                if len(calls) == 5:
                    (workspace / 'failure.txt').write_text('fixed\n')
            return self.response(review)
        repair.repair(queue, self.repository, value, policy, queue.QueueError('failure'), evidence, lambda: None, mock.Mock(), transport)
        self.assertEqual(len(calls), 5)
        self.assertEqual(len(value['repair']['attempts']), 5)
        start = datetime.datetime.fromisoformat(value['repair']['first_started_utc'])
        self.assertEqual(repair.remaining(value['repair'], start + datetime.timedelta(hours=3)), 0)
        self.assertEqual(value['repair']['consecutive_no_progress'], 0)

    def test_interrupted_and_expired_repairs_cannot_restart_the_clock(self):
        value, policy, evidence = self.inputs()
        start = datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(hours=4)
        value['repair'] = dict(window_seconds=10800, first_started_utc=start.isoformat(), attempts=[], seen=[])
        transport = mock.Mock()
        self.assertRaisesRegex(queue.QueueError, 'exhausted', repair.repair, queue, self.repository, value, policy,
                               queue.QueueError('failure'), evidence, lambda: None, mock.Mock(), transport)
        value['repair']['first_started_utc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        value['repair']['attempts'] = [dict(state='applying')]
        self.assertRaisesRegex(queue.QueueError, 'Interrupted', repair.repair, queue, self.repository, value, policy,
                               queue.QueueError('failure'), evidence, lambda: None, mock.Mock(), transport)
        transport.assert_not_called()

    def test_disk_evidence_and_model_transport_have_no_previous_size_or_ten_minute_ceiling(self):
        settings = dict(executable='fixture', model='unavailable', effort='high', fallbacks=[dict(model='available', effort='high')])
        catalogue = dict(account_state='signed_in', models=[dict(model='available', efforts=['high'])])
        folder = self.root / 'Transport'; folder.mkdir()
        context = dict(failure='diagnostics' * 200000)
        def run(arguments, **options):
            self.assertEqual(arguments[arguments.index('--sandbox') + 1], 'workspace-write')
            self.assertIn('shell_tool', arguments)
            self.assertGreater(options['timeout'], 600)
            self.assertGreater(len(options['input']), 1024 * 1024)
            response = dict(status='changed', summary='s' * (300 * 1024), limitations='')
            Path(arguments[arguments.index('-o') + 1]).write_text(json.dumps(response))
            return subprocess.CompletedProcess(arguments, 0)
        with mock.patch.object(models, 'discover', return_value=catalogue), mock.patch.object(models, 'executable', return_value='fixture'), mock.patch.object(models, 'process_run', side_effect=run):
            result = models.repair_project(settings, context, folder, folder, 7200)
        self.assertEqual(result['model'], 'available')
        self.assertGreater(len(result['proposal']['summary']), 256 * 1024)

    def test_repaired_request_runs_unchanged_gates_and_retains_source_ancestry(self):
        value = self.source('fail.txt', 'fail\n')
        config = queue.configuration(self.repository)
        config['policy']['repair'] = dict(enabled=True, executable='fixture', model='selected-model', effort='high', window_seconds=10800)
        config['policy_hash'] = queue.digest(config['policy'])
        queue.durable(queue.common(self.repository) / 'gameforge_queue/configuration.json', config)
        queue.durable(self.workspace_root / 'ownership.json', config)
        gate = (self.repository / 'Gate.py').read_bytes()
        def transport(settings, context, workspace, folder, budget, review=False):
            if not review:
                (workspace / 'fail.txt').unlink()
            return self.response(review)
        with mock.patch.object(models, 'repair_project', side_effect=transport):
            result = queue.process_once(self.repository)
        self.assertEqual(result['state'], 'integrated')
        self.assertTrue(queue.ancestor(self.repository, value['source'], result['main_commit']))
        self.assertEqual((self.workspace_root / 'Workspace/Gate.py').read_bytes(), gate)
        self.assertTrue((self.repository / 'fail.txt').exists(), 'Worker checkout must remain intact.')
        self.assertEqual(result['receipt']['checks'][0]['name'], 'compile_project')

    def test_native_overlay_bytes_survive_review_and_commit(self):
        value, policy, evidence = self.inputs()
        payload = b'%YAML 1.1\r\n--- !u!114 &1\r\n  m_EditorClassIdentifier: \r\n'
        def transport(settings, context, workspace, folder, budget, review=False):
            if not review:
                target = workspace / 'Packages/Prepared/Editor/Assets/Default.overlay'
                target.parent.mkdir(parents=True)
                target.write_bytes(payload)
            return self.response(review)
        repair.repair(queue, self.repository, value, policy, queue.QueueError('failure'), evidence, lambda: None, mock.Mock(), transport)
        self.assertEqual((self.repository / 'Packages/Prepared/Editor/Assets/Default.overlay').read_bytes(), payload)
        queue.clean(self.repository)

    def test_whitespace_rejection_retries_before_mutation_and_then_runs_required_gates(self):
        value = self.source('fail.txt', 'fail\n')
        config = queue.configuration(self.repository)
        config['policy']['repair'] = dict(enabled=True, executable='fixture', model='selected-model', effort='high', window_seconds=10800)
        config['policy_hash'] = queue.digest(config['policy'])
        queue.durable(queue.common(self.repository) / 'gameforge_queue/configuration.json', config)
        queue.durable(self.workspace_root / 'ownership.json', config)
        proposals, reviews, baseline = [], [], []
        def transport(settings, context, workspace, folder, budget, review=False):
            if review:
                reviews.append(True)
                return self.response(True)
            proposals.append(context)
            candidate = self.workspace_root / 'Workspace'
            import GitFlowRepairWorkspace as artifacts
            if not baseline:
                baseline.append(artifacts.identity(queue, candidate))
            self.assertEqual(artifacts.identity(queue, candidate), baseline[0])
            self.assertTrue((candidate / 'fail.txt').is_file())
            (workspace / 'fail.txt').unlink()
            (workspace / 'Repair.cs').write_bytes(b'// correction' + (b'  \n' if len(proposals) == 1 else b'\n'))
            if len(proposals) == 2:
                self.assertIn('trailing whitespace', context['history'][0]['reason'])
            return self.response()
        with mock.patch.object(models, 'repair_project', side_effect=transport):
            result = queue.process_once(self.repository)
        self.assertEqual(result['state'], 'integrated')
        self.assertEqual(len(proposals), 2)
        self.assertEqual(len(reviews), 1, 'Invalid authored whitespace must be rejected before independent review.')
        attempts = result['repair']['attempts']
        self.assertEqual(attempts[0]['state'], 'preflight_rejected_candidate_unchanged')
        self.assertEqual(attempts[1]['state'], 'applied_pending_validation')
        self.assertLess(attempts[1]['remaining_seconds'], attempts[0]['remaining_seconds'])
        self.assertTrue({'snapshot_seconds', 'proposal_seconds', 'review_seconds', 'application_seconds'} <= attempts[1]['timings'].keys())
        self.assertTrue(all(seconds >= 0 for seconds in attempts[1]['timings'].values()))
        self.assertEqual(result['receipt']['checks'][0]['name'], 'compile_project')
        self.assertTrue(queue.ancestor(self.repository, value['source'], result['main_commit']))

    def test_preflight_checks_staged_source_outside_proposal_without_changing_real_index(self):
        value, policy, evidence = self.inputs()
        (self.repository / 'Outside.cs').write_bytes(b'// staged task source  \n')
        queue.git(self.repository, 'add', 'Outside.cs')
        import GitFlowRepairWorkspace as artifacts
        baseline = artifacts.identity(queue, self.repository)
        original_failure = (self.repository / 'failure.txt').read_bytes()
        def transport(settings, context, workspace, folder, budget, review=False):
            self.assertFalse(review)
            (workspace / 'failure.txt').write_bytes(b'fixed\n')
            return self.response()
        self.assertRaises(repair.RetryableNoProgress, repair.repair_attempt, queue, self.repository,
                          value, policy, queue.QueueError('failure'), evidence, lambda: None, mock.Mock(), transport)
        self.assertEqual(artifacts.identity(queue, self.repository), baseline)
        self.assertEqual((self.repository / 'failure.txt').read_bytes(), original_failure)
        self.assertIn('Outside.cs', value['repair']['attempts'][0]['no_progress_reason'])


if __name__ == '__main__':
    unittest.main()
