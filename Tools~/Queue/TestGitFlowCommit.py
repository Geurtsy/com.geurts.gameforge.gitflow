"""Real-Git acceptance for default commits, exact submission and preservation on failure."""
import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest import mock

import GitFlowQueue as queue
import TestGitFlowQueue as baseline


class CommitBeforeQueueAcceptance(unittest.TestCase):
    """Exercise consequential local-history/index boundaries without touching a real checkout."""
    setUp = baseline.QueueAcceptance.setUp
    tearDown = baseline.QueueAcceptance.tearDown

    def make_scoped_asset(self, project=None):
        """Create one committed native fixture and a saved pending edit."""
        root = project or self.repository
        asset = root / 'Assets/Game/Enemy.asset'
        asset.parent.mkdir(parents=True)
        asset.write_bytes(b'MovementSpeed: 3\r\n')
        asset.with_suffix('.asset.meta').write_text('guid: 12345678901234567890123456789012\n')
        (root / 'Assets/Game.meta').write_text('folderAsset: yes\n')
        queue.git(self.repository, 'add', '.')
        queue.git(self.repository, 'commit', '-m', 'Native fixture baseline')
        asset.write_bytes(b'MovementSpeed: 4\r\n')
        return root, asset

    def test_default_submit_commits_all_saved_changes_then_delivers(self):
        retired = self.repository / 'retired.txt'
        retired.write_text('old source\n')
        queue.git(self.repository, 'add', 'retired.txt')
        queue.git(self.repository, 'commit', '-m', 'Retired baseline')
        parent = queue.git(self.repository, 'rev-parse', 'HEAD')
        retired.unlink()
        (self.repository / 'feature.txt').write_bytes(b'first staged\r\n')
        queue.git(self.repository, 'add', 'feature.txt')
        (self.repository / 'feature.txt').write_bytes(b'latest saved\r\n')
        fresh = self.repository / 'saved space é.bin'
        fresh.write_bytes(b'\x00\x01\xff')
        ignored = self.repository / 'Library/retained.cache'
        ignored.parent.mkdir(); ignored.write_bytes(b'ignored user cache')
        value = queue.submit(self.repository, 'origin', 'Commit saved work', 'patch', 'Saved staged, unstaged, binary and deleted files')
        self.assertEqual(queue.git(self.repository, 'rev-parse', 'HEAD'), value['source'])
        self.assertEqual(queue.git(self.repository, 'rev-parse', 'HEAD^'), parent)
        self.assertEqual(queue.git(self.repository, 'status', '--porcelain'), '')
        self.assertEqual(fresh.read_bytes(), b'\x00\x01\xff')
        self.assertEqual(ignored.read_bytes(), b'ignored user cache')
        self.assertNotIn('Library/retained.cache', value['capture']['files'])
        self.assertEqual(queue.submit(self.repository, 'origin', 'Retry', 'patch', 'Same saved source')['id'], value['id'])
        result = queue.process_once(self.repository)
        self.assertEqual(result['state'], 'integrated')
        self.assertTrue(queue.ancestor(self.repository, value['source'], result['main_commit']))
        self.assertEqual(result['pulls']['original'], 'passed')

    def test_scoped_default_advances_branch_and_preserves_unrelated_staging(self):
        root, asset = self.make_scoped_asset()
        parent = queue.git(root, 'rev-parse', 'HEAD')
        outside = root / 'feature.txt'
        outside.write_text('unrelated staged\n'); queue.git(root, 'add', 'feature.txt')
        outside.write_text('unrelated later edit\n')
        value = queue.queue_local_changes(root, 'origin', ['Assets/Game'], 'Saved asset', 'patch', 'Commit selected asset only')
        self.assertEqual(queue.git(root, 'rev-parse', 'HEAD'), value['source'])
        self.assertEqual(queue.git(root, 'rev-parse', 'HEAD^'), parent)
        self.assertEqual(queue.git(root, 'show', ':feature.txt'), 'unrelated staged')
        self.assertEqual(outside.read_text(), 'unrelated later edit\n')
        self.assertEqual(asset.read_bytes(), b'MovementSpeed: 4\r\n')
        self.assertNotIn('Enemy.asset', queue.git(root, 'status', '--porcelain'))
        self.assertTrue(value['capture']['unrelated_staging_preserved'])
        self.assertEqual(queue.queue_local_changes(root, 'origin', ['Assets/Game'], 'Retry', 'patch', 'Same commit')['id'], value['id'])

    def test_native_commit_hook_failure_never_submits_or_discards_work(self):
        parent = queue.git(self.repository, 'rev-parse', 'HEAD')
        (self.repository / 'feature.txt').write_text('saved pending work\n')
        hook = self.repository / '.git/hooks/pre-commit'
        hook.write_text('#!/bin/sh\nexit 1\n'); hook.chmod(0o755)
        with self.assertRaises(queue.QueueError):
            queue.submit(self.repository, 'origin', 'Hook refusal', 'patch', 'Retain failed commit inputs')
        self.assertEqual(queue.git(self.repository, 'rev-parse', 'HEAD'), parent)
        self.assertEqual((self.repository / 'feature.txt').read_text(), 'saved pending work\n')
        self.assertEqual(queue.entries(self.repository, 'origin'), [])

    def test_failed_submission_retains_commit_and_retry_reuses_it(self):
        (self.repository / 'feature.txt').write_text('saved offline\n')
        with mock.patch.object(queue, 'submit_source', side_effect=queue.QueueError('Unavailable remote fixture')):
            self.assertRaises(queue.QueueError, queue.submit, self.repository, 'origin', 'Offline', 'patch', 'Commit first')
        source = queue.git(self.repository, 'rev-parse', 'HEAD')
        self.assertEqual(queue.git(self.repository, 'status', '--porcelain'), '')
        value = queue.submit(self.repository, 'origin', 'Retry', 'patch', 'Reuse retained commit')
        self.assertEqual(value['source'], source)
        self.assertEqual(len(queue.entries(self.repository, 'origin')), 1)

    def test_nested_project_scoped_commit_preserves_repository_paths(self):
        project = self.repository / 'Unity Project'
        root, asset = self.make_scoped_asset(project)
        value = queue.queue_local_changes(root, 'origin', ['Assets/Game'], 'Nested source', 'patch', 'Commit nested native asset')
        self.assertEqual(queue.git(root, 'show', value['source'] + ':Unity Project/Assets/Game/Enemy.asset'), 'MovementSpeed: 4')
        self.assertEqual(queue.git(self.repository, 'status', '--porcelain'), '')
        self.assertEqual(asset.read_bytes(), b'MovementSpeed: 4\r\n')

    def test_detached_head_and_existing_index_lock_are_preserved(self):
        parent = queue.git(self.repository, 'rev-parse', 'HEAD')
        queue.git(self.repository, 'checkout', '--detach', parent)
        (self.repository / 'feature.txt').write_text('saved pending\n')
        self.assertRaisesRegex(queue.QueueError, 'task branch', queue.submit, self.repository, 'origin', 'Detached', 'patch', 'Do not create detached local history')
        queue.git(self.repository, 'checkout', 'main')
        lock = self.repository / '.git/index.lock'; lock.write_bytes(b'other Git owner')
        try:
            self.assertRaises(queue.QueueError, queue.submit, self.repository, 'origin', 'Busy index', 'patch', 'Do not steal a Git lock')
            self.assertEqual(lock.read_bytes(), b'other Git owner')
            self.assertEqual(queue.git(self.repository, 'rev-parse', 'HEAD'), parent)
        finally:
            lock.unlink()
        self.assertEqual(queue.entries(self.repository, 'origin'), [])

    def test_cli_submit_defaults_to_commit_and_committed_only_is_explicit(self):
        (self.repository / 'feature.txt').write_text('saved CLI edit\n')
        command = [sys.executable, str(Path(queue.__file__)), '--repository', str(self.repository), 'submit', '--title', 'CLI default', '--size', 'patch', '--summary', 'Commit and submit exact saved bytes']
        refused = subprocess.run(command + ['--committed-only'], capture_output=True, timeout=60)
        self.assertNotEqual(refused.returncode, 0)
        result = subprocess.run(command, capture_output=True, timeout=60, check=True)
        value = json.loads(result.stdout)
        self.assertEqual(value['source'], queue.git(self.repository, 'rev-parse', 'HEAD'))
        self.assertEqual(queue.git(self.repository, 'status', '--porcelain'), '')

    def test_concurrent_outside_staging_stops_without_overwriting_it(self):
        root, asset = self.make_scoped_asset()
        parent = queue.git(root, 'rev-parse', 'HEAD')
        original_run = queue.run
        def stage_external(arguments, directory, *arguments_tail, **options):
            result = original_run(arguments, directory, *arguments_tail, **options)
            if arguments[:3] == ['git', 'add', '--all'] and not options.get('environment'):
                (root / 'feature.txt').write_text('new external staged work\n')
                original_run(['git', 'add', 'feature.txt'], root)
            return result
        with mock.patch.object(queue, 'run', side_effect=stage_external):
            self.assertRaisesRegex(queue.QueueError, 'Checkout changed', queue.queue_local_changes,
                root, 'origin', ['Assets/Game'], 'Concurrent stage', 'patch', 'Preserve the other writer')
        self.assertEqual(queue.git(root, 'rev-parse', 'HEAD'), parent)
        self.assertEqual(queue.git(root, 'show', ':feature.txt'), 'new external staged work')
        self.assertEqual(asset.read_bytes(), b'MovementSpeed: 4\r\n')
        self.assertEqual(queue.entries(root, 'origin'), [])

    def test_hook_changed_source_retains_actual_commit_without_queueing(self):
        parent = queue.git(self.repository, 'rev-parse', 'HEAD')
        (self.repository / 'feature.txt').write_text('reviewed source\n')
        reviewed_bytes = (self.repository / 'feature.txt').read_bytes()
        hook = self.repository / '.git/hooks/pre-commit'
        hook.write_text("#!/bin/sh\nprintf 'hook changed source\\n' > feature.txt\ngit add feature.txt\n")
        hook.chmod(0o755)
        self.assertRaisesRegex(queue.QueueError, 'Commit or hook changed', queue.submit,
            self.repository, 'origin', 'Hook source change', 'patch', 'Require review before submission')
        self.assertNotEqual(queue.git(self.repository, 'rev-parse', 'HEAD'), parent)
        self.assertEqual((self.repository / 'feature.txt').read_text(), 'hook changed source\n')
        self.assertEqual(queue.entries(self.repository, 'origin'), [])
        journal = next((self.repository / '.git/gameforge_queue/local_commits').glob('*/commit.json'))
        retained = json.loads(journal.read_text())
        original = retained['original_files']['feature.txt']['artifact']
        self.assertEqual((journal.parent / original).read_bytes(), reviewed_bytes)

    def test_concurrent_outside_working_edit_is_preserved(self):
        root, asset = self.make_scoped_asset()
        parent = queue.git(root, 'rev-parse', 'HEAD')
        outside = root / 'feature.txt'; outside.write_text('unrelated pending original\n')
        original_bytes = outside.read_bytes()
        original_run = queue.run
        def edit_external(arguments, directory, *arguments_tail, **options):
            result = original_run(arguments, directory, *arguments_tail, **options)
            if arguments[:3] == ['git', 'add', '--all'] and not options.get('environment'):
                outside.write_text('external later working edit\n')
            return result
        with mock.patch.object(queue, 'run', side_effect=edit_external):
            self.assertRaisesRegex(queue.QueueError, 'Checkout changed', queue.queue_local_changes,
                root, 'origin', ['Assets/Game'], 'Concurrent working edit', 'patch', 'Retain both outside versions')
        self.assertEqual(queue.git(root, 'rev-parse', 'HEAD'), parent)
        self.assertEqual(outside.read_text(), 'external later working edit\n')
        journal = next((root / '.git/gameforge_queue/local_commits').glob('*/commit.json'))
        retained = json.loads(journal.read_text())
        self.assertEqual((journal.parent / retained['original_files']['feature.txt']['artifact']).read_bytes(), original_bytes)
        self.assertEqual(queue.entries(root, 'origin'), [])

    def test_staged_asset_renames_commit_both_paths_and_metadata(self):
        root, asset = self.make_scoped_asset()
        asset.write_bytes(b'MovementSpeed: 3\r\n')
        queue.git(root, 'mv', 'Assets/Game/Enemy.asset', 'Assets/Game/Renamed.asset')
        queue.git(root, 'mv', 'Assets/Game/Enemy.asset.meta', 'Assets/Game/Renamed.asset.meta')
        scoped = queue.queue_local_changes(root, 'origin', ['Assets/Game'], 'Move saved asset', 'patch', 'Retain both rename paths and native identity')
        self.assertEqual(queue.git(root, 'status', '--porcelain'), '')
        self.assertNotIn('Assets/Game/Enemy.asset', queue.git(root, 'ls-tree', '-r', '--name-only', scoped['source']).splitlines())
        self.assertIn('Assets/Game/Enemy.asset.meta', scoped['capture']['files'])
        queue.git(root, 'mv', 'Assets/Game/Renamed.asset', 'Assets/Game/Final.asset')
        queue.git(root, 'mv', 'Assets/Game/Renamed.asset.meta', 'Assets/Game/Final.asset.meta')
        whole = queue.submit(root, 'origin', 'Move asset again', 'patch', 'Whole-repository rename uses the same complete path inventory')
        self.assertEqual(queue.git(root, 'status', '--porcelain'), '')
        self.assertNotIn('Assets/Game/Renamed.asset', queue.git(root, 'ls-tree', '-r', '--name-only', whole['source']).splitlines())
        self.assertEqual(queue.git(root, 'show', whole['source'] + ':Assets/Game/Final.asset.meta'), 'guid: 12345678901234567890123456789012')


if __name__ == '__main__':
    unittest.main()
