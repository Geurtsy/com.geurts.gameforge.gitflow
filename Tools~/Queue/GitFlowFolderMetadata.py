# IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md
# and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
"""Native-created folder sidecars only; no GUID generation or unknown-file cleanup."""
import base64
import os
from pathlib import Path
import re
import uuid

PLAN = 'native-folder-metadata-plan.json'
PROOF = 'native-folder-metadata-proof.json'
RECOVERY = 'native-folder-metadata-recovery.json'
ENVIRONMENT = 'GITFLOW_NATIVE_FOLDER_METADATA_PLAN'
FORBIDDEN = {'.git', '.github', '.codex', 'plugins', 'packages', 'library', 'temp', 'obj', 'vendor', 'thirdparty', '_thirdparty'}


class Blocked(Exception):
    """Unproven metadata must not consume model calls or weaken the clean guard."""


class Recovered(Exception):
    """Native metadata changed HEAD; all required gates must validate the new candidate."""
    def __init__(self, proof):
        super().__init__('Native folder metadata recovered; validate the new candidate.')
        self.proof = proof


def index_identity(q, workspace):
    path = Path(q.git(workspace, 'rev-parse', '--path-format=absolute', '--git-path', 'index'))
    return q.file_digest(q.plain(path))


def untracked(q, workspace):
    return sorted(p for p in q.git(workspace, 'ls-files', '--others', '--exclude-standard', '-z').split('\0') if p)


def metadata_guid(raw):
    text = raw.decode('utf-8-sig')
    matches = re.findall(r'^guid: ([0-9a-f]{32})\r?$', text, re.M)
    if '\0' in text or len(matches) != 1 or not re.search(r'^fileFormatVersion: 2\r?$', text, re.M):
        raise Blocked('Native folder metadata has an unsupported format; originals retained.')
    if re.search(r'^folderAsset:', text, re.M) and not re.search(r'^folderAsset: yes\r?$', text, re.M):
        raise Blocked('Native metadata does not identify a folder.')
    return matches[0]


def prepare(q, workspace, folder, policy, allowed_tracked=()):
    settings = policy.get('repair', {}) if policy else {}
    if settings.get('enabled') is not True or settings.get('native_folder_metadata', True) is False:
        return None
    # Pre-existing untracked content is never attributed to a later native invocation.
    if untracked(q, workspace):
        return None
    editors = sorted({str(q.plain(command[index + 1])) for command in
        [policy.get('native_version_command', []), *[item['command'] for item in policy.get('checks', [])]]
        for index, arg in enumerate(command[:-1]) if arg == '--unity'})
    if not editors:
        return None
    head = q.git(workspace, 'rev-parse', 'HEAD')
    tree = q.run(['git', 'ls-tree', '-r', '-t', '-z', head], workspace).decode('utf-8')
    entries = {}
    for row in tree.split('\0'):
        if row:
            header, path = row.split('\t', 1)
            mode, kind, identity = header.split()
            entries[path] = (mode, kind, identity)
    marker = 'ProjectSettings/ProjectVersion.txt'
    projects = sorted(path[:-len(marker)].rstrip('/') for path in entries if entries[path][:2] == ('100644', 'blob') and (path == marker or path.endswith('/' + marker)))
    projects = [p for p in projects if not ({part.casefold() for part in p.split('/')} & (FORBIDDEN | {'assets'}))]
    roots = [prefix + 'Assets/' + name for project in projects for prefix in [project + '/' if project else ''] for name in ('_Project', '_Generated')]
    planned = []
    for path, (mode, kind, identity) in sorted(entries.items()):
        if kind != 'tree' or path + '.meta' in entries or not any(path == root or path.startswith(root + '/') for root in roots):
            continue
        if set(part.casefold() for part in path.split('/')) & FORBIDDEN or any(ord(c) < 32 for c in path):
            continue
        directory, metadata = q.plain(workspace / path), q.plain(workspace / (path + '.meta'))
        if directory.is_dir() and not metadata.exists():
            planned.append(dict(folder=path, metadata=path + '.meta', tree=identity))
    if not planned:
        return None
    supplied = b''.join(item['metadata'].encode('utf-8') + b'\0' for item in planned)
    try:
        ignored = set(p for p in q.run(['git', 'check-ignore', '--no-index', '-z', '--stdin'], workspace, data=supplied).decode('utf-8').split('\0') if p)
    except q.QueueError as error:
        if not str(error).startswith('git failed (1):'):
            raise
        ignored = set()
    planned = [item for item in planned if item['metadata'] not in ignored]
    if not planned:
        return None
    plan = dict(protocol=1, invocation=uuid.uuid4().hex, repository=str(q.plain(workspace)), candidate=head,
                index=index_identity(q, workspace), projects=projects, editors=editors, paths=planned, allowed_tracked=list(allowed_tracked))
    q.durable(folder / PLAN, plan)
    return plan


def environment(folder, plan):
    return {ENVIRONMENT: str(folder / PLAN)} if plan else {ENVIRONMENT: ''}


def begin_native(q, project, editor):
    selected = os.environ.get(ENVIRONMENT)
    if not selected:
        return None
    path = q.plain(selected)
    plan = q.read_json(path)
    workspace = q.plain(plan['repository'])
    relative = q.plain(project).relative_to(workspace).as_posix()
    if relative == '.':
        relative = ''
    if plan.get('protocol') != 1 or relative not in plan['projects']:
        raise Blocked('Native metadata plan belongs to another Unity project.')
    if str(q.plain(editor)) not in plan['editors']:
        raise Blocked('Native metadata plan does not authorize this configured Editor.')
    if q.git(workspace, 'rev-parse', 'HEAD') != plan['candidate'] or index_identity(q, workspace) != plan['index']:
        raise Blocked('Candidate or index changed before native metadata import.')
    selected_paths = [item for item in plan['paths'] if item['folder'].startswith((relative + '/' if relative else '') + 'Assets/')]
    if any(q.plain(workspace / item['metadata']).exists() for item in selected_paths):
        raise Blocked('Metadata existed before the native child; provenance is unverified.')
    return dict(plan=plan, plan_path=path, paths=selected_paths, project=str(q.plain(project)))


