"""Capture explicitly scoped saved work with a separate Git index, never changing the live checkout."""
import os
from pathlib import Path
import tempfile

DEFAULT_SCOPES = ['Assets']
EXCLUDED_PARTS = {'library', 'temp', 'obj', 'logs', 'build', 'builds', 'usersettings',
                  'plugins', '_thirdparty', 'thirdparty', 'third-party', '_generated', 'generated',
                  '.git', '.github', '.codex', 'tools', 'tools~', 'tests', 'tests~', 'test', 'testing'}


def inventory(q, root, scopes):
    """List saved changed files and companions in approved roots, respecting ordinary Git ignores."""
    if not scopes:
        raise q.QueueError('Choose at least one task-owned capture folder during setup.')
    for scope in scopes:
        if (not scope or scope == '.' or scope.startswith(('/', '\\')) or ':' in scope
                or '..' in Path(scope).parts or Path(scope).parts[0] not in {'Assets', 'Packages', 'ProjectSettings'}):
            raise q.QueueError('Capture scope must be a specific Assets, Packages or ProjectSettings folder.')
    paths = set(q.run(['git', 'diff', '--name-only', '-z', 'HEAD', '--', *scopes], root).decode().split('\0'))
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
        result = dict(source=source, parent=parent, tree=tree, files=plan['paths'], excluded=plan['excluded'], scopes=scopes,
                      working_files_preserved=True, index_preserved=True, branch_preserved=True)
        q.durable(q.common(root) / ('gameforge_queue/captures/' + source + '.json'), result)
        return result
