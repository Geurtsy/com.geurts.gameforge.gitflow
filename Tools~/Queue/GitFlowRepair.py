"""Three-hour local-Codex exact-edit repair. Independent queue/gate authority is immutable."""
import datetime
from pathlib import Path
import re

from GitFlowModels import MAXIMUM_CONTEXT_BYTES

WINDOW_SECONDS = 10800
NO_PROGRESS_LIMIT = 3
MAXIMUM_FILE_BYTES = 256 * 1024
MAXIMUM_DIAGNOSTIC_BYTES = 64 * 1024
MAXIMUM_NATIVE_LOG_BYTES = 8 * 1024 * 1024


def conflict_paths(settings):
    """Validate exact, explicitly authorized first-party conflict exceptions; never globs or control paths."""
    paths = settings.get('conflict_paths', [])
    if not isinstance(paths, list) or len(paths) > 16:
        raise ValueError('Repair conflict_paths must be an explicit list of at most sixteen files.')
    seen = set()
    forbidden = {'tools', 'tools~', 'projectsettings', 'packages', '.git', '.github', '.codex', '_thirdparty', 'plugins', '_generated'}
    for name in paths:
        if (not isinstance(name, str) or not name or len(name) > 259 or name != name.strip()
                or any(character in name for character in '\\:*?[]\x00') or name.startswith('/')
                or any(part in {'', '.', '..'} or part.endswith((' ', '.')) for part in name.split('/'))
                or set(part.casefold() for part in name.split('/')) & forbidden
                or (name != '.gitattributes' and Path(name).suffix.casefold() not in {'.cs', '.py'})
                or name.casefold() in seen):
            raise ValueError('Repair conflict scope requires unique safe first-party .cs/.py files or root .gitattributes: ' + repr(name))
        seen.add(name.casefold())
    return paths


def guard_conflict_resolution(q, relative, original, updated):
    """Permit edits only inside standard merge blocks, retaining attribute rules and assertion coverage."""
    pattern = re.compile(r'^<<<<<<<[^\r\n]*\r?\n(.*?)^=======\r?\n(.*?)^>>>>>>>[^\r\n]*(?:\r?\n|$)', re.M | re.S)
    blocks = list(pattern.finditer(original))
    if not blocks or '|||||||' in original:
        raise q.QueueError('Conflict-only scope needs complete standard two-way merge blocks: ' + relative)
    pieces, cursor = ['\\A'], 0
    for index, block in enumerate(blocks):
        pieces.extend([re.escape(original[cursor:block.start()]), '(?P<resolution' + str(index) + '>.*?)'])
        cursor = block.end()
    pieces.extend([re.escape(original[cursor:]), '\\Z'])
    match = re.fullmatch(''.join(pieces), updated, re.S)
    if match is None:
        raise q.QueueError('Conflict-only proposal changed text outside the authorized merge blocks: ' + relative)
    assertion = re.compile(r'\b(?:Require|Assert(?:\.\w+)?|CollectionAssert\.\w+|StringAssert\.\w+|LogAssert\.\w+)\s*\(')
    for index, block in enumerate(blocks):
        resolution = match.group('resolution' + str(index))
        if not resolution.strip():
            raise q.QueueError('Conflict-only repair cannot discard a merge block: ' + relative)
        if relative == '.gitattributes':
            required = {line for side in block.groups() for line in side.splitlines() if line.strip()}
            if not required <= set(resolution.splitlines()):
                raise q.QueueError('Attribute conflict repair must retain the complete rules from both sides.')
        elif (len(assertion.findall(resolution)) < max(len(assertion.findall(side)) for side in block.groups())
                or re.search(r'\bAssert\.(?:Ignore|Inconclusive)\s*\(|\[\s*(?:Ignore|Explicit)\b', resolution)):
            raise q.QueueError('Conflict-only test repair removed assertions or introduced a skipped check: ' + relative)


def check_authored_whitespace(q, workspace):
    """Check all staged authored text without treating unchanged native Unity serialization as source formatting."""
    exclusions = [':(exclude,glob)**/*' + suffix for suffix in ('.asset', '.meta', '.prefab', '.unity')]
    q.git(workspace, 'diff', '--cached', '--check', '--', '.', *exclusions)


