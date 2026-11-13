"""Explicit immutable publisher copies; package-cache removal must not erase active tools."""
from pathlib import Path
import re
import runpy
import sys
import uuid

REQUIRED = {'GitFlowQueue.py', 'GitFlowLease.py', 'GitFlowRepair.py', 'GitFlowModels.py',
            'GitFlowUnity.py', 'GitFlowProcess.py', 'GitFlowStorage.py', 'GitFlowCapture.py',
            'GitFlowObserve.py', 'GitFlowPublisher.py', 'GitFlowDeployment.py', 'GitFlowRepairWorkspace.py', 'GitFlowSession.py'}


def deploy(q, workspace):
    """Copy the complete reviewed runner once into a new owner-local directory; never replace it."""
    source = q.plain(Path(q.__file__).resolve())
    files = {path.name: q.plain(path).read_bytes() for path in source.parent.glob('GitFlow*.py')}
    if not REQUIRED <= files.keys():
        raise q.QueueError('The installed publisher is incomplete; repair/native modules must exist before adoption.')
    identity = q.digest({name: q.digest(raw) for name, raw in files.items()})
    destination = q.plain(Path(workspace) / 'Runner' / uuid.uuid4().hex)
    for name in files:
        q.plain(destination / name)
    destination.mkdir(parents=True, exist_ok=False)
    for name, raw in files.items():
        with q.plain(destination / name).open('xb') as output:
            output.write(raw)
    q.durable(destination / 'deployment.json', dict(schema=1, source_runner=str(source),
        runner_hash=identity, files={name: q.digest(raw) for name, raw in files.items()}))
    return dict(runner=str(destination / 'GitFlowQueue.py'), runner_source=str(source), runner_hash=identity)


def current(q, config):
    """Accept legacy exact-path owners or a complete bound deployment, without acquiring/upgrading anything."""
    actual = str(Path(q.__file__).resolve())
    if config.get('runner_hash') != q.runner_hash():
        return False
    if not config.get('runner_source'):
        return config.get('runner') == actual
    runner = q.plain(config['runner'])
    workspace = q.plain(config['workspace_root'])
    if (runner.name != 'GitFlowQueue.py' or runner.parent.parent != workspace / 'Runner'
            or not re.fullmatch('[a-f0-9]{32}', runner.parent.name)
            or actual not in {config['runner'], config['runner_source']}):
        return False
    manifest = q.read_json(runner.parent / 'deployment.json')
    files = manifest.get('files')
    if (manifest.get('schema') != 1 or manifest.get('source_runner') != config['runner_source']
            or manifest.get('runner_hash') != config['runner_hash'] or not isinstance(files, dict)
            or not REQUIRED <= files.keys()):
        return False
    paths = {path.name: q.plain(path) for path in runner.parent.glob('GitFlow*.py')}
    if set(paths) != set(files):
        return False
    observed = {name: q.digest(path.read_bytes()) for name, path in paths.items()}
    return observed == files and q.digest(observed) == config['runner_hash']


def bind_native(policy, old_folders, new_folder):
    """Relocate only the owning native adapter; keep required gates and every other argument intact."""
    for command in [item['command'] for item in policy['checks']] + [policy.get('native_version_command', [])]:
        for index, argument in enumerate(command):
            path = Path(argument)
            if path.name == 'GitFlowUnity.py' and path.parent in old_folders:
                command[index] = str(new_folder / path.name)


def forward(q, root, command):
    """Run long-lived CLI work from the reviewed deployment in this same PID before ownership begins."""
    if command not in {'process', 'serve'}:
        return False
    config = q.configuration(root)
    runner = Path(config['runner'])
    if not config.get('runner_source') or runner == Path(q.__file__).resolve():
        return False
    # No leases, threads or writes have started. Reload the complete module family
    # from the preserved directory; leaving old lazy imports defeats the snapshot.
    sys.path.insert(0, str(runner.parent))
    for name in list(sys.modules):
        if name.startswith('GitFlow'):
            del sys.modules[name]
    runpy.run_path(str(runner), run_name='__main__')
    return True
