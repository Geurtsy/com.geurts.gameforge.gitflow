"""GitFlow's deterministic, strict integration host. No validation bypass.

One shared Git object store, one exclusively owned reusable worktree, immutable
requests and candidate-bound gate evidence. Project adapters own native behavior.
"""
import argparse
from contextlib import contextmanager
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import time
import uuid

import GitFlowLease as leases
from GitFlowStorage import inspect_storage
from GitFlowProcess import process_run

LOCK = 'refs/heads/gameforge/integration-lease'
REQUEST = 'request.json'
PREFIX = 'refs/heads/gameforge/queue/'
SCHEMA = 1
MAXIMUM_BYTES = 1024 * 1024


class QueueError(RuntimeError):
    """An actionable blocker; never treated as a successful integration."""


class GateFailure(QueueError):
    """Retains a bounded diagnostic location for an independently guarded source repair."""
    def __init__(self, message, folder):
        super().__init__(message)
        self.folder = str(folder)


def encoded(value):
    return json.dumps(value, sort_keys=True, indent=2, ensure_ascii=False) + '\n'


def digest(value):
    return hashlib.sha256(value if isinstance(value, bytes) else encoded(value).encode()).hexdigest()


def file_digest(path):
    with Path(path).open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest() if hasattr(hashlib, 'file_digest') else digest(source.read())


def utc():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def console_options():
    return {'creationflags': subprocess.CREATE_NO_WINDOW} if os.name == 'nt' else {}


def run(arguments, root, data=None, timeout=300, environment=None):
    """Never invokes a shell or interactive credential flow; transports have a finite deadline."""
    env = dict(os.environ, GIT_TERMINAL_PROMPT='0', GCM_INTERACTIVE='Never', GIT_OPTIONAL_LOCKS='0')
    if environment:
        env.update(environment)
    try:
        result = process_run([str(item) for item in arguments], cwd=root, env=env,
                                input=data, capture_output=True, timeout=timeout, **console_options())
    except (OSError, subprocess.TimeoutExpired) as error:
        raise QueueError(type(error).__name__ + ': ' + str(arguments[0]) + '; inspect the retained candidate before resuming.') from error
    if result.returncode:
        raise QueueError(str(arguments[0]) + ' failed (' + str(result.returncode) + '): ' +
                         (result.stderr + result.stdout).decode('utf-8', 'replace')[-4000:])
    return result.stdout


def git(root, *arguments, env=None):
    return run(['git', *arguments], root, environment=env).decode('utf-8').rstrip('\r\n')


def plain(path):
    """Validate every existing ancestor before creating a task-owned descendant."""
    path = Path(os.path.abspath(path))
    if len(str(path)) > 259:
        raise QueueError('Path exceeds 259 characters: ' + str(path))
    for current in [path, *path.parents]:
        if current.is_symlink() or current.exists() and getattr(current.lstat(), 'st_file_attributes', 0) & 0x400:
            raise QueueError('Reparse/link path is not an owned ordinary directory: ' + str(current))
    return path