def native_diagnostics(q, folder):
    """Collect every native exception/assertion/compiler diagnostic line, never an arbitrary log tail."""
    path = q.plain(folder / 'Unity.log')
    if not path.is_file():
        return None
    size = path.stat().st_size
    if size > MAXIMUM_NATIVE_LOG_BYTES:
        raise q.QueueError(f'Native Unity.log requires {size} bytes to inspect; limit {MAXIMUM_NATIVE_LOG_BYTES}. The owner must supply diagnostics.json before repair.')
    text = path.read_text(encoding='utf-8-sig')
    pattern = re.compile(r'^.*(?:\b(?:warning|error)\s+[A-Z]+\d+\b|(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*Exception:|[A-Z][A-Z0-9_]*_FAIL(?:ED)?:).*$', re.M)
    matches = [match.group(0).rstrip('\r') for match in pattern.finditer(text)]
    if not matches:
        raise q.QueueError('Native Unity.log has no supported complete failure diagnostics; the owner must supply diagnostics.json before paid repair.')
    result = dict(source='Unity.log', complete_matching_diagnostics=matches,
                  scope='Every matching native exception, assertion marker and compiler warning/error line; the full log remains in owner evidence.')
    required = len(q.encoded(result).encode('utf-8'))
    if required > MAXIMUM_DIAGNOSTIC_BYTES:
        raise q.QueueError(f'Complete native failure diagnostics require {required} bytes; limit {MAXIMUM_DIAGNOSTIC_BYTES}. No diagnostic list was truncated.')
    return result


class RetryableNoProgress(Exception):
    """An unchanged outcome permits another bounded proposal within the original window."""


def no_progress(q, record, attempt, checkpoint, reason):
    """Persist consecutive failures before retrying; restarts cannot erase stalled attempts."""
    record['consecutive_no_progress'] = record.get('consecutive_no_progress', 0) + 1
    attempt['no_progress_reason'] = reason
    checkpoint()
    if record['consecutive_no_progress'] >= NO_PROGRESS_LIMIT:
        raise q.QueueError('No meaningful progress after three consecutive repair attempts: ' + reason)
    raise RetryableNoProgress(reason)


def remaining(record, now):
    """Restarts, new candidates and model changes never reset the original durable clock."""
    if record.get('window_seconds') != WINDOW_SECONDS:
        raise ValueError('Unsupported repair window; preserve original attempt history.')
    started = datetime.datetime.fromisoformat(record['first_started_utc'])
    if started.tzinfo is None:
        raise ValueError('Repair start must have a UTC timezone.')
    elapsed = (now - started).total_seconds()
    if elapsed < 0:
        raise ValueError('Clock precedes the original repair start; no additional budget is granted.')
    return max(0, WINDOW_SECONDS - elapsed)


def fingerprint(files):
    """Ignore blank lines and trailing whitespace; keep interior/literal text in the progress identity."""
    import hashlib
    import json
    normalized = {path: '\n'.join(line.rstrip() for line in value['text'].splitlines() if line.strip())
                  for path, value in files.items()}
    return hashlib.sha256(json.dumps(normalized, sort_keys=True).encode()).hexdigest()


def repair(q, workspace, value, policy, failure, evidence_root, checkpoint, lease, transport=None):
    """Try again after an unproductive proposal; stop at the durable deadline or repeated stalled attempts."""
    while True:
        try:
            return repair_attempt(q, workspace, value, policy, failure, evidence_root, checkpoint, lease, transport)
        except RetryableNoProgress:
            lease.guard()


