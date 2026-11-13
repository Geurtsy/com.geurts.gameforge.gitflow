"""Disk-backed project repairs, reviewed before mutation of the publisher candidate."""
import hashlib
import os
from pathlib import Path
import shutil
import uuid
from concurrent.futures import ThreadPoolExecutor


def parallel_files(operation, values):
    """Overlap independent ordinary-file I/O; keep every existing path and byte check."""
    with ThreadPoolExecutor(max_workers=min(8, os.cpu_count() or 1)) as workers:
        return list(workers.map(operation, values))


def file_hash(path):
    """Stream complete bytes; repair files have no application-imposed size ceiling."""
    digest = hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda: source.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def identity(q, workspace):
    """Bind review/application to the exact original HEAD, index and visible work."""
    status = q.run(['git', '--no-optional-locks', 'status', '--porcelain=v1', '-z', '--untracked-files=all'], workspace)
    index = Path(q.git(workspace, 'rev-parse', '--git-path', 'index'))
    if not index.is_absolute():
        index = workspace / index
    return dict(head=q.git(workspace, 'rev-parse', 'HEAD'), index=file_hash(q.plain(index)),
                status=q.digest(status))


def manifest(q, directory):
    """Inspect all proposed ordinary files, rejecting links and nested Git metadata."""
    paths = []
    for parent, folders, files in os.walk(directory, followlinks=False):
        for name in folders:
            path = q.plain(Path(parent) / name)
            relative = path.relative_to(directory).as_posix()
            if any(part.casefold() == '.git' for part in Path(relative).parts):
                raise q.QueueError('Repair workspace must not contain Git metadata: ' + relative)
            if not path.is_dir():
                raise q.QueueError('Unsupported non-ordinary repair artifact: ' + relative)
        paths.extend(Path(parent) / name for name in files)
    def inspect(path):
        path = q.plain(path)
        relative = path.relative_to(directory).as_posix()
        if any(part.casefold() == '.git' for part in Path(relative).parts) or not path.is_file():
            raise q.QueueError('Unsupported repair artifact: ' + relative)
        return relative, dict(sha256=file_hash(path), bytes=path.stat().st_size)
    return dict(parallel_files(inspect, paths))


def prepare(q, workspace, evidence_root, folder):
    """Copy complete saved project content without remotes, Git metadata or ignored caches."""
    root = q.plain(evidence_root.parent.parent / 'Repair' / uuid.uuid4().hex[:12])
    original, proposed = root / 'Original', root / 'Proposed'
    names = set(q.run(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], workspace).decode('utf-8').split('\0')) - {''}
    baseline = identity(q, workspace)
    # Validate every concrete descendant before creating/copying any project content.
    def preflight(name):
        q.plain(workspace / name)
        q.plain(original / name)
        q.plain(proposed / name)
    parallel_files(preflight, sorted(names))
    original.mkdir(parents=True, exist_ok=False)
    proposed.mkdir()
    def copy(name):
        source = q.plain(workspace / name)
        if not source.exists():
            return
        if not source.is_file():
            raise q.QueueError('Unsupported tracked repair input: ' + name)
        for destination in [original / name, proposed / name]:
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
    parallel_files(copy, sorted(names))
    files = manifest(q, original)
    if identity(q, workspace) != baseline or manifest(q, proposed) != files:
        raise q.QueueError('Candidate changed while preparing repair; original candidate retained.')
    def verify(item):
        name, value = item
        if file_hash(q.plain(workspace / name)) != value['sha256']:
            raise q.QueueError('Candidate bytes changed during repair capture: ' + name)
    parallel_files(verify, files.items())
    record = dict(root=str(root), original=str(original), proposed=str(proposed),
                  candidate=baseline, files=files)
    q.durable(folder / 'workspace.json', record)
    return record


def protected_paths(policy):
    """Protect independent validation, version ownership and publication machinery only."""
    values = set(policy.get('protected_paths', [])) | {policy['version_file']} | set(policy.get('native_version_outputs', []))
    for check in policy['checks']:
        values.update(arg for arg in check['command'] if not arg.startswith(('-', '{')))
    return values


def is_protected(relative, policy):
    parts = Path(relative).parts
    normalized = relative.casefold()
    control_names = {'GitFlowQueue.py', 'GitFlowRepair.py', 'GitFlowRepairWorkspace.py', 'GitFlowModels.py',
                     'GitFlowLease.py', 'GitFlowProcess.py', 'GitFlowDeployment.py', 'GitFlowPublisher.py', 'GitFlowUnity.py'}
    return (any(part.casefold() in {'.git', '.codex'} for part in parts)
            or normalized.startswith('.github/workflows/') or Path(relative).name.casefold() in {name.casefold() for name in control_names}
            or any(normalized == item.casefold() or normalized.startswith(item.rstrip('/').casefold() + '/') for item in protected_paths(policy)))


def changes(q, record, policy):
    """Audit the complete artifact tree, including creations, deletions and binary changes."""
    if manifest(q, Path(record['original'])) != record['files']:
        raise q.QueueError('Repair changed its immutable original evidence; candidate retained.')
    proposed = manifest(q, Path(record['proposed']))
    changed = {name: dict(before=record['files'].get(name), after=proposed.get(name))
               for name in record['files'].keys() | proposed.keys()
               if record['files'].get(name) != proposed.get(name)}
    for name in changed:
        if is_protected(name, policy):
            raise q.QueueError('Repair changed independent validation or queue control: ' + name)
    return changed


def apply(q, workspace, record, changed):
    """Import reviewed exact artifacts only after rechecking original candidate membership and bytes."""
    if identity(q, workspace) != record['candidate']:
        raise q.QueueError('Candidate changed during repair; reviewed artifacts retained without application.')
    names = set(q.run(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], workspace).decode('utf-8').split('\0')) - {''}
    existing = {name for name in names if (workspace / name).is_file()}
    if existing != set(record['files']):
        raise q.QueueError('Candidate file membership changed during repair; artifacts retained.')
    def verify(item):
        name, value = item
        if file_hash(q.plain(workspace / name)) != value['sha256']:
            raise q.QueueError('Candidate bytes changed during repair: ' + name)
    parallel_files(verify, record['files'].items())
    # Validate all destinations before starting the reviewed import.
    for name, change in changed.items():
        q.plain(workspace / name)
        if change['after'] and file_hash(q.plain(Path(record['proposed']) / name)) != change['after']['sha256']:
            raise q.QueueError('Reviewed repair artifact changed: ' + name)
    for name in sorted((name for name, change in changed.items() if change['after'] is None),
                       key=lambda item: len(Path(item).parts), reverse=True):
        (workspace / name).unlink()
    for name, change in sorted(changed.items()):
        if change['after'] is None:
            continue
        target = workspace / name
        if target.is_dir():
            # Only empty directories left by reviewed file deletions may be removed.
            target.rmdir()
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(Path(record['proposed']) / name, target)
    pathspec = b''.join((':(literal)' + name).encode('utf-8') + b'\0' for name in changed)
    q.run(['git', 'add', '--all', '--pathspec-from-file=-', '--pathspec-file-nul'], workspace, data=pathspec)
