"""Capture explicitly scoped saved work with a separate Git index, never changing the live checkout."""
import os
from pathlib import Path
import tempfile
import uuid

DEFAULT_SCOPES = ['Assets']
EXCLUDED_PARTS = {'library', 'temp', 'obj', 'logs', 'build', 'builds', 'usersettings',
                  'plugins', '_thirdparty', 'thirdparty', 'third-party', '_generated', 'generated',
                  '.git', '.github', '.codex', 'tools', 'tools~', 'tests', 'tests~', 'test', 'testing'}


def inventory(q, root, scopes):
    """List saved changed files and companions in approved roots, respecting ordinary Git ignores."""
    if scopes is None:
        paths = set(q.run(['git', 'diff', '--no-renames', '--relative', '--name-only', '-z', 'HEAD'], root).decode().split('\0'))
        paths.update(q.run(['git', 'ls-files', '--others', '--exclude-standard', '-z'], root).decode().split('\0'))
        paths.discard('')
        return dict(paths=sorted(paths), excluded=[], scopes=['.'])
    if not scopes:
        raise q.QueueError('Choose at least one task-owned capture folder during setup.')
    for scope in scopes:
        if (not scope or scope == '.' or scope.startswith(('/', '\\')) or ':' in scope
                or '..' in Path(scope).parts or Path(scope).parts[0] not in {'Assets', 'Packages', 'ProjectSettings'}):
            raise q.QueueError('Capture scope must be a specific Assets, Packages or ProjectSettings folder.')
    paths = set(q.run(['git', 'diff', '--no-renames', '--relative', '--name-only', '-z', 'HEAD', '--', *scopes], root).decode().split('\0'))
    paths.update(q.run(['git', 'ls-files', '--others', '--exclude-standard', '-z', '--', *scopes], root).decode().split('\0'))
    paths.discard('')
    excluded = []
    for path in sorted(paths):
        parts = {part.casefold().removesuffix('.meta') for part in Path(path).parts}
        if parts & EXCLUDED_PARTS or path in {'ProjectSettings/ProjectSettings.asset', 'ProjectSettings/GeurtsGameForgeQueueVersion.txt', 'Packages/manifest.json', 'Packages/packages-lock.json'}:
            excluded.append(path)
    paths.difference_update(excluded)
    # Unity asset and folder identities travel with the selected first-party changes.
    companions = set()
    for relative in paths:
        if not relative.startswith('Assets/'):
            continue
        target = relative[:-5] if relative.endswith('.meta') else relative
        for item in [Path(target), *Path(target).parents]:
            if str(item) in {'.', 'Assets'}:
                break
            meta = item.as_posix() + '.meta'
            if q.plain(root / meta).is_file():
                # git check-ignore returns one for an eligible path; use ls-files for a read-only check.
                tracked = q.git(root, 'ls-files', '--', meta)
                untracked = q.git(root, 'ls-files', '--others', '--exclude-standard', '--', meta)
                if tracked or untracked:
                    companions.add(meta)
                else:
                    raise q.QueueError('Required Unity metadata is ignored; review repository rules before capture: ' + meta)
    paths.update(companions)
    return dict(paths=sorted(paths), excluded=excluded, scopes=scopes)


def signature(q, root, paths):
    """Observe exact disk bytes and missing files; reject links before touching a Git index."""
    result = {}
    for relative in paths:
        target = q.plain(root / relative)
        if target.exists() and not target.is_file():
            raise q.QueueError('Unsupported capture target; choose ordinary first-party files: ' + relative)
        result[relative] = q.file_digest(target) if target.is_file() else None
    return result


def capture(q, root, scopes, expected_inventory=None):
    """Create an idempotent commit from saved eligible files, preserving HEAD, index and all working bytes."""
    root = q.plain(root)
    with q.exclusive(q.common(root) / 'gameforge_queue/capture.lock'):
        return prepare_capture(q, root, scopes, expected_inventory)[0]


