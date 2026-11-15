"""Real-Git verification reads and same-service fenced recovery; no live fault injection."""
import json
from pathlib import Path
import unittest
from unittest import mock

import GitFlowQueue as q
import GitFlowLease as leases
from TestGitFlowQueue import QueueAcceptance


class OwnershipAcceptance(unittest.TestCase):
    setUp = QueueAcceptance.setUp
    tearDown = QueueAcceptance.tearDown
    source = QueueAcceptance.source

    def owner(self):
        q.git(self.repository, 'update-ref', 'refs/remotes/origin/main', self.base)
        return leases.Lease(q, self.repository, 'origin').acquire()

    def test_one_failed_fetch_recovers_without_repeating_heartbeat_push(self):
        owner = self.owner()
        real = leases.transport
        counts = dict(fetch=0, push=0)
        def once(api, root, *arguments, **options):
            if arguments[0] in counts:
                counts[arguments[0]] += 1
            if arguments[0] == 'fetch' and counts['fetch'] == 1:
                raise leases.Rejected('Injected exit 128; no output')
            return real(api, root, *arguments, **options)
        with mock.patch.object(leases, 'transport', side_effect=once):
            owner.advance()
        self.assertEqual(counts, dict(fetch=2, push=1))
        self.assertFalse(owner.failure)
        proof = q.read_json(q.common(self.repository) / 'gameforge_queue' / ('verification_' + owner.oid + '.json'))
        self.assertEqual(len(proof['read_failures']), 1)
        self.assertEqual(proof['matched'], 'exact_child')
        self.assertEqual(proof['observations'], [owner.oid, owner.oid])
        owner.close()

    def test_changed_owner_during_read_retry_is_never_accepted(self):
        owner = self.owner()
        real = leases.transport
        failures = []
        def moved(api, root, *arguments, **options):
            if arguments[0] == 'fetch' and not failures:
                failures.append(True)
                held = q.remote_ref(root, 'origin', q.LOCK)
                value = owner.metadata('owned', 'competing owner')
                value['owner'] = 'f' * 32
                child = q.object_commit(root, held, value, 'Competing owner during read')
                real(api, root, 'push', 'origin', child + ':' + q.LOCK)
                raise leases.Rejected('Injected fetch interruption')
            return real(api, root, *arguments, **options)
        with mock.patch.object(leases, 'transport', side_effect=moved):
            self.assertRaises(leases.LeaseError, owner.advance)
        self.assertFalse(owner.verification['retryable_read_failure'])
        self.assertTrue(owner.stop.is_set())
        self.assertRaises(leases.LeaseError, owner.advance)

    def test_failed_read_cannot_extend_verification_budget(self):
        owner = self.owner()
        real = leases.transport
        clock = [0.0]
        fetches = []
        def late(api, root, *arguments, **options):
            if arguments[0] == 'fetch':
                fetches.append(arguments)
                clock[0] = leases.VERIFICATION_BUDGET + 1
                raise leases.Rejected('Read exhausted the original verification allowance')
            return real(api, root, *arguments, **options)
        with mock.patch.object(leases.time, 'monotonic', side_effect=lambda: clock[0]), mock.patch.object(leases, 'transport', side_effect=late):
            self.assertRaises(leases.LeaseError, leases.verify_child, q, self.repository, 'origin', owner.oid, expected_owner=owner.owner)
        self.assertEqual(len(fetches), 1)
        owner.close()

    def service_case(self, tamper=False):
        value = self.source()
        real_lease = leases.Lease
        real_transport = leases.transport
        real_gate = q.execute_gate
        real_process = q.process_once
        clock = [0.0]
        owners = []
        failures = []
        calls = []
        def factory(*args, **kwargs):
            owner = real_lease(*args, **dict(kwargs, clock=lambda: clock[0]))
            owners.append(owner)
            return owner
        def broken_fetch(api, root, *arguments, **options):
            if arguments[0] == 'fetch':
                failures.append(arguments)
                raise leases.Rejected('Injected heartbeat verification fetch failure')
            return real_transport(api, root, *arguments, **options)
        def gate(*args, **kwargs):
            calls.append(args[3])
            if len(calls) == 1:
                with mock.patch.object(leases, 'transport', side_effect=broken_fetch):
                    leases.ACTIVE.get().advance()
            return real_gate(*args, **kwargs)
        interrupted = []
        preserved = []
        def wait(seconds):
            clock[0] += leases.SECONDS / 30
            if tamper and interrupted and not preserved:
                workspace = self.workspace_root / 'Workspace'
                (workspace / 'feature.txt').write_bytes(b'concurrent staged source\r\n')
                preserved.append(True)
                q.git(workspace, 'add', 'feature.txt')
        def process(*args, **kwargs):
            if interrupted and interrupted[-1] == 'ownership':
                interrupted.append('access')
                error = q.QueueError('Injected unavailable Git read')
                error.remote_read = True
                raise error
            try:
                result = real_process(*args, **kwargs)
            except q.OwnershipRecovery:
                interrupted.append('ownership')
                raise
            if result['state'] == 'integrated':
                q.durable(q.common(self.repository) / 'gameforge_queue' / 'stop.json', dict(schema=1))
            return result
        with mock.patch.object(leases, 'Lease', side_effect=factory), mock.patch.object(real_lease, 'start'), \
                mock.patch.object(q, 'execute_gate', side_effect=gate), mock.patch.object(q, 'process_once', side_effect=process), \
                mock.patch.object(q.time, 'sleep', side_effect=wait):
            q.serve(self.repository)
        integrated = q.entries(self.repository, 'origin')[0][2]
        self.assertEqual(integrated['id'], value['id'])
        if tamper:
            self.assertEqual(integrated['state'], 'validating')
            self.assertEqual(q.remote_ref(self.repository, 'origin', 'refs/heads/main'), self.base)
            self.assertEqual((self.workspace_root / 'Workspace/feature.txt').read_bytes(), b'concurrent staged source\r\n')
            self.assertEqual(len(calls), 1)
            state = q.read_json(q.common(self.repository) / 'gameforge_queue/state.json')
            self.assertEqual(state['state'], 'blocked')
            self.assertIn('Candidate/index changed', state['detail'])
            return
        self.assertEqual(integrated['state'], 'integrated')
        self.assertEqual(integrated['version'], '1.0.1')
        self.assertEqual(len(integrated['attempts']), 2)
        self.assertEqual(len(set(calls)), 1)
        self.assertEqual(len(failures), 2)
        self.assertEqual(interrupted, ['ownership', 'access'])
        self.assertTrue(owners[0].closed)
        self.assertTrue(owners[0].failure)
        self.assertNotEqual(owners[0].owner, owners[-1].owner)
        self.assertGreaterEqual(clock[0], leases.SECONDS)
        self.assertRaises(leases.LeaseError, owners[0].advance)
        self.assertTrue(q.ancestor(self.repository, value['source'], integrated['main_commit']))
        self.assertTrue(list((q.common(self.repository) / 'gameforge_queue').glob('ownership_recovered_*.json')))

    def test_service_survives_failed_heartbeat_reads_and_access_loss_then_delivers_original(self):
        self.service_case()

    def test_changed_index_stops_automatic_continuation_and_preserves_source(self):
        self.service_case(tamper=True)


if __name__ == '__main__':
    unittest.main(verbosity=2)
