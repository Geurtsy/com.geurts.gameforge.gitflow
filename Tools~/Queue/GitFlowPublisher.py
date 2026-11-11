"""Read-only publisher defaults and local process observations; never configure or start work."""
import ctypes
from datetime import datetime, timezone
import hashlib
import os
from pathlib import Path
import re


def settings(queue, root, remote, active_unity):
    """Reuse exact existing policy or propose a deterministic short root and all Edit Mode tests."""
    root = queue.plain(root)
    control = queue.common(root) / 'gameforge_queue/configuration.json'
    if control.exists():
        config = queue.configuration(root)
        if config['remote'] != remote:
            raise queue.QueueError('Select the existing publisher remote: ' + config['remote'])
        unity, tests = '', ''
        for check in config['policy']['checks']:
            command = check['command']
            if '--unity' in command:
                unity = command[command.index('--unity') + 1]
            if '--test-filter' in command:
                tests = command[command.index('--test-filter') + 1]
        return dict(state='configured', workspace_root=config['workspace_root'], unity=unity,
                    test_filter=tests or '(existing required validation policy)',
                    automatic_repair=config['policy'].get('repair', {}).get('enabled', False),
                    process_health=process_health(queue.read_json(control.with_name('state.json'))) if control.with_name('state.json').exists() else 'not_started',
                    configuration_identity=queue.digest(config), detail='Existing policy retained unchanged.')
    version_file = queue.plain(root / 'ProjectSettings/ProjectVersion.txt')
    if not version_file.is_file():
        raise queue.QueueError('Automatic setup requires a Unity project. Use the reviewed-policy controls for other repositories.')
    match = re.search(r'^m_EditorVersion:\s*(\S+)\s*$', version_file.read_text(encoding='utf-8-sig'), re.M)
    if not match:
        raise queue.QueueError('Cannot determine this project\'s exact Unity Editor version.')
    version = match[1]
    # The active Editor is accepted only when its installation directory matches the selected project.
    candidates = [Path(active_unity)] if active_unity else []
    candidates.append(Path(os.environ.get('ProgramFiles', 'C:/Program Files')) / 'Unity/Hub/Editor' / version / 'Editor/Unity.exe')
    unity = next((path for path in candidates if path.is_file() and path.parent.parent.name == version), None)
    if unity is None:
        raise queue.QueueError('Install Unity ' + version + ' or select its executable manually in the publisher fields.')
    identity = hashlib.sha256(str(queue.common(root)).casefold().encode()).hexdigest()[:10]
    workspace = queue.plain(Path(root.anchor) / ('gfq' + identity))
    if len(str(workspace)) + 220 > 259 or workspace.exists():
        raise queue.QueueError('The suggested short workspace is unavailable: ' + str(workspace) + '. Choose a new root manually; existing content is preserved.')
    return dict(state='new_publisher', workspace_root=str(workspace), unity=str(unity), test_filter='.*',
                automatic_repair=False, detail='All Edit Mode tests are required. No matching tests, skipped tests, warnings or errors block publication.')


def process_health(processor):
    """Verify a Windows PID's lifetime against its recorded observation without acquiring a queue lock."""
    if processor is None:
        return 'not_started'
    if not isinstance(processor, dict):
        return 'unknown'
    process_id = processor.get('pid')
    if type(process_id) is not int or process_id <= 0 or os.name != 'nt':
        return 'unknown'
    from ctypes import wintypes
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(wintypes.FILETIME)] * 4
    kernel.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = kernel.OpenProcess(0x1000, False, process_id)
    if not handle:
        return 'not_running' if ctypes.get_last_error() == 87 else 'unknown'
    try:
        code = wintypes.DWORD()
        if not kernel.GetExitCodeProcess(handle, ctypes.byref(code)):
            return 'unknown'
        if code.value != 259:
            return 'not_running'
        created, exited, system, user = (wintypes.FILETIME() for _ in range(4))
        if not kernel.GetProcessTimes(handle, ctypes.byref(created), ctypes.byref(exited), ctypes.byref(system), ctypes.byref(user)):
            return 'unknown'
        if exited.dwHighDateTime or exited.dwLowDateTime:
            return 'not_running'
        born = ((created.dwHighDateTime << 32) | created.dwLowDateTime) / 10000000 - 11644473600
        observed = datetime.fromisoformat(processor['updated_utc'].replace('Z', '+00:00')).astimezone(timezone.utc).timestamp()
        # A reused PID starts after the old observation and cannot stand in for the original publisher.
        return 'running' if born <= observed else 'not_running'
    except (KeyError, ValueError, TypeError, OverflowError):
        return 'unknown'
    finally:
        kernel.CloseHandle(handle)