def prepare_capture(q, root, scopes, expected_inventory=None):
    """Build the immutable snapshot while its caller holds the shared capture lock."""
    parent = q.git(root, 'rev-parse', 'HEAD')
    if q.git(root, 'ls-files', '--unmerged'):
        raise q.QueueError('Resolve the existing merge in your Git client first. Nothing was captured or discarded.')
    plan = inventory(q, root, scopes)
    if expected_inventory is not None and plan != expected_inventory:
        raise q.QueueError('Saved changes changed after preview. Review the refreshed capture list.')
    if not plan['paths']:
        raise q.QueueError('No eligible saved changes exist in the configured task scope. Excluded work is preserved.')
    before = signature(q, root, plan['paths'])
    index_path = Path(q.git(root, 'rev-parse', '--path-format=absolute', '--git-path', 'index'))
    original_index = q.file_digest(index_path) if index_path.exists() else None
    with tempfile.TemporaryDirectory(prefix='gitflow_capture_') as temporary:
        environment = {'GIT_INDEX_FILE': str(Path(temporary) / 'index')}
        q.git(root, 'read-tree', parent, env=environment)
        # Literal pathspecs and NUL input handle spaces and punctuation without broad staging.
        pathspecs = b'\0'.join((':(literal)' + item).encode() for item in plan['paths']) + b'\0'
        q.run(['git', 'add', '--all', '--pathspec-from-file=-', '--pathspec-file-nul'], root,
              data=pathspecs, environment=environment)
        tree = q.git(root, 'write-tree', env=environment)
    if (parent != q.git(root, 'rev-parse', 'HEAD') or before != signature(q, root, plan['paths'])
            or plan != inventory(q, root, scopes)
            or original_index != (q.file_digest(index_path) if index_path.exists() else None)):
        raise q.QueueError('The checkout changed during capture. Existing work is preserved; review and capture again.')
    if tree == q.git(root, 'rev-parse', parent + '^{tree}'):
        raise q.QueueError('Saved files have no source difference after normal Git conversion.')
    # Canonical snapshot identity makes response-loss retries deterministic, including before any receipt exists.
    date = q.git(root, 'show', '--no-patch', '--format=%ct', parent) + ' +0000'
    identity = dict(GIT_AUTHOR_NAME='GitFlow source snapshot', GIT_AUTHOR_EMAIL='snapshot@gameforge.invalid',
                    GIT_COMMITTER_NAME='GitFlow source snapshot', GIT_COMMITTER_EMAIL='snapshot@gameforge.invalid',
                    GIT_AUTHOR_DATE=date, GIT_COMMITTER_DATE=date)
    source = q.git(root, 'commit-tree', tree, '-p', parent, '-m', 'Capture saved local changes\n\nTree: ' + tree, env=identity)
    result = dict(source=source, parent=parent, tree=tree, files=plan['paths'], excluded=plan['excluded'], scopes=plan['scopes'],
                  working_files_preserved=True, index_preserved=True, branch_preserved=True)
    q.durable(q.common(root) / ('gameforge_queue/captures/' + source + '.json'), result)
    return result, before


def staged_entries(q, root, selected):
    """Read native index entries outside the selected paths for preservation checks."""
    repository = Path(q.git(root, 'rev-parse', '--show-toplevel'))
    prefix = root.relative_to(repository).as_posix()
    names = {name if prefix == '.' else prefix + '/' + name for name in selected}
    entries = q.run(['git', 'ls-files', '--stage', '-z'], repository).split(b'\0')
    return [entry for entry in entries if entry and entry.split(b'\t', 1)[1].decode('utf-8') not in names]