def durable(path, value):
    """Flush then atomically replace one local control record; retain unknown files elsewhere."""
    path = plain(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.' + uuid.uuid4().hex + '.pending')
    with temporary.open('xb') as output:
        output.write(encoded(value).encode())
        output.flush()
        os.fsync(output.fileno())
    os.replace(temporary, path)


def read_json(path):
    path = plain(path)
    if path.stat().st_size > MAXIMUM_BYTES:
        raise QueueError('Control record exceeds one MiB: ' + str(path))
    return json.loads(path.read_text(encoding='utf-8-sig'))


def common(root):
    return plain(Path(root) / git(root, 'rev-parse', '--git-common-dir'))


@contextmanager
def exclusive(path):
    """OS ownership is authoritative; stale PID/status files cannot grant ownership."""
    path = plain(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('a+b') as handle:
        handle.seek(0)
        if os.name == 'nt':
            import msvcrt
            if path.stat().st_size == 0:
                handle.write(b'0')
                handle.flush()
            handle.seek(0)
            try:
                msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
            except OSError as error:
                raise QueueError('Another local owner is using this repository/workspace.') from error
        else:
            import fcntl
            try:
                fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except OSError as error:
                raise QueueError('Another local owner is using this repository/workspace.') from error
        try:
            yield
        finally:
            if os.name == 'nt':
                handle.seek(0)
                msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(handle, fcntl.LOCK_UN)


def object_commit(root, parent, value, message, artifacts=None, inherited=None):
    """Create a control commit without changing HEAD, index, worktree or FETCH_HEAD."""
    files = {REQUEST: encoded(value).encode(), **(artifacts or {})}
    blobs = dict(inherited or {})
    for name, content in sorted(files.items()):
        if not re.fullmatch(r'[A-Za-z0-9_.-]+', name):
            raise QueueError('Control artifact names must be flat and predictable.')
        blob = run(['git', 'hash-object', '-w', '--stdin'], root, data=content).decode().strip()
        blobs[name] = blob
    rows = ['100644 blob ' + blob + '\t' + name + '\n' for name, blob in sorted(blobs.items())]
    tree = run(['git', 'mktree'], root, data=''.join(rows).encode()).decode().strip()
    return git(root, 'commit-tree', tree, '-p', parent, '-m', message)


def read_request(root, identity):
    data = run(['git', 'show', identity + ':' + REQUEST], root)
    if len(data) > MAXIMUM_BYTES:
        raise QueueError('Remote control record exceeds one MiB.')
    return json.loads(data)


def remote_ref(root, remote, reference):
    rows = git(root, 'ls-remote', '--refs', remote, reference).splitlines()
    if not rows:
        return None
    if len(rows) != 1 or rows[0].split()[1] != reference:
        raise QueueError('Ambiguous remote reference: ' + reference)
    identity = rows[0].split()[0]
    if not re.fullmatch('[0-9a-f]{40}', identity):
        raise QueueError('Only SHA-1 Git repositories are supported by protocol 1.')
    git(root, 'fetch', '--no-write-fetch-head', remote, identity)
    git(root, 'cat-file', '-e', identity + '^{commit}')
    return identity


def ancestor(root, before, after):
    return git(root, 'merge-base', before, after) == before


def clean(root):
    if git(root, 'status', '--porcelain', '--untracked-files=all'):
        raise QueueError('Checkout contains uncommitted/untracked work; preserve it: ' + str(root))
    if Path(git(root, 'rev-parse', '--path-format=absolute', '--git-path', 'MERGE_HEAD')).exists():
        raise QueueError('An unresolved merge remains in the owned workspace.')


def entries(root, remote):
    values = []
    for row in git(root, 'ls-remote', '--refs', remote, PREFIX + '*').splitlines():
        identity, reference = row.split()
        git(root, 'fetch', '--no-write-fetch-head', remote, identity)
        value = read_request(root, identity)
        if value.get('schema') != SCHEMA or reference != PREFIX + value.get('id', ''):
            raise QueueError('Unknown/malformed queue request: ' + reference)
        values.append((identity, reference, value))
    return sorted(values, key=lambda row: (row[2]['submitted_utc'], row[2]['id']))


def submit(root, remote, title, size, summary, dependencies=()):
    """Idempotency is exact source identity. A lost response never creates a second request."""
    root = plain(root)
    clean(root)
    source = git(root, 'rev-parse', 'HEAD')
    return submit_source(root, remote, source, title, size, summary, dependencies)


def submit_source(root, remote, source, title, size, summary, dependencies=(), capture=None):
    """Submit an exact committed source, including a separately captured dirty-checkout snapshot."""
    if not re.fullmatch('[0-9a-f]{40}', source):
        raise QueueError('Submission requires an exact full source commit.')
    git(root, 'cat-file', '-e', source + '^{commit}')
    if git(root, 'ls-remote', '--refs', remote, 'refs/heads/codex/integration-lock', 'refs/heads/codex/queue/*'):
        raise QueueError('A legacy queue exists. Use its existing submission route until an explicit migration is completed.')
    identifier = 'request_' + source
    reference = PREFIX + identifier
    existing = remote_ref(root, remote, reference)
    if existing:
        value = read_request(root, existing)
        if value.get('schema') != SCHEMA or value.get('source') != source:
            raise QueueError('Existing source request identity is incompatible.')
        return value
    if size not in {'patch', 'minor', 'major'} or not title.strip() or not summary.strip():
        raise QueueError('Supply title, summary and patch/minor/major scope.')
    main = remote_ref(root, remote, 'refs/heads/main')
    if not main:
        raise QueueError('Remote main is missing.')
    base = git(root, 'merge-base', main, source)
    if ancestor(root, source, main):
        raise QueueError('This source is already on main; no new delivery was queued.')
    for dependency in dependencies:
        if not re.fullmatch('request_[0-9a-f]{40}', dependency) or not remote_ref(root, remote, PREFIX + dependency):
            raise QueueError('Dependency must identify an existing exact request.')
    value = dict(schema=SCHEMA, id=identifier, source=source, base=base, title=title, summary=summary,
                 size=size, dependencies=list(dependencies), submitted_utc=utc(), state='queued', attempts=[])
    if capture is not None:
        value['capture'] = capture
    child = object_commit(root, source, value, 'Submit ' + identifier)
    try:
        git(root, 'push', remote, child + ':' + reference)
    except QueueError:
        # Reconcile once after ambiguity; never resend an uncertain write.
        current = remote_ref(root, remote, reference)
        if not current or read_request(root, current).get('source') != source:
            raise
        return read_request(root, current)
    if remote_ref(root, remote, reference) != child:
        raise QueueError('Submission response is uncertain; inspect the deterministic request ID before any retry: ' + identifier)
    return value


def queue_local_changes(root, remote, scopes, title, size, summary):
    """One user action captures saved scoped files and verifies an idempotent queue submission."""
    from GitFlowCapture import capture
    snapshot = capture(sys.modules[__name__], plain(root), scopes)
    return submit_source(root, remote, snapshot['source'], title, size, summary, capture=snapshot)


def validate_policy(policy):
    if policy.get('schema') != SCHEMA:
        raise QueueError('Unsupported integration policy schema.')
    for key in ('version_file',):
        value = policy.get(key, '')
        if not value or value.startswith(('/', '\\')) or ':' in value or '..' in Path(value).parts:
            raise QueueError('Policy requires a safe repository-relative ' + key)
    checks = policy.get('checks', [])
    if not checks or not any(item.get('kind') == 'compilation' for item in checks):
        raise QueueError('At least one required compilation gate must be configured.')
    names = set()
    for check in checks:
        if not re.fullmatch('[a-z]+(?:_[a-z]+)*', check.get('name', '')) or check['name'] in names:
            raise QueueError('Gate names must be unique full-word identifiers.')
        names.add(check['name'])
        if check.get('kind') not in {'compilation', 'final', 'specialist'}:
            raise QueueError('Every check must be a required compilation/final/specialist gate.')
        if not check.get('command') or not all(isinstance(arg, str) and arg for arg in check['command']):
            raise QueueError('Gate commands require nonempty argument arrays.')
        if not check.get('inputs') or any(p != '.' and (p.startswith(('/', '\\')) or ':' in p or '..' in Path(p).parts) for p in check['inputs']):
            raise QueueError('Declare every gate input path, using . when uncertain.')
        if type(check.get('timeout_seconds')) is not int or not 1 <= check['timeout_seconds'] <= 7200:
            raise QueueError('Gate timeout must be between 1 and 7200 seconds.')
    repair = policy.get('repair', {})
    if repair.get('enabled'):
        if repair.get('window_seconds') != 10800 or not all(repair.get(key) for key in ('executable', 'model', 'effort')):
            raise QueueError('Authorized automatic repair requires a selected local Codex model/effort and a three-hour window.')
    return policy


def configure(root, remote, workspace_root, policy):
    """Operator setup reserves a single new root; an existing reservation is never overwritten."""
    root, workspace_root = plain(root), plain(workspace_root)
    validate_policy(policy)
    if len(str(workspace_root)) + 220 > 259:
        raise QueueError('Choose a shorter processor root; 220 characters are reserved for Unity descendants.')
    if workspace_root.exists():
        raise QueueError('Choose a new empty processor root; existing content will not be adopted or overwritten.')
    control = common(root) / 'gameforge_queue'
    with exclusive(control / 'configuration.lock'):
        if (control / 'configuration.json').exists():
            raise QueueError('This repository already reserves a processor root. Reuse its existing configuration.')
        if git(root, 'ls-remote', '--refs', remote, 'refs/heads/codex/integration-lock', 'refs/heads/codex/queue/*'):
            raise QueueError('A legacy queue exists. Explicit migration is required; a second queue cannot be installed beside it.')
        main = remote_ref(root, remote, 'refs/heads/main')
        if not main:
            raise QueueError('Remote main is missing.')
        next_version(git(root, 'show', main + ':' + policy['version_file']).strip(), 'patch')
        config = dict(schema=SCHEMA, repository=str(root), remote=remote, workspace_root=str(workspace_root),
                      remote_url=git(root, 'remote', 'get-url', remote), policy=policy, policy_hash=digest(policy),
                      runner=str(Path(__file__).resolve()), runner_hash=runner_hash(), created_utc=utc())
        workspace_root.mkdir()
        durable(workspace_root / 'ownership.json', config)
        durable(control / 'configuration.json', config)
        return config


def runner_hash():
    return digest({path.name: digest(path.read_bytes()) for path in Path(__file__).parent.glob('GitFlow*.py')})


def configuration(root):
    config = read_json(common(root) / 'gameforge_queue/configuration.json')
    if config.get('schema') != SCHEMA or config.get('policy_hash') != digest(config.get('policy')):
        raise QueueError('Damaged or unsupported processor configuration; preserve its bytes.')
    if config['runner_hash'] != runner_hash() or config['runner'] != str(Path(__file__).resolve()):
        raise QueueError('Configured runner differs. A stopped-owner reviewed upgrade is required.')
    owner = read_json(plain(config['workspace_root']) / 'ownership.json')
    if owner != config:
        raise QueueError('Workspace ownership marker does not match the repository reservation.')
    validate_policy(config['policy'])
    if git(root, 'remote', 'get-url', config['remote']) != config['remote_url']:
        raise QueueError('Configured remote changed; operator reconciliation required.')
    return config


def upgrade_runner(root, expected_configuration):
    """An explicit stopped-owner action adopts this installed runner without updating any package or resetting work."""
    control = common(root) / 'gameforge_queue'
    with exclusive(control / 'service.lock'), exclusive(control / 'processor.lock'), exclusive(control / 'configuration.lock'):
        path = control / 'configuration.json'
        prior = read_json(path)
        if digest(prior) != expected_configuration or prior.get('schema') != SCHEMA:
            raise QueueError('Configuration changed. Inspect it again before a reviewed runner upgrade.')
        root_owner = plain(prior['workspace_root']) / 'ownership.json'
        if read_json(root_owner) != prior or prior['policy_hash'] != digest(prior['policy']):
            raise QueueError('Ownership/configuration mismatch; preserve both records for reconciliation.')
        if git(root, 'remote', 'get-url', prior['remote']) != prior['remote_url']:
            raise QueueError('Remote identity changed; upgrade cannot proceed.')
        owner = inspect_owner(root, prior['remote'])
        if owner['state'] not in {'released', 'not_configured'}:
            raise QueueError('Shared owner is not verifiably released. A stale or unknown observation is not upgrade authorization.')
        revised = dict(prior)
        revised['policy'] = json.loads(encoded(prior['policy']))
        old_folder = Path(prior['runner']).parent
        new_folder = Path(__file__).resolve().parent
        for command in [item['command'] for item in revised['policy']['checks']] + [revised['policy'].get('native_version_command', [])]:
            for index, argument in enumerate(command):
                if Path(argument).parent == old_folder and Path(argument).name == 'GitFlowUnity.py':
                    command[index] = str(new_folder / 'GitFlowUnity.py')
        validate_policy(revised['policy'])
        revised.update(runner=str(Path(__file__).resolve()), runner_hash=runner_hash(), policy_hash=digest(revised['policy']), upgraded_utc=utc())
        revised['runner_history'] = [*prior.get('runner_history', []), dict(runner=prior['runner'], runner_hash=prior['runner_hash'], policy_hash=prior['policy_hash'])]
        journal = control / ('upgrade_' + uuid.uuid4().hex + '.json')
        durable(journal, dict(state='prepared', prior=prior, revised=revised))
        durable(root_owner, revised)
        durable(path, revised)
        durable(journal, dict(state='completed', prior=prior, revised=revised))
        return dict(state='runner_adopted', detail='Existing installed runner adopted explicitly. No package was installed or updated. Restart separately; candidate, requests, repair start and evidence were preserved.')


def input_key(workspace, check, policy_hash, environment):
    """Relevant Git objects, exact command and declared environment define reusable evidence."""
    objects = {path: git(workspace, 'ls-tree', '-r', 'HEAD', '--', path) if path != '.' else
               git(workspace, 'rev-parse', 'HEAD^{tree}') for path in check['inputs']}
    return digest(dict(objects=objects, check=check, policy=policy_hash, environment=environment))


def execute_gate(workspace, evidence_root, check, candidate, policy_hash, cache):
    """Mandatory final gates always run. Specialist reuse requires existing matching bytes."""
    environment = dict(python=sys.version, platform=sys.platform, host=socket.gethostname(),
                       git=git(workspace, '--version'), declared=check.get('environment', {}),
                       tools={arg: file_digest(arg) for arg in check['command'] if Path(arg).is_absolute() and Path(arg).is_file()})
    key = input_key(workspace, check, policy_hash, environment)
    cached = cache.get(key)
    if check['kind'] == 'specialist' and cached:
        folder = plain(cached['folder'])
        if all(plain(folder / name).is_file() and digest((folder / name).read_bytes()) == value
               for name, value in cached['artifacts'].items()):
            return dict(cached, reused=True, candidate=candidate)
    identity = uuid.uuid4().hex
    folder = plain(evidence_root / (check['name'] + '_' + identity))
    folder.mkdir(parents=True)
    report = folder / 'result.json'
    arguments = [arg.replace('{repository}', str(workspace)).replace('{evidence}', str(folder))
                 .replace('{run}', identity).replace('{candidate}', candidate).replace('{python}', sys.executable)
                 for arg in check['command']]
    started = utc()
    durable(folder / 'invocation.json', dict(run=identity, candidate=candidate, inputs=key,
            command=arguments, environment=environment, started_utc=started))
    try:
        active = leases.ACTIVE.get()
        with (folder / 'output.log').open('wb') as output:
            result_process = process_run(arguments, cwd=workspace, stdout=output, stderr=output,
                timeout=check['timeout_seconds'], cancelled=active.stop if active else None,
                env=dict(os.environ, GIT_TERMINAL_PROMPT='0', GCM_INTERACTIVE='Never'))
        if result_process.returncode:
            raise QueueError('Required gate ' + check['name'] + ' failed with exit ' + str(result_process.returncode) + '; full output retained at ' + str(folder))
        clean(workspace)
        if git(workspace, 'rev-parse', 'HEAD') != candidate:
            raise QueueError('Gate changed candidate HEAD.')
        result = read_json(report)
        if (result.get('run') != identity or result.get('candidate') != candidate or result.get('status') != 'passed'
                or result.get('warnings') != 0 or result.get('errors') != 0 or result.get('skipped', 0) != 0):
            raise QueueError('Gate lacks a successful exact-candidate report with zero warnings/errors/skips.')
        artifacts = {}
        total = 0
        for path in folder.iterdir():
            plain(path)
            if not path.is_file():
                raise QueueError('Gate artifacts must be regular flat files; unsupported evidence was retained.')
            total += path.stat().st_size
            if total > 128 * 1024 * 1024:
                raise QueueError('Gate evidence exceeds the 128 MiB portable evidence limit; preserve it and refine the owning adapter.')
            artifacts[path.name] = file_digest(path)
        value = dict(name=check['name'], kind=check['kind'], candidate=candidate, key=key, environment=environment,
                     started_utc=started, completed_utc=utc(), folder=str(folder), artifacts=artifacts, reused=False)
        cache[key] = value
        return value
    except Exception as error:
        durable(folder / 'failure.json', dict(run=identity, candidate=candidate, error=type(error).__name__ + ': ' + str(error)))
        raise GateFailure(str(error), folder) from error


def next_version(current, size):
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', current):
        raise QueueError('Version evidence is not a supported semantic version.')
    numbers = list(map(int, current.split('.')))
    position = {'major': 0, 'minor': 1, 'patch': 2}[size]
    numbers[position] += 1
    for index in range(position + 1, 3):
        numbers[index] = 0
    return '.'.join(map(str, numbers))


def stamp_version(workspace, policy, version, evidence_root):
    """Generic text-version adapter; native Unity synchronization requires its owning adapter."""
    path = plain(workspace / policy['version_file'])
    old = path.read_bytes()
    if old.decode('utf-8-sig').strip() == version:
        return  # Same-ID continuation retains its already assigned unpublished version.
    if policy.get('native_version_command'):
        folder = plain(evidence_root / ('version_' + uuid.uuid4().hex))
        folder.mkdir(parents=True, exist_ok=False)
        command = [arg.replace('{repository}', str(workspace)).replace('{version}', version).replace('{evidence}', str(folder)) for arg in policy['native_version_command']]
        try:
            run(command, workspace, timeout=7200)
        except QueueError as error:
            raise GateFailure(str(error), folder) from error
    path.write_bytes((version + ('\r\n' if b'\r\n' in old else '\n')).encode())
    allowed = [policy['version_file'], *policy.get('native_version_outputs', [])]
    changed = git(workspace, 'diff', '--name-only').splitlines()
    if any(path not in allowed for path in changed):
        raise QueueError('Version adapter changed unowned source; preserve the candidate.')
    git(workspace, 'add', '--', *allowed)
    git(workspace, 'commit', '-m', 'Assign integration version ' + version)
    clean(workspace)


def state_file(root):
    return common(root) / 'gameforge_queue/state.json'


def update_state(root, **value):
    active = leases.ACTIVE.get()
    if active is not None:
        active.work = dict(value)
    durable(state_file(root), dict(updated_utc=utc(), pid=os.getpid(), **value))


def publish_record(root, remote, lease, previous, reference, value, artifacts=None, targets=()):
    # Keep portable evidence in every later state, including a post-push pull failure or restart.
    inherited = {}
    for row in git(root, 'ls-tree', previous).splitlines():
        header, name = row.split('\t', 1)
        if not re.fullmatch(r'[A-Za-z0-9_.-]+', name) or header.split()[1] != 'blob':
            raise QueueError('Malformed queue control artifact tree.')
        inherited[name] = header.split()[2]
    child = object_commit(root, previous, value, 'Queue ' + value['state'] + ' ' + value['id'], artifacts, inherited)
    lease.publish(root, remote, [child + ':' + reference, *targets])
    if remote_ref(root, remote, reference) != child:
        lease.failed(QueueError('Published queue result could not be verified.'))
        raise QueueError('Publication is uncertain; reconcile this exact request instead of resubmitting.')
    return child


def verify_published_evidence(root, identity, value):
    """A resumed publication must retain its exact portable receipt and every referenced artifact."""
    receipt = json.loads(run(['git', 'show', identity + ':receipt.json'], root))
    if (receipt != value.get('receipt') or receipt.get('source') != value['source']
            or receipt.get('main_commit') != value['main_commit'] or not receipt.get('checks')):
        raise QueueError('Published receipt is missing or differs from the exact delivery.')
    for index, check in enumerate(receipt['checks']):
        if check.get('candidate') != value['main_commit'] or not check.get('artifacts'):
            raise QueueError('Published gate evidence does not identify the delivered candidate.')
        for name, expected in check['artifacts'].items():
            if not re.fullmatch(r'[A-Za-z0-9_.-]+', name):
                raise QueueError('Malformed portable evidence path.')
            content = run(['git', 'show', identity + ':' + str(index) + '-' + name], root)
            if digest(content) != expected:
                raise QueueError('Published validation artifact checksum differs.')


def workspace_for(root, config, main):
    workspace = plain(Path(config['workspace_root']) / 'Workspace')
    if not workspace.exists():
        git(root, 'worktree', 'add', '--detach', str(workspace), main)
    else:
        if common(workspace) != common(root):
            raise QueueError('Workspace Git identity differs from its reserved repository.')
        clean(workspace)
        head = git(workspace, 'rev-parse', 'HEAD')
        if not ancestor(workspace, head, main):
            raise QueueError('Retained candidate requires explicit same-request continuation; no replacement workspace will be created.')
        git(workspace, 'merge', '--ff-only', main)
    return workspace


def synchronize(root, config, workspace, value):
    """Complete the original publication without assigning a second version or rerunning checks."""
    remote = config['remote']
    main = remote_ref(root, remote, 'refs/heads/main')
    if not ancestor(root, value['main_commit'], main) or not ancestor(root, value['source'], main):
        raise QueueError('Remote main does not contain the recorded exact delivery/source.')
    clean(workspace)
    git(workspace, 'pull', '--ff-only', remote, 'main')
    original = 'not_on_main'
    if git(root, 'branch', '--show-current') == 'main':
        clean(root)
        git(root, 'pull', '--ff-only', remote, 'main')
        original = 'passed'
    return dict(publisher='passed', original=original, verified_main=main)


def process_once(root, expected_request=None, expected_candidate=None):
    """Drain one oldest request. Any blocked request stops progression; there is no bypass selector."""
    root = plain(root)
    config = configuration(root)
    control = common(root) / 'gameforge_queue'
    with exclusive(control / 'processor.lock'):
        remote = config['remote']
        rows = [row for row in entries(root, remote) if row[2]['state'] != 'integrated']
        if not rows:
            update_state(root, state='idle')
            return {'state': 'idle'}
        previous, reference, value = rows[0]
        if value['state'] not in {'queued', 'published'} and not expected_request:
            update_state(root, state='blocked', request=value['id'], detail='Explicit same-request continuation required; no automatic retry.')
            return value
        if expected_request and expected_request != previous:
            raise QueueError('Request changed or is not the oldest blocked request. Review it again.')
        lease = leases.Lease(sys.modules[__name__], root, remote)
        # Only our remote tracking main is updated; all observation fetches leave FETCH_HEAD alone.
        main = remote_ref(root, remote, 'refs/heads/main')
        git(root, 'update-ref', 'refs/remotes/' + remote + '/main', main)
        lease.acquire()
        lease.start()
        ownership_token = leases.ACTIVE.set(lease)
        workspace = plain(Path(config['workspace_root']) / 'Workspace')
        evidence_root = plain(Path(config['workspace_root']) / 'Evidence' / value['id'])
        artifacts = {}
        try:
            if value['state'] == 'published':
                verify_published_evidence(root, previous, value)
                value['pulls'] = synchronize(root, config, workspace, value)
                value['state'] = 'integrated'
                publish_record(root, remote, lease, previous, reference, value)
                update_state(root, state='integrated', request=value['id'])
                return value
            all_rows = {row[2]['id']: row[2] for row in entries(root, remote)}
            for dependency in value['dependencies']:
                dependency_value = all_rows.get(dependency)
                if not dependency_value or dependency_value['state'] != 'integrated' or not ancestor(root, dependency_value['source'], main):
                    raise QueueError('Dependency is not verifiably integrated: ' + dependency)
            if expected_request:
                clean(workspace)
                head = git(workspace, 'rev-parse', 'HEAD')
                if head != expected_candidate or not ancestor(workspace, value['source'], head):
                    raise QueueError('Continuation requires the reviewed clean candidate with original source ancestry.')
                if not ancestor(workspace, main, head):
                    git(workspace, 'merge', '--no-ff', '--no-edit', main)
                candidate = git(workspace, 'rev-parse', 'HEAD')
                if value['state'] in {'validation_failed', 'blocked'} and value['attempts'] and candidate == value['attempts'][-1].get('candidate'):
                    raise QueueError('Unchanged failed inputs are not retried. Supply a reviewed source correction.')
            else:
                workspace = workspace_for(root, config, main)
            value['state'] = 'preparing'
            previous = publish_record(root, remote, lease, previous, reference, value)
            update_state(root, state='preparing', request=value['id'])
            def checkpoint():
                nonlocal previous
                previous = publish_record(root, remote, lease, previous, reference, value)

            def repair_failure(failure):
                from GitFlowRepair import repair
                update_state(root, state='repairing', request=value['id'], detail=str(failure))
                return repair(sys.modules[__name__], workspace, value, config['policy'], failure, evidence_root, checkpoint, lease)

            if not expected_request:
                try:
                    git(workspace, 'merge', '--no-ff', '--no-edit', value['source'])
                except QueueError as error:
                    repair_failure(error)
            policy = config['policy']
            main_version = git(root, 'show', main + ':' + policy['version_file']).strip()
            version = next_version(main_version, value['size'])
            while True:
                try:
                    stamp_version(workspace, policy, version, evidence_root)
                    break
                except GateFailure as error:
                    repair_failure(error)
            candidate = git(workspace, 'rev-parse', 'HEAD')
            attempt = dict(candidate=candidate, base_main=main, version=version, started_utc=utc(), policy=config['policy_hash'])
            value['attempts'].append(attempt)
            value.update(state='validating', candidate=candidate, version=version)
            previous = publish_record(root, remote, lease, previous, reference, value)
            update_state(root, state='validating', request=value['id'], candidate=candidate)
            cache_path = Path(config['workspace_root']) / 'evidence_cache.json'
            cache = read_json(cache_path) if cache_path.exists() else {}
            while True:
                checks = []
                try:
                    for check in policy['checks']:
                        lease.guard()
                        result = execute_gate(workspace, evidence_root, check, candidate, config['policy_hash'], cache)
                        checks.append(result)
                        durable(cache_path, cache)
                    break
                except QueueError as error:
                    attempt.update(failure=str(error), finished_utc=utc())
                    checkpoint()
                    candidate = repair_failure(error)
                    value['candidate'] = candidate
                    attempt = dict(candidate=candidate, base_main=main, version=version, started_utc=utc(), policy=config['policy_hash'])
                    value['attempts'].append(attempt)
                    checkpoint()
                    update_state(root, state='validating', request=value['id'], candidate=candidate)
            clean(workspace)
            if git(workspace, 'rev-parse', 'HEAD') != candidate:
                raise QueueError('Candidate changed while validation was running.')
            if remote_ref(root, remote, 'refs/heads/main') != main:
                raise QueueError('Main moved during validation. Candidate/evidence retained; review one explicit continuation.')
            # Evidence lives in the request commit, outside source and the source fingerprint.
            for index, check in enumerate(checks):
                for name, expected in check['artifacts'].items():
                    content = plain(Path(check['folder']) / name).read_bytes()
                    if digest(content) != expected:
                        raise QueueError('Validation artifact changed before publication.')
                    artifacts[str(index) + '-' + name] = content
            receipt = dict(schema=SCHEMA, request=value['id'], source=value['source'], main_commit=candidate,
                           version=version, base_main=main, checks=checks, policy=config['policy_hash'], completed_utc=utc())
            artifacts['receipt.json'] = encoded(receipt).encode()
            value.update(state='published', main_commit=candidate, receipt=receipt)
            update_state(root, state='publishing', request=value['id'], candidate=candidate)
            previous = publish_record(root, remote, lease, previous, reference, value, artifacts,
                                      [candidate + ':refs/heads/main'])
            value['pulls'] = synchronize(root, config, workspace, value)
            value['state'] = 'integrated'
            publish_record(root, remote, lease, previous, reference, value, artifacts)
            update_state(root, state='integrated', request=value['id'])
            return value
        except Exception as error:
            detail = type(error).__name__ + ': ' + (str(error) or 'No error details supplied')
            evidence_root.mkdir(parents=True, exist_ok=True)
            durable(evidence_root / ('failure_' + uuid.uuid4().hex + '.json'), dict(request=value['id'], state=value['state'], error=detail, utc=utc()))
            # An uncertain write must never be followed by another write from that owner.
            if not lease.failure:
                if value['state'] != 'published':
                    value['state'] = 'validation_failed' if value['state'] == 'validating' else 'blocked'
                value['failure'] = detail
                try:
                    publish_record(root, remote, lease, previous, reference, value)
                except Exception as failure:
                    detail += '; result publication: ' + str(failure)
            update_state(root, state='blocked', request=value['id'], detail=detail)
            raise QueueError(detail) from error
        finally:
            if lease.failure:
                durable(control / 'lease_failure.json', dict(owner=lease.owner, held=lease.oid,
                        attempted=lease.attempted_oid, verification=lease.verification, error=lease.failure, utc=utc()))
            try:
                lease.close()
            finally:
                leases.ACTIVE.reset(ownership_token)


def inspect_owner(root, remote):
    """One verified shared-ref observation, never a liveness test or takeover decision."""
    observed = utc()
    try:
        identity, value = leases.snapshot(sys.modules[__name__], root, remote)
        if not identity:
            return dict(state='not_configured', summary='No shared GitFlow lease was observed.', observed_utc=observed, process_health='unknown')
        leases.validate(value)
        if remote_ref(root, remote, LOCK) != identity:
            return dict(state='changed_during_read', summary='Shared owner changed during refresh. Refresh again; ownership is unknown.',
                        observed_utc=observed, process_health='unknown')
        state = 'released' if value['state'] == 'released' else 'recorded_owned'
        summary = 'Available — last owner released the lease.' if state == 'released' else (
            'Recorded owner: ' + value['host'] + ' | stage: ' + str(value.get('stage') or 'unknown') +
            ' | request: ' + str(value.get('request') or 'none recorded'))
        return dict(state=state, summary=summary, observed_utc=observed, control_commit=identity,
                    last_recorded_renewal=value['updated_utc'], owner=value['owner'], machine=value['host'],
                    recorded_process_id=value.get('process_id'), request=value.get('request'), stage=value.get('stage'),
                    process_health='unknown', detail='Exact ref/object observed. A PID or audit timestamp does not prove health, progress or expiry.')
    except Exception as error:
        return dict(state='unknown', summary='Shared ownership unavailable: ' + type(error).__name__ + ': ' + str(error),
                    observed_utc=observed, process_health='unknown')


def serve(root):
    """One ordinary background program per repository; no AI monitoring, implicit retry or new workspace."""
    configuration(root)
    control = common(root) / 'gameforge_queue'
    with exclusive(control / 'service.lock'):
        if (control / 'stop.json').exists():
            update_state(root, state='stopped', detail='A graceful stop is recorded. Explicit operator resume is required.')
            return
        while not (control / 'stop.json').exists():
            try:
                result = process_once(root)
                if result['state'] not in {'integrated', 'idle'}:
                    return
                if result['state'] == 'integrated':
                    continue
            except leases.Contended:
                update_state(root, state='waiting_for_owner', detail='Another owner holds the renewable lease; observing with bounded backoff.')
            except Exception as error:
                update_state(root, state='blocked', detail=type(error).__name__ + ': ' + str(error))
                return
            for _ in range(60):
                if (control / 'stop.json').exists():
                    break
                time.sleep(1)
        update_state(root, state='stopped', detail='Graceful stop completed between requests.')


def resume_service(root):
    """Remove only a proven stopped-owner request; never clear a live service lock."""
    configuration(root)
    control = common(root) / 'gameforge_queue'
    with exclusive(control / 'service.lock'), exclusive(control / 'processor.lock'):
        path = plain(control / 'stop.json')
        if path.exists():
            if read_json(path).get('schema') != SCHEMA:
                raise QueueError('Unknown stop marker; preserve it for operator review.')
            path.unlink()
    return {'state': 'ready_to_start', 'detail': 'Preserved configuration and workspace. Blocked requests still require exact continuation.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', required=True)
    parser.add_argument('--remote', default='origin')
    subcommands = parser.add_subparsers(dest='command', required=True)
    submission = subcommands.add_parser('submit')
    submission.add_argument('--title', required=True)
    submission.add_argument('--summary', required=True)
    submission.add_argument('--size', choices=['patch', 'minor', 'major'], required=True)
    submission.add_argument('--dependency', action='append', default=[])
    capture = subcommands.add_parser('queue-local-changes')
    capture.add_argument('--scope', action='append', required=True)
    capture.add_argument('--title', default='Saved Unity project changes')
    capture.add_argument('--summary', default='Capture the saved first-party changes in the configured task scope.')
    capture.add_argument('--size', choices=['patch', 'minor', 'major'], default='patch')
    preview = subcommands.add_parser('preview-local-changes')
    preview.add_argument('--scope', action='append', required=True)
    setup = subcommands.add_parser('configure')
    setup.add_argument('--workspace-root', required=True)
    setup.add_argument('--policy', required=True)
    setup.add_argument('--operator', action='store_true', required=True)
    processing = subcommands.add_parser('process')
    processing.add_argument('--operator', action='store_true', required=True)
    processing.add_argument('--expected-request')
    processing.add_argument('--expected-candidate')
    subcommands.add_parser('inspect')
    subcommands.add_parser('storage')
    for name in ('serve', 'request-stop', 'resume-service'):
        command = subcommands.add_parser(name)
        command.add_argument('--operator', action='store_true', required=True)
    upgrade = subcommands.add_parser('upgrade-runner')
    upgrade.add_argument('--operator', action='store_true', required=True)
    upgrade.add_argument('--expected-configuration', required=True)
    arguments = parser.parse_args()
    root = plain(arguments.repository)
    if arguments.command == 'submit':
        result = submit(root, arguments.remote, arguments.title, arguments.size, arguments.summary, arguments.dependency)
    elif arguments.command == 'queue-local-changes':
        result = queue_local_changes(root, arguments.remote, arguments.scope, arguments.title, arguments.size, arguments.summary)
    elif arguments.command == 'preview-local-changes':
        from GitFlowCapture import inventory
        result = dict(state='preview', **inventory(sys.modules[__name__], root, arguments.scope))
    elif arguments.command == 'configure':
        result = configure(root, arguments.remote, arguments.workspace_root, read_json(arguments.policy))
    elif arguments.command == 'process':
        result = process_once(root, arguments.expected_request, arguments.expected_candidate)
    elif arguments.command == 'serve':
        serve(root)
        return
    elif arguments.command == 'request-stop':
        configuration(root)
        durable(common(root) / 'gameforge_queue/stop.json', dict(schema=SCHEMA, requested_utc=utc()))
        result = {'state': 'stop_requested', 'detail': 'The current owned request finishes before shutdown. This is not stopped-owner proof.'}
    elif arguments.command == 'resume-service':
        result = resume_service(root)
    elif arguments.command == 'upgrade-runner':
        result = upgrade_runner(root, arguments.expected_configuration)
    elif arguments.command == 'storage':
        config = configuration(root)
        result = inspect_storage([config['workspace_root'], common(root)])
    else:
        result = {'requests': [dict(value, control_commit=identity) for identity, _, value in entries(root, arguments.remote)]}
        result['owner'] = inspect_owner(root, arguments.remote)
        result['owner_summary'] = result['owner']['summary']
        if state_file(root).exists():
            result['recorded_processor'] = read_json(state_file(root))
        config_path = common(root) / 'gameforge_queue/configuration.json'
        if config_path.exists():
            config = read_json(config_path)
            result['configuration_identity'] = digest(config)
            result['recorded_runner'] = dict(path=config.get('runner'), hash=config.get('runner_hash'))
    print(encoded(result))


if __name__ == '__main__':
    try:
        main()
    except (QueueError, leases.LeaseError, OSError, ValueError) as failure:
        print(encoded({'state': 'blocked', 'error': type(failure).__name__ + ': ' + str(failure)}))
        sys.exit(1)