def repair_attempt(q, workspace, value, policy, failure, evidence_root, checkpoint, lease, transport=None):
    """Persist intent/proposal before applying guarded source edits; never relax validation or publish main."""
    settings = policy.get('repair', {})
    if settings.get('enabled') is not True:
        raise q.QueueError('Automatic repair is disabled; candidate and failure are preserved.')
    if not all(settings.get(name) for name in ['executable', 'model', 'effort']):
        raise q.QueueError('Automatic repair needs an explicitly selected local Codex model and effort.')
    now = datetime.datetime.now(datetime.timezone.utc)
    record = value.setdefault('repair', dict(window_seconds=WINDOW_SECONDS, first_started_utc=now.isoformat(), attempts=[], seen=[]))
    budget = remaining(record, now)
    if budget <= 0:
        raise q.QueueError('The original three-hour repair window is exhausted.')
    if record.get('consecutive_no_progress', 0) >= NO_PROGRESS_LIMIT:
        raise q.QueueError('No meaningful progress after three consecutive repair attempts; preserve the recorded attempts for review.')
    if record['attempts'] and record['attempts'][-1]['state'] in {'requesting', 'applying'}:
        raise q.QueueError('Interrupted repair has an uncertain outcome. Preserve its exact proposal/candidate and reconcile before continuation.')
    failure_content = str(failure)
    native = None
    if getattr(failure, 'folder', None):
        folder = q.plain(failure.folder)
        diagnostics_path = folder / 'diagnostics.json'
        if diagnostics_path.is_file():
            diagnostics = q.read_json(diagnostics_path)
            failure_content = q.encoded(diagnostics.get('complete_matching_diagnostics', diagnostics))
        elif (folder / 'output.log').is_file():
            failure_content = (folder / 'output.log').read_text(encoding='utf-8-sig', errors='replace')
        if not diagnostics_path.is_file():
            native = native_diagnostics(q, folder)
            if native:
                failure_content = q.encoded(native['complete_matching_diagnostics'])
    failure_identity = q.digest(failure_content.replace(str(workspace), '{workspace}').encode())
    if record['attempts'] and record['attempts'][-1]['state'] == 'applied_pending_validation':
        previous = record['attempts'][-1]
        if previous.get('failure_evidence') == failure_identity:
            previous['state'] = 'no_progress_after_validation'
            no_progress(q, record, previous, checkpoint, 'Independent validation produced the same failure after the source repair.')
        else:
            previous['state'] = 'changed_failure_after_validation'
            record['consecutive_no_progress'] = 0
            checkpoint()
    protected = set(policy.get('protected_paths', [])) | {policy['version_file']} | set(policy.get('native_version_outputs', []))
    for check in policy['checks']:
        protected.update(arg for arg in check['command'] if not arg.startswith(('-', '{')))
    changed = q.git(workspace, 'diff', '--name-only', value['base'], value['source']).splitlines()
    conflicts = set(q.git(workspace, 'diff', '--name-only', '--diff-filter=U').splitlines())
    files = {}
    try:
        authorized_conflicts = set(conflict_paths(settings))
    except ValueError as error:
        raise q.QueueError(str(error)) from error
    for relative in changed:
        path = Path(relative)
        parts = {part.casefold() for part in path.parts}
        conflict_only = relative in authorized_conflicts and relative in conflicts
        if (relative in protected or any(relative.startswith(item.rstrip('/') + '/') for item in protected)
                or parts & {'tools', 'tools~', 'projectsettings', 'packages', '.git', '.github', '.codex', '_thirdparty', 'plugins', '_generated'}
                or (not conflict_only and (parts & {'tests', 'tests~', 'test', 'testing', 'editor'}
                    or path.suffix.casefold() not in {'.cs', '.py'}))):
            continue
        target = q.plain(workspace / path)
        if not target.is_file():
            continue
        raw = target.read_bytes()
        if len(raw) > MAXIMUM_FILE_BYTES:
            raise q.QueueError(f'Complete repair file {relative} requires {len(raw)} bytes; limit {MAXIMUM_FILE_BYTES} bytes. No partial file was sent.')
        if b'\0' in raw:
            raise q.QueueError('Repair context requires a supported text file: ' + relative)
        files[relative] = dict(sha256=q.digest(raw), text=raw.decode('utf-8-sig'), bom=raw.startswith(b'\xef\xbb\xbf'))
        if conflict_only:
            files[relative]['conflict_only'] = True
    if not files or conflicts - set(files):
        raise q.QueueError('No complete supported repair scope, or conflicts include protected/native files: ' + ', '.join(sorted(conflicts - set(files))))
    before = fingerprint(files)
    diagnostics = {}
    if native:
        diagnostics['Unity.log diagnostics'] = q.encoded(native)
    if getattr(failure, 'folder', None):
        folder = q.plain(failure.folder)
        selected = folder / 'diagnostics.json'
        if not selected.exists():
            selected = folder / 'output.log'
        if selected.is_file():
            diagnostic_bytes = selected.stat().st_size
            if diagnostic_bytes > MAXIMUM_DIAGNOSTIC_BYTES:
                raise q.QueueError(f'Complete failure diagnostics {selected.name} require {diagnostic_bytes} bytes; limit {MAXIMUM_DIAGNOSTIC_BYTES} bytes. Retain them for operator review.')
            diagnostics[selected.name] = selected.read_text(encoding='utf-8-sig')
    context = q.encoded(dict(request=value['id'], source=value['source'], candidate=q.git(workspace, 'rev-parse', 'HEAD'),
                            failure=str(failure), diagnostics=diagnostics, allowed_files=files,
                            recent_attempts=[{key: attempt.get(key) for key in ('state', 'failure_evidence', 'no_progress_reason', 'paths')}
                                             for attempt in record['attempts'][-NO_PROGRESS_LIMIT:]],
                            rule='Complete file text is provided. Learn from unsuccessful attempts. Change only a unique necessary fragment; no publication/lease/validation-policy/controller changes. Ordinary files forbid whole-file replacement and test edits. Files marked conflict_only have explicit user permission solely to resolve their existing merge blocks: preserve every byte outside those blocks, all test coverage and existing behavior, without skips or weakened assertions. Retain all .gitattributes rules from both sides. Combine the current and incoming feature behavior, retaining current input and interaction policies when incoming source predates them.'))
    context_bytes = len(context.encode('utf-8'))
    if context_bytes > MAXIMUM_CONTEXT_BYTES:
        raise q.QueueError(f'Complete repair context requires {context_bytes} bytes; limit {MAXIMUM_CONTEXT_BYTES} bytes. No partial conflict context was sent.')
    if before not in record['seen']:
        record['seen'].append(before)
    attempt = dict(started_utc=now.isoformat(), before=before, failure=q.digest(str(failure).encode()),
                   failure_evidence=failure_identity, model=settings['model'], effort=settings['effort'], state='requesting', remaining_seconds=budget)
    record['attempts'].append(attempt)
    checkpoint()  # A crash cannot silently erase the paid attempt or restart its clock.
    lease.guard()
    if transport is None:
        from GitFlowModels import diagnose
        transport = diagnose
    reply = transport(settings['executable'], settings['model'], settings['effort'], context,
                      propose_edits=True, maximum_seconds=budget)
    folder = q.plain(evidence_root / ('repair_' + str(len(record['attempts']))))
    folder.mkdir(parents=True, exist_ok=False)
    q.durable(folder / 'proposal.json', reply)
    attempt['proposal_sha256'] = q.digest(reply)
    attempt['model_invoked'] = reply.get('model_invoked') is True
    lease.guard()
    if remaining(record, datetime.datetime.now(datetime.timezone.utc)) <= 0:
        attempt['state'] = 'expired'
        checkpoint()
        raise q.QueueError('Repair reply arrived after the original three-hour deadline; edits were not applied.')
    edits = reply.get('proposal', {}).get('edits')
    if not isinstance(edits, list) or not 1 <= len(edits) <= 16:
        attempt['state'] = 'no_progress'
        no_progress(q, record, attempt, checkpoint, 'Model supplied no bounded exact edits.')
    replacements = {}
    for edit in edits:
        if set(edit) != {'path', 'expected_sha256', 'old_text', 'new_text'} or not all(isinstance(item, str) for item in edit.values()):
            raise q.QueueError('Malformed edit proposal; source unchanged.')
        relative = edit['path']
        if relative not in files or relative in replacements or edit['expected_sha256'] != files[relative]['sha256']:
            raise q.QueueError('Edit targets a protected, repeated or stale source; source unchanged.')
        original, old, new = files[relative]['text'], edit['old_text'], edit['new_text']
        if not old or (old == original and not files[relative].get('conflict_only')) or original.count(old) != 1 or old == new:
            raise q.QueueError('Edit must replace one exact necessary fragment; source unchanged.')
        updated = original.replace(old, new, 1)
        if any(marker in updated for marker in ['<<<<<<<', '=======', '>>>>>>>', '|||||||']):
            raise q.QueueError('Proposed source retains conflict markers; source unchanged.')
        if files[relative].get('conflict_only'):
            guard_conflict_resolution(q, relative, original, updated)
        replacements[relative] = dict(files[relative], text=updated)
    after_files = dict(files, **replacements)
    after = fingerprint(after_files)
    if after == before or after in record['seen']:
        attempt['state'] = 'no_progress'
        no_progress(q, record, attempt, checkpoint, 'Whitespace-only or repeated source proposal; source unchanged.')
    for relative in replacements:
        if q.digest(q.plain(workspace / relative).read_bytes()) != files[relative]['sha256']:
            raise q.QueueError('Candidate changed during model inference; source unchanged.')
    attempt.update(state='applying', after=after, paths=list(replacements))
    checkpoint()
    for relative, replacement in replacements.items():
        payload = replacement['text'].encode('utf-8')
        if replacement['bom']:
            payload = b'\xef\xbb\xbf' + payload
        q.plain(workspace / relative).write_bytes(payload)
    q.git(workspace, 'add', '--', *replacements)
    check_authored_whitespace(q, workspace)
    q.git(workspace, 'commit', '-m', 'Repair ' + value['id'] + ' with source-bound proposal')
    q.clean(workspace)
    attempt.update(state='applied_pending_validation', candidate=q.git(workspace, 'rev-parse', 'HEAD'), completed_utc=q.utc())
    checkpoint()
    return attempt['candidate']