def commit_local(q, root, scopes=None, title='Commit saved local changes'):
    """Commit saved selected work on its branch using native Git, retaining unrelated staging and working bytes."""
    root = q.plain(root)
    if scopes is None:
        root = q.plain(Path(q.git(root, 'rev-parse', '--show-toplevel')))
    with q.exclusive(q.common(root) / 'gameforge_queue/capture.lock'):
        parent = q.git(root, 'rev-parse', 'HEAD')
        if q.git(root, 'ls-files', '--unmerged'):
            raise q.QueueError('Resolve the existing merge before committing local changes. Nothing was submitted.')
        if any(Path(q.git(root, 'rev-parse', '--path-format=absolute', '--git-path', name)).exists()
               for name in ['MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'rebase-merge', 'rebase-apply']):
            raise q.QueueError('Finish the existing Git operation before committing local changes. Nothing was submitted.')
        plan = inventory(q, root, scopes)
        if not plan['paths']:
            return dict(source=parent, parent=parent, files=[], excluded=plan['excluded'], scopes=plan['scopes'],
                        committed=False, working_files_preserved=True, unrelated_staging_preserved=True)
        branch = q.git(root, 'branch', '--show-current')
        if not branch:
            raise q.QueueError('Create or select a task branch before committing saved local changes. Detached HEAD was preserved.')
        snapshot, signatures = prepare_capture(q, root, scopes, plan)
        selected = snapshot['files']
        outside = staged_entries(q, root, selected)
        journal = q.plain(q.common(root) / 'gameforge_queue/local_commits' / uuid.uuid4().hex)
        q.plain(journal / 'index.before.bin')
        journal.mkdir(parents=True)
        index = Path(q.git(root, 'rev-parse', '--path-format=absolute', '--git-path', 'index'))
        (journal / 'index.before.bin').write_bytes(index.read_bytes())
        repository = q.plain(Path(q.git(root, 'rev-parse', '--show-toplevel')))
        prefix = root.relative_to(repository).as_posix()
        selected_repository_paths = {name if prefix == '.' else prefix + '/' + name for name in selected}
        pending = sorted(set(inventory(q, repository, None)['paths']) | selected_repository_paths)
        originals = {}
        for number, name in enumerate(pending):
            path = q.plain(repository / name)
            artifact = str(number) + '.original' if path.is_file() else None
            data = path.read_bytes() if artifact is not None else None
            if artifact is not None:
                (journal / artifact).write_bytes(data)
            originals[name] = dict(artifact=artifact, sha256=q.digest(data) if data is not None else None)
        outside_paths = sorted(set(pending) - selected_repository_paths)
        outside_signatures = {name: originals[name]['sha256'] for name in outside_paths}
        record = dict(state='prepared', parent=parent, branch=branch, snapshot=snapshot,
                      original_files=originals, utc=q.utc())
        q.durable(journal / 'commit.json', record)
        pathspecs = b'\0'.join((':(literal)' + name).encode('utf-8') for name in selected) + b'\0'
        try:
            # Git owns its index/ref locks and runs configured hooks/signing. --only
            # commits this selection without incorporating unrelated staged entries.
            tracked = set(q.run(['git', 'ls-files', '--cached', '-z'], root).decode('utf-8').split('\0'))
            staging = [name for name in selected if name in tracked or (root / name).exists()]
            # A staged deletion/rename has already removed its old index path.
            # Do not re-add that missing path; commit --only still includes its
            # deletion from HEAD together with the destination and metadata.
            if staging:
                staging_pathspecs = b'\0'.join((':(literal)' + name).encode('utf-8') for name in staging) + b'\0'
                q.run(['git', 'add', '--all', '--pathspec-from-file=-', '--pathspec-file-nul'], root, data=staging_pathspecs)
            if (q.git(root, 'rev-parse', 'HEAD') != parent or q.git(root, 'branch', '--show-current') != branch
                    or signature(q, root, selected) != signatures or staged_entries(q, root, selected) != outside
                    or signature(q, repository, outside_paths) != outside_signatures):
                raise q.QueueError('Checkout changed before the local commit. Saved and staged work remains; nothing was submitted.')
            record.update(state='committing'); q.durable(journal / 'commit.json', record)
            q.run(['git', 'commit', '--only', '--pathspec-from-file=-', '--pathspec-file-nul', '-m', title], root, data=pathspecs)
            source = q.git(root, 'rev-parse', 'HEAD')
            record.update(state='committed_pending_verification', source=source); q.durable(journal / 'commit.json', record)
            if (q.git(root, 'branch', '--show-current') != branch
                    or q.git(root, 'rev-parse', source + '^') != parent
                    or q.git(root, 'rev-parse', source + '^{tree}') != snapshot['tree']
                    or signature(q, root, selected) != signatures or staged_entries(q, root, selected) != outside
                    or signature(q, repository, outside_paths) != outside_signatures):
                raise q.QueueError('Commit or hook changed the reviewed source/index. The actual local commit and files are retained; inspect them before submission.')
            result = dict(snapshot, source=source, committed=True, branch=branch, branch_advanced=True,
                          branch_preserved=False, branch_name_preserved=True, index_preserved=False,
                          unrelated_staging_preserved=True, journal=str(journal / 'commit.json'))
            record.update(state='verified', result=result); q.durable(journal / 'commit.json', record)
            return result
        except Exception as error:
            # A native failure can leave selected files staged or a hook-created
            # commit present. Never reset it, discard bytes, or submit uncertain work.
            record.update(state='blocked', detail=str(error), actual_head=q.git(root, 'rev-parse', 'HEAD'))
            q.durable(journal / 'commit.json', record)
            raise
