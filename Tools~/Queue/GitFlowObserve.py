"""Explicit bounded GitFlow observations. No ownership, PID probes or checkout writes."""
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
from urllib.parse import urlsplit

MAXIMUM_RECORDS = 64
MAXIMUM_OUTPUT = 768 * 1024
FIELDS = {
    'schema', 'id', 'state', 'status', 'title', 'source', 'base', 'size', 'version', 'candidate', 'main_commit',
    'submitted_utc', 'started_utc', 'finished_utc', 'completed_utc', 'updated_utc', 'policy', 'base_main',
    'dependencies', 'attempts', 'failure', 'receipt', 'checks', 'name', 'kind', 'folder', 'run', 'warnings', 'errors',
    'skipped', 'reused', 'pulls', 'publisher', 'original', 'repair', 'window_started_utc', 'consecutive_stalled',
    'attempt_count', 'owner', 'host', 'request', 'stage', 'process_id', 'pid', 'verified_main', 'evidence_candidate', 'lease_protocol', 'lease_seconds'
}


def projection(value, depth=0):
    """Omit executable/raw/credential fields and visibly bound nested evidence."""
    if depth > 7:
        return '[deeper evidence omitted]'
    if isinstance(value, dict):
        result = {key: projection(item, depth + 1) for key, item in value.items() if key in FIELDS and key != 'failure' and (key not in {'errors', 'warnings', 'skipped', 'pid', 'process_id'} or type(item) is int) and (key not in {'verified_main', 'evidence_candidate'} or isinstance(item, str) and re.fullmatch('[0-9a-f]{40}', item))}
        if value.get('failure'):
            phase = value.get('state') if value.get('state') in {'queued', 'preparing', 'validating', 'repairing', 'publishing', 'published', 'integrated', 'blocked', 'validation_failed'} else 'unknown'
            result['failure_phase'] = phase
            result['failure_summary'] = 'Required validation failed (reported).' if phase == 'validation_failed' else 'Synchronization remains pending (reported).' if phase == 'published' else 'Queue operation blocked or failed (reported).'
            result['next_action'] = 'Preserve this request ID and exact control commit. Ask its owner to review the retained attempt/check folder. Raw diagnostics withheld; do not resubmit or bypass checks.'
        return result
    if isinstance(value, list):
        return [projection(item, depth + 1) for item in value[-16:]] + (['[older entries omitted]'] if len(value) > 16 else [])
    if isinstance(value, str):
        value = re.sub(r'(?i)(?:https?|ssh)://[^\s]+', '[remote URL omitted]', value)
        value = re.sub(r'(?i)(?:bearer\s+\S+|(?:token|password|secret|api[_-]?key)\s*[:=]\s*\S+|(?:gh[pousr]_[A-Za-z0-9_]+|sk-[A-Za-z0-9_-]+))', '[credential-like text omitted]', value)
        value = ''.join(character for character in value if character in '\n\t' or ord(character) >= 32)
        return value[:512] + (' [text omitted]' if len(value) > 512 else '')
    return value if value is None or type(value) in (bool, int, float) else '[unsupported value]'


def object_json(content):
    """Reject duplicate keys and non-object records without guessing their schema meaning."""
    def unique(pairs):
        value = {}
        for key, item in pairs:
            if key in value:
                raise ValueError('Duplicate control field')
            value[key] = item
        return value
    value = json.loads(content, object_pairs_hook=unique)
    if not isinstance(value, dict):
        raise ValueError('Expected a control object')
    return value


def local_record(q, path):
    """Read one ordinary local metadata file without creating a directory or probing a lock."""
    with q.plain(path).open('rb') as source:
        content = source.read(q.MAXIMUM_BYTES + 1)
    if len(content) > q.MAXIMUM_BYTES:
        raise ValueError('Oversize local metadata')
    return object_json(content.decode('utf-8-sig'))


