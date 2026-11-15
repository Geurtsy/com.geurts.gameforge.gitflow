"""Real-Git controls for shorter leases with unchanged large-publication allowance."""
import datetime
import unittest
from unittest import mock

import GitFlowQueue as q
import GitFlowLease as leases
from TestGitFlowQueue import QueueAcceptance


class TimingAcceptance(unittest.TestCase):
    setUp = QueueAcceptance.setUp
    tearDown = QueueAcceptance.tearDown

    def owner(self, clock):
        q.git(self.repository, 'update-ref', 'refs/remotes/origin/main', self.base)
        return leases.Lease(q, self.repository, 'origin', clock=lambda: clock[0]).acquire()

    def test_old_record_duration_is_never_shortened_by_new_default(self):
        parent = self.base
        for duration in (1800, 180, leases.SECONDS):
            with self.subTest(recorded_seconds=duration):
                clock = [0.0]
                metadata = dict(lease_protocol=1, state='owned', owner='f'*32, host='OtherHost',
                                lease_seconds=duration, updated_utc='2099-01-01T00:00:00+00:00')
                child = q.object_commit(self.repository, parent, metadata, 'Compatible prior lease')
                q.git(self.repository, 'push', 'origin', child + ':' + q.LOCK)
                observer = leases.Lease(q, self.repository, 'origin', clock=lambda: clock[0])
                self.assertRaises(leases.Contended, observer.acquire)
                clock[0] = min(leases.SECONDS, duration) - 0.01
                self.assertRaises(leases.Contended, observer.acquire)
                clock[0] = duration - 0.01
                self.assertRaises(leases.Contended, observer.acquire)
                clock[0] = duration
                observer.acquire()
                record = q.read_request(self.repository, observer.oid)
                self.assertEqual(record['lease_seconds'], 600)
                observer.close()
                parent = observer.oid

    def test_healthy_renewal_resets_short_takeover_window_and_old_owner_is_fenced(self):
        clock = [0.0]
        owner = self.owner(clock)
        observed = [0.0]
        contender = leases.Lease(q, self.repository, 'origin', clock=lambda: observed[0])
        self.assertRaises(leases.Contended, contender.acquire)
        observed[0] = 599.9
        self.assertRaises(leases.Contended, contender.acquire)
        owner.advance()
        observed[0] = 600.0
        self.assertRaises(leases.Contended, contender.acquire)
        observed[0] = 1199.9
        self.assertRaises(leases.Contended, contender.acquire)
        observed[0] = 1200.0
        contender.acquire()
        self.assertRaises(leases.LeaseError, owner.advance)
        self.assertEqual(q.remote_ref(self.repository, 'origin', q.LOCK), contender.oid)
        contender.close()

    def test_large_publication_renews_first_and_retains_full_five_minute_transport(self):
        clock = [0.0]
        owner = self.owner(clock)
        clock[0] = 300.0
        real = leases.transport
        writes = []
        target = 'refs/heads/gameforge/timing-fixture'
        def record(api, root, *arguments, **options):
            if arguments[0] == 'push':
                writes.append(dict(arguments=arguments, timeout=options.get('timeout', leases.TRANSPORT_TIMEOUT), remaining=owner.deadline-clock[0]))
            return real(api, root, *arguments, **options)
        with mock.patch.object(leases, 'transport', side_effect=record):
            owner.publish(self.repository, 'origin', [self.base+':'+target])
        self.assertEqual(len(writes), 2)
        self.assertEqual(writes[0]['timeout'], leases.COORDINATION_TIMEOUT)
        self.assertFalse(any(target in arg for arg in writes[0]['arguments']))
        self.assertEqual(writes[1]['timeout'], 300)
        self.assertGreaterEqual(writes[1]['remaining'], 300+leases.VERIFICATION_BUDGET)
        self.assertEqual(q.remote_ref(self.repository, 'origin', target), self.base)
        owner.close()

    def test_late_delivery_verification_cannot_revive_old_deadline(self):
        clock = [0.0]
        owner = self.owner(clock)
        deadline = owner.deadline
        real = leases.transport
        writes = []
        target = 'refs/heads/gameforge/late-timing-fixture'
        def late(api, root, *arguments, **options):
            result = real(api, root, *arguments, **options)
            if arguments[0] == 'push':
                writes.append(arguments)
            elif arguments[0] == 'fetch':
                clock[0] = deadline
            return result
        with mock.patch.object(leases, 'transport', side_effect=late):
            self.assertRaises(leases.LeaseError, owner.publish, self.repository, 'origin', [self.base+':'+target])
        self.assertEqual(len(writes), 1)
        self.assertEqual(owner.deadline, deadline)
        self.assertTrue(owner.failure)
        self.assertRaises(leases.LeaseError, owner.advance)


if __name__ == '__main__':
    unittest.main(verbosity=2)
