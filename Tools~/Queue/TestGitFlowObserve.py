"""Focused observation regressions: bounds, preservation and unknown/partial state are material UI risks."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import GitFlowObserve as observation
import GitFlowQueue as queue


class ObservationAcceptance(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='gfo_test_', dir=str(Path(__file__).resolve().parents[3]))
        self.root = Path(self.temporary.name)
        self.repository = self.root / 'Source'
        self.remote = self.root / 'Remote.git'
        self.repository.mkdir()
        queue.git(self.repository, 'init', '--template=', '-b', 'main')
        queue.git(self.root, 'init', '--template=', '--bare', str(self.remote))
        for root in (self.repository, self.remote):
            queue.git(root, 'config', 'user.name', 'Disposable observation fixture')
            queue.git(root, 'config', 'user.email', 'fixture@example.invalid')
        (self.repository / 'source.txt').write_text('preserved\n')
        queue.git(self.repository, 'add', '.')
        queue.git(self.repository, 'commit', '-m', 'Fixture base')
        queue.git(self.repository, 'remote', 'add', 'origin', str(self.remote))
        queue.git(self.repository, 'push', 'origin', 'main')
        self.base = queue.git(self.repository, 'rev-parse', 'HEAD')

    def tearDown(self):
        self.temporary.cleanup()

    def request(self, index, state='queued', **extra):
        source = format(index, '040x')
        value = dict(schema=1, id='request_' + source, source=source, state=state, submitted_utc='2026-11-08T00:00:00+00:00', title='Fixture request', **extra)
        identity = queue.object_commit(self.remote, self.base, value, 'Fixture request')
        queue.git(self.remote, 'update-ref', queue.PREFIX + value['id'], identity)
        return identity

    def fingerprint(self, root):
        return {str(path.relative_to(root)): queue.file_digest(path) for path in root.rglob('*') if path.is_file()}

    def test_large_pages_and_repeated_reads_preserve_selected_checkout_and_remote(self):
        for index in range(140):
            self.request(index)
        before_source, before_remote = self.fingerprint(self.repository), self.fingerprint(self.remote)
        first = observation.observe(queue, self.repository, 'origin', 0, 16)
        last = observation.observe(queue, self.repository, 'origin', 128, 16)
        again = observation.observe(queue, self.repository, 'origin', 0, 16)
        self.assertEqual(first['coverage'], again['coverage'])
        self.assertEqual(first['state'], 'partial')
        self.assertEqual(first['coverage']['advertised'], 140)
        self.assertEqual(first['coverage']['returned'], 16)
        self.assertEqual(first['coverage']['next_offset'], 16)
        self.assertEqual(last['coverage']['returned'], 12)
        self.assertIsNone(last['coverage']['next_offset'])
        self.assertFalse(set(item['id'] for item in first['requests']) & set(item['id'] for item in last['requests']))
        self.assertEqual(before_source, self.fingerprint(self.repository))
        self.assertEqual(before_remote, self.fingerprint(self.remote))

    def test_legacy_future_malformed_and_unsafe_payloads_remain_partial(self):
        identity = self.request(1, state='future_state', failure='password=DO_NOT_DISPLAY', command=['secret script'], secret='DO_NOT_DISPLAY', attempts=[dict(candidate=self.base)] * 100)
        queue.git(self.remote, 'update-ref', 'refs/heads/codex/queue/superseded_fixture', identity)
        malformed = queue.object_commit(self.remote, self.base, dict(schema=99, id='request_' + '2' * 40), 'Future fixture')
        queue.git(self.remote, 'update-ref', queue.PREFIX + 'request_' + '2' * 40, malformed)
        result = observation.observe(queue, self.repository, 'origin')
        self.assertEqual(result['state'], 'partial')
        self.assertEqual(result['coverage']['failed'], 1)
        self.assertEqual(result['legacy']['state'], 'unsupported_existing_queue')
        self.assertEqual(result['legacy']['requests'], 1)
        output = queue.encoded(result)
        self.assertNotIn('DO_NOT_DISPLAY', output)
        self.assertNotIn('secret script', output)
        self.assertIn('Queue operation blocked or failed (reported)', output)
        self.assertIn('Preserve this request ID', output)
        self.assertLess(len(output.encode()), observation.MAXIMUM_OUTPUT)
        self.assertIn('[older entries omitted]', output)
        self.assertEqual(result['owner']['process_health'], 'unknown')

    def test_malformed_object_shapes_are_reported_without_replacing_original_bytes(self):
        control = self.repository / '.git/gameforge_queue'
        for malformed in ([], None, {'schema': 1, 'policy': {'repair': [], 'checks': [None]}}):
            queue.durable(control / 'configuration.json', malformed)
            queue.durable(control / 'state.json', [])
            result = observation.observe(queue, self.repository, 'origin')
            self.assertEqual(result['setup']['state'], 'invalid_configuration')
            self.assertEqual(result['state'], 'partial')
            self.assertTrue(result['findings'])
            self.assertEqual(queue.read_json(control / 'configuration.json'), malformed)

    def test_actual_cli_dispatch_pages_and_refuses_changed_review_without_setup(self):
        self.request(4, state='validation_failed', failure='private raw compiler output', pulls={'verified_main': self.base}, receipt={'checks': [{'evidence_candidate': self.base}]})
        script = str(Path(queue.__file__).resolve())
        result = queue.process_run([sys.executable, script, '--repository', str(self.repository), '--remote', 'origin', 'inspect', '--limit', '1'], capture_output=True, timeout=60, **queue.console_options())
        self.assertEqual(result.returncode, 0, result.stderr.decode())
        value = json.loads(result.stdout)
        self.assertEqual(value['coverage']['returned'], 1)
        self.assertEqual(value['requests'][0]['pulls']['verified_main'], self.base)
        self.assertEqual(value['requests'][0]['receipt']['checks'][0]['evidence_candidate'], self.base)
        self.assertEqual(observation.projection({'pid': 123, 'process_id': 'secret', 'verified_main': 'secret'}), {'pid': 123})
        self.assertIn('Required validation failed (reported)', queue.encoded(value))
        path = self.root / 'policy.json'
        path.write_bytes(b'{"schema":1}')
        expected = queue.digest(path.read_bytes())
        path.write_bytes(b'{"schema":2}')
        result = queue.process_run([sys.executable, script, '--repository', str(self.repository), 'configure', '--operator', '--workspace-root', str(self.root / 'NeverCreated'), '--policy', str(path), '--policy-sha256', expected], capture_output=True, timeout=60, **queue.console_options())
        self.assertEqual(result.returncode, 1)
        self.assertEqual(json.loads(result.stdout)['state'], 'blocked')
        self.assertFalse((self.root / 'NeverCreated').exists())
        self.assertFalse((self.repository / '.git/gameforge_queue').exists())

    def test_reviewed_policy_is_bound_to_exact_bytes_before_setup(self):
        path = self.root / 'policy.json'
        path.write_bytes(b'{"schema":1}')
        expected = queue.digest(path.read_bytes())
        self.assertEqual(queue.reviewed_policy(path, expected), {'schema': 1})
        path.write_bytes(b'{"schema":2}')
        self.assertRaises(queue.QueueError, queue.reviewed_policy, path, expected)
        self.assertFalse((self.repository / '.git/gameforge_queue').exists())

    def test_interrupted_remote_read_retains_independent_setup_without_false_empty_success(self):
        control = self.repository / '.git/gameforge_queue'
        queue.durable(control / 'configuration.json', dict(schema=99, private='DO_NOT_DISPLAY'))
        real = queue.process_run
        def interrupt(arguments, **keywords):
            if 'ls-remote' in arguments:
                raise subprocess.TimeoutExpired(arguments[0], 1)
            return real(arguments, **keywords)
        with mock.patch.object(queue, 'process_run', side_effect=interrupt):
            result = observation.observe(queue, self.repository, 'origin')
        self.assertEqual(result['state'], 'unavailable')
        self.assertEqual(result['setup']['state'], 'invalid_configuration')
        self.assertEqual(result['legacy']['state'], 'not_observed')
        self.assertTrue(result['findings'])
        self.assertEqual(queue.read_json(control / 'configuration.json')['private'], 'DO_NOT_DISPLAY')
        self.assertNotIn('DO_NOT_DISPLAY', queue.encoded(result))
        self.assertRaises(queue.QueueError, observation.observe, queue, self.repository, 'origin', 0, 65)
        self.assertRaises(ValueError, observation.object_json, '{"state":"queued","state":"integrated"}')


if __name__ == '__main__':
    unittest.main()