def observe(q, root, remote, offset=0, limit=16):
    """Return one ref-sorted page with exact coverage and independent local evidence."""
    if type(offset) is not int or offset < 0 or type(limit) is not int or not 1 <= limit <= MAXIMUM_RECORDS:
        raise q.QueueError('Inspection offset must be nonnegative and page size between 1 and 64.')
    if not re.fullmatch(r'[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}', remote):
        raise q.QueueError('Select an existing remote name.')
    root = q.plain(root)
    started = q.utc()
    deadline = __import__('time').monotonic() + 45
    # Ignore caller Git overrides and user/system configuration. No hooks, credential
    # helper, external transport or project code runs through this inspection route.
    env = {key: value for key, value in os.environ.items() if not key.upper().startswith('GIT_')}
    env.update(GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL=os.devnull, GIT_TERMINAL_PROMPT='0', GCM_INTERACTIVE='Never', GIT_OPTIONAL_LOCKS='0')

    def read(directory, *arguments):
        remaining = deadline - __import__('time').monotonic()
        if remaining <= 0:
            raise q.QueueError('Inspection deadline exceeded.')
        command = ['git', '--no-optional-locks', '-c', 'credential.helper=', '-c', 'credential.interactive=never',
                   '-c', 'core.hooksPath=', '-c', 'protocol.ext.allow=never', '-c', 'protocol.ssh.allow=never', '-c', 'gc.auto=0', *map(str, arguments)]
        result = q.process_run(command, cwd=directory, env=env, capture_output=True, timeout=min(remaining, 40), **q.console_options())
        if result.returncode:
            raise q.QueueError('Read failed: ' + str(arguments[0]) + ' (exit ' + str(result.returncode) + '). Check access using the repository owner workflow.')
        if len(result.stdout) > q.MAXIMUM_BYTES:
            raise q.QueueError('Inspection read exceeds one MiB; narrow the adapter scope.')
        return result.stdout.decode('utf-8-sig')

    common = q.plain(read(root, 'rev-parse', '--path-format=absolute', '--git-common-dir').strip())
    url = read(root, 'config', '--get', 'remote.' + remote + '.url').strip()
    address = urlsplit(url)
    if not (os.path.isabs(url) or address.scheme == 'https' and address.hostname and not address.username and not address.password and not address.query and not address.fragment):
        raise q.QueueError('Safe inspection supports public HTTPS without embedded credentials or absolute local remotes. Other transports are not observed.')
    result = dict(schema=1, state='observed', repository=str(root), remote=remote, started_utc=started,
                  requests=[], findings=[], observation='Dated read only; no live health or expiry proof. Refresh explicitly.',
                  coverage=dict(offset=offset, limit=limit, advertised=0, returned=0, failed=0, omitted=0, next_offset=None),
                  legacy=dict(state='not_observed', requests=0, locks=0, migration='No automatic migration or adoption. Preserve original records; use the existing owner route.'),
                  owner=dict(state='unknown', process_health='unknown'), setup=dict(state='not_checked'))
    config_path = common / 'gameforge_queue/configuration.json'
    try:
        if config_path.exists():
            config = local_record(q, config_path)
            if not isinstance(config, dict) or not isinstance(config.get('policy'), dict):
                raise ValueError('Unsupported configuration shape')
            if not all(isinstance(config.get(key), str) for key in ('workspace_root', 'runner', 'runner_hash', 'policy_hash')):
                raise ValueError('Unsupported configuration fields')
            policy = config['policy']
            if not isinstance(policy.get('repair', {}), dict) or not isinstance(policy.get('checks', []), list) or not all(isinstance(item, dict) for item in policy.get('checks', [])):
                raise ValueError('Unsupported policy shape')
            valid = type(config.get('schema')) is int and config.get('schema') == q.SCHEMA and config.get('policy_hash') == q.digest(config.get('policy'))
            runner_current = config.get('runner_hash') == q.runner_hash() and config.get('runner') == str(Path(q.__file__).resolve())
            if not valid:
                result['findings'].append('Recorded configuration schema/policy identity is invalid. Preserve its bytes for owner reconciliation.')
            if not runner_current:
                result['findings'].append('Installed runner differs from recorded configuration. Use the explicit stopped-owner adoption procedure; do not replace a running owner.')
            result['configuration_identity'] = q.digest(config)
            result['setup'] = dict(state='configured_recorded' if valid else 'invalid_configuration', runner_current=runner_current,
                                   workspace_root=projection(str(config.get('workspace_root', ''))), policy_hash=projection(config.get('policy_hash')),
                                   automatic_repair=policy.get('repair', {}).get('enabled') if type(policy.get('repair', {}).get('enabled')) is bool else None,
                                   checks=[dict(name=projection(str(item.get('name', 'unknown'))), kind=projection(str(item.get('kind', 'unknown'))), timeout_seconds=item.get('timeout_seconds') if type(item.get('timeout_seconds')) is int else None)
                                           for item in config.get('policy', {}).get('checks', [])[:16]],
                                   reason='Recorded configuration only. Workspace ownership, tools, foundation and service eligibility are not verified.')
        else:
            result['setup'] = dict(state='unconfigured', reason='Explicitly configure one new short publisher root and required policy. Configuration does not start a publisher.')
    except (OSError, ValueError, TypeError, q.QueueError):
        result['setup'] = dict(state='invalid_configuration', reason='Preserve the existing configuration; owner reconciliation is required.')
        result['findings'].append('Local configuration could not be read; no defaults substituted.')
    state_path = common / 'gameforge_queue/state.json'
    try:
        if state_path.exists():
            processor = local_record(q, state_path)
            if not isinstance(processor, dict):
                raise ValueError('Unsupported processor shape')
            result['recorded_processor'] = projection(processor)
            result['recorded_processor']['process_health'] = 'unknown'
    except (OSError, ValueError, TypeError, q.QueueError):
        result['findings'].append('Recorded processor evidence unavailable; no process probe performed.')
    try:
        with tempfile.TemporaryDirectory(prefix='gfo_') as temporary:
            q.plain(temporary)
            read(temporary, 'init', '--bare', '--template=')
            rows = read(temporary, 'ls-remote', '--refs', url, q.PREFIX + '*', q.LOCK, 'refs/heads/codex/queue/*', 'refs/heads/codex/integration-lock').splitlines()
            refs = {}
            for row in rows:
                parts = row.split()
                if len(parts) != 2 or not re.fullmatch('[0-9a-f]{40}', parts[0]) or parts[1] in refs:
                    raise q.QueueError('Malformed or duplicate advertised ref.')
                refs[parts[1]] = parts[0]
            legacy_requests = sum(name.startswith('refs/heads/codex/queue/') for name in refs)
            legacy_locks = int('refs/heads/codex/integration-lock' in refs)
            result['legacy'].update(requests=legacy_requests, locks=legacy_locks, state='unsupported_existing_queue' if legacy_requests or legacy_locks else 'none_advertised')
            if legacy_requests or legacy_locks:
                result['findings'].append('Legacy IntegrationQueue records exist. This adapter does not parse/adopt them; new setup/submission remains blocked by its existing guard.')
            requests = sorted((name, identity) for name, identity in refs.items() if name.startswith(q.PREFIX))
            page = requests[offset:offset + limit]
            result['coverage'].update(advertised=len(requests), omitted=len(requests) - len(page), next_offset=offset + limit if offset + limit < len(requests) else None)
            identities = list(dict.fromkeys([identity for _, identity in page] + ([refs[q.LOCK]] if q.LOCK in refs else [])))
            if identities:
                read(temporary, 'fetch', '--no-tags', '--no-write-fetch-head', '--depth=1', url, *identities)
            for name, identity in page:
                try:
                    if int(read(temporary, 'cat-file', '-s', identity + ':' + q.REQUEST).strip()) > q.MAXIMUM_BYTES:
                        raise ValueError('Oversize request')
                    value = object_json(read(temporary, 'show', identity + ':' + q.REQUEST))
                    if not isinstance(value, dict):
                        raise ValueError('Unsupported request shape')
                    if type(value.get('schema')) is not int or value['schema'] != 1 or not re.fullmatch('request_[0-9a-f]{40}', value.get('id', '')) or name != q.PREFIX + value['id'] or not re.fullmatch('[0-9a-f]{40}', value.get('source', '')) or value['id'] != 'request_' + value['source']:
                        raise q.QueueError('Unsupported request schema/identity.')
                    if value.get('state') not in {'queued', 'preparing', 'validating', 'repairing', 'publishing', 'published', 'integrated', 'blocked', 'validation_failed'}:
                        result['findings'].append('Unsupported request state at control ' + identity + '; meaning is unknown.')
                    item = projection(value)
                    item['control_commit'] = identity
                    item['evidence_scope'] = 'Reported fields only; commands/raw diagnostics omitted; lists limited to latest 16 entries. Validation, receipt artifacts and current-main ancestry not independently verified.'
                    if len(q.encoded(item).encode()) > 24576:
                        item = {key: item.get(key) for key in ('id', 'state', 'source', 'control_commit', 'version', 'main_commit')}
                        item['evidence_scope'] = 'Request details exceed display bound; retained original record was not changed.'
                        result['findings'].append('Oversize request details omitted: ' + value['id'])
                    if len(q.encoded(result).encode()) + len(q.encoded(item).encode()) > MAXIMUM_OUTPUT - 4096:
                        result['findings'].append('Aggregate display bound reached; reduce page size.')
                        break
                    result['requests'].append(item)
                except (OSError, ValueError, TypeError, q.QueueError, subprocess.TimeoutExpired):
                    result['coverage']['failed'] += 1
                    result['findings'].append('Request unreadable or unsupported at control ' + identity + '; original record preserved.')
            if q.LOCK not in refs:
                result['owner'] = dict(state='absent', process_health='unknown', reason='No modern shared lease was advertised. This does not authorize acquisition or prove another processor stopped.')
            if q.LOCK in refs:
                try:
                    if int(read(temporary, 'cat-file', '-s', refs[q.LOCK] + ':' + q.REQUEST).strip()) > q.MAXIMUM_BYTES:
                        raise ValueError('Oversize lease')
                    lease = object_json(read(temporary, 'show', refs[q.LOCK] + ':' + q.REQUEST))
                    if not isinstance(lease, dict):
                        raise ValueError('Unsupported lease shape')
                    q.leases.validate(lease)
                    result['owner'] = dict(projection(lease), state='released' if lease['state'] == 'released' else 'recorded_owned',
                                           control_commit=refs[q.LOCK], process_health='unknown', reason='Single exact-object observation; no expiry, current ownership or takeover decision.')
                except (OSError, ValueError, TypeError, q.QueueError, q.leases.LeaseError, subprocess.TimeoutExpired):
                    result['findings'].append('Lease metadata unavailable or unsupported; ownership remains unknown.')
    except (OSError, ValueError, TypeError, q.QueueError, subprocess.TimeoutExpired):
        result['findings'].append('Remote inspection incomplete. Check selected remote access and refresh explicitly. Prior observations are not current.')
        result['state'] = 'unavailable'
    coverage = result['coverage']
    coverage['returned'] = len(result['requests'])
    consumed = coverage['returned'] + coverage['failed']
    coverage['next_offset'] = offset + consumed if consumed and offset + consumed < coverage['advertised'] else None
    coverage['omitted'] = max(0, coverage['advertised'] - coverage['returned'] - coverage['failed'])
    if result['state'] != 'unavailable' and (result['findings'] or coverage['omitted'] or coverage['failed']):
        result['state'] = 'partial'
    result['completed_utc'] = q.utc()
    result['coverage']['ordering'] = 'Ref name, not execution order. Separate pages are separate observations; changing queues can move page membership.'
    if len(q.encoded(result).encode()) > MAXIMUM_OUTPUT:
        result['requests'] = []
        result['recorded_processor'] = {'state': 'omitted_display_bound', 'process_health': 'unknown'}
        result['setup'] = {'state': 'omitted_display_bound'}
        result['owner'] = {'state': 'unknown', 'process_health': 'unknown'}
        result['findings'] = ['Aggregate display bound exceeded. Original evidence preserved; reduce page size or reconcile malformed metadata.']
        result['state'] = 'partial'
        coverage.update(returned=0, omitted=coverage['advertised'], next_offset=None)
    return result