def finish_native(q, observation, editor, returncode):
    if not observation:
        return
    plan = observation['plan']; workspace = q.plain(plan['repository'])
    if q.git(workspace, 'rev-parse', 'HEAD') != plan['candidate'] or index_identity(q, workspace) != plan['index']:
        raise Blocked('Candidate or index changed during native metadata import.')
    outputs = []
    for item in observation['paths']:
        path = q.plain(workspace / item['metadata'])
        if path.exists():
            if not path.is_file() or not q.plain(workspace / item['folder']).is_dir():
                raise Blocked('Native folder metadata is not an ordinary sidecar.')
            raw = path.read_bytes()
            outputs.append(dict(item, guid=metadata_guid(raw), raw_sha256=q.digest(raw), raw_base64=base64.b64encode(raw).decode('ascii')))
    q.durable(observation['plan_path'].with_name(PROOF), dict(protocol=1, invocation=plan['invocation'],
        plan_sha256=q.file_digest(observation['plan_path']), candidate=plan['candidate'], index=plan['index'],
        project=observation['project'], editor=str(q.plain(editor)), native_child_stopped=True,
        native_returncode=returncode, outputs=outputs))


def harvest(q, workspace, folder, plan):
    try:
        return _harvest(q, workspace, folder, plan)
    except Blocked:
        raise
    except Exception as error:
        raise Blocked('Native folder metadata could not be verified; artifacts and source retained.') from error


def _harvest(q, workspace, folder, plan):
    if not plan:
        return None
    added = untracked(q, workspace)
    if not added:
        return None
    proof_path = q.plain(folder / PROOF)
    if not proof_path.is_file():
        raise Blocked('New folder metadata lacks native provenance; files and diagnostics retained.')
    proof = q.read_json(proof_path)
    if (q.read_json(folder / PLAN) != plan or proof.get('protocol') != 1 or proof.get('invocation') != plan['invocation'] or
            proof.get('plan_sha256') != q.file_digest(folder / PLAN) or proof.get('candidate') != plan['candidate'] or
            proof.get('index') != plan['index'] or proof.get('native_child_stopped') is not True or
            proof.get('editor') not in plan['editors'] or
            proof.get('project') not in {str(q.plain(workspace / p)) for p in plan['projects']} or
            q.git(workspace, 'rev-parse', 'HEAD') != plan['candidate'] or index_identity(q, workspace) != plan['index']):
        raise Blocked('Native folder metadata proof is stale or incomplete.')
    expected = {item['metadata']: item for item in plan['paths']}
    outputs = proof.get('outputs', [])
    names = [item['metadata'] for item in outputs]
    if set(added) != set(names) or len(names) != len(set(names)) or not set(names) <= expected.keys():
        raise Blocked('Unrelated untracked content remains; no metadata was staged.')
    changed = q.git(workspace, 'diff', '--name-only').splitlines()
    if not set(changed) <= set(plan['allowed_tracked']):
        raise Blocked('Native import changed undeclared tracked source; no metadata was staged.')
    guids = set()
    for item in outputs:
        path = q.plain(workspace / item['metadata']); raw = path.read_bytes()
        if not q.plain(workspace / item['folder']).is_dir():
            raise Blocked('Native folder changed after import; metadata retained.')
        if item['folder'] != expected[item['metadata']]['folder'] or item['tree'] != expected[item['metadata']]['tree']:
            raise Blocked('Native folder membership differs from the tracked source tree.')
        guid = metadata_guid(raw)
        if guid != item['guid'] or guid in guids or q.digest(raw) != item['raw_sha256'] or base64.b64decode(item['raw_base64'], validate=True) != raw:
            raise Blocked('Native folder metadata bytes or GUID changed after import.')
        guids.add(guid)
    arguments = ['git', 'grep', '-I', '-n', '-F']
    for guid in sorted(guids):arguments += ['-e', 'guid: ' + guid]
    try:
        collisions = q.run([*arguments, plan['candidate'], '--', '*.meta'], workspace)
    except q.QueueError as error:
        if not str(error).startswith('git failed (1):'):raise
        collisions = b''
    if collisions:
        raise Blocked('Native folder GUID collides with tracked metadata; originals retained.')
    active = q.leases.ACTIVE.get()
    if active:
        active.guard()
    q.git(workspace, 'add', '--', *names)
    if set(q.git(workspace, 'diff', '--cached', '--name-only').splitlines()) != set(names):
        raise Blocked('Index diverged during native folder metadata recovery; preserve it.')
    if active:
        active.guard()
    q.git(workspace, 'commit', '-m', 'Recover native folder metadata for tracked Unity assets')
    recovery = dict(protocol=1, candidate=q.git(workspace, 'rev-parse', 'HEAD'), previous_candidate=plan['candidate'],
                    outputs=outputs, recovered_folders=len(outputs), native_returncode=proof['native_returncode'], model_invoked=False,
                    proof_sha256=q.file_digest(proof_path), completed_utc=q.utc())
    q.durable(folder / RECOVERY, recovery)
    return recovery
