"""Three-hour local-Codex exact-edit repair. Independent queue/gate authority is immutable."""
import datetime
from pathlib import Path
import re


WINDOW_SECONDS = 10800


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
    text = path.read_text(encoding='utf-8-sig')
    pattern = re.compile(r'\b(?:warning|error)\s+[A-Z]+\d+\b|\bshader\s+(?:warning|error)\b|\b[A-Za-z_][A-Za-z_0-9.]*Exception:|\b[A-Z][A-Z0-9_]*_FAIL(?:ED)?:')
    matches = [line for line in text.splitlines()
               if any(token in line for token in ('warning', 'error', 'Exception:', '_FAIL')) and pattern.search(line)]
    if not matches:
        raise q.QueueError('Native Unity.log has no supported complete failure diagnostics; the owner must supply diagnostics.json before paid repair.')
    result = dict(source='Unity.log', complete_matching_diagnostics=matches,
                  scope='Every matching native exception, assertion marker and compiler warning/error line; the full log remains in owner evidence.')
    return result


class RetryableNoProgress(Exception):
    """An unchanged outcome permits another bounded proposal within the original window."""


def no_progress(q, record, attempt, checkpoint, reason):
    """Persist consecutive failures before retrying; restarts cannot erase stalled attempts."""
    record['consecutive_no_progress'] = record.get('consecutive_no_progress', 0) + 1
    attempt['no_progress_reason'] = reason
    checkpoint()
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
    """Repair any relevant project artifact in a sandbox copy; independently review and validate it."""
    from GitFlowRepairWorkspace import prepare, changes, apply, protected_paths
    settings = policy.get('repair', {})
    if settings.get('enabled') is not True:
        raise q.QueueError('Automatic repair is disabled; candidate and failure are preserved.')
    if not all(settings.get(name) for name in ['executable', 'model', 'effort']):
        raise q.QueueError('Automatic repair needs a configured local Codex model and effort.')
    now = datetime.datetime.now(datetime.timezone.utc)
    record = value.setdefault('repair', dict(window_seconds=WINDOW_SECONDS, first_started_utc=now.isoformat(), attempts=[], seen=[]))
    budget = remaining(record, now)
    if budget <= 0:
        raise q.QueueError('The original three-hour repair window is exhausted.')
    if record['attempts'] and record['attempts'][-1]['state'] in {'requesting', 'reviewing', 'applying'}:
        raise q.QueueError('Interrupted repair has an uncertain outcome; reconcile retained artifacts before continuation.')
    if transport is None:
        from GitFlowModels import repair_project
        transport = repair_project
    folder = q.plain(evidence_root / ('repair_' + str(len(record['attempts']) + 1)))
    folder.mkdir(parents=True, exist_ok=False)
    lease.guard()
    workspace_record = prepare(q, workspace, evidence_root, folder)
    evidence = {}
    if getattr(failure, 'folder', None):
        for path in q.plain(failure.folder).iterdir():
            if path.is_file():
                # Models inspect complete files on demand rather than embedding/truncating them in prompts.
                evidence[path.name] = str(q.plain(path))
    context = dict(request=value['id'], source=value['source'], candidate=workspace_record['candidate']['head'],
                   failure=str(failure), evidence=evidence, original=workspace_record['original'],
                   proposed=workspace_record['proposed'], protected_paths=sorted(protected_paths(policy)),
                   history=[dict(state=a.get('state'), reason=a.get('no_progress_reason'), paths=a.get('paths', []))
                            for a in record['attempts']])
    q.durable(folder / 'context.json', context)
    attempt = dict(started_utc=now.isoformat(), state='requesting', mode='project_workspace',
                   before=q.digest(workspace_record['files']), model=settings['model'], effort=settings['effort'],
                   remaining_seconds=budget, workspace=str(folder / 'workspace.json'))
    record['attempts'].append(attempt)
    checkpoint()
    try:
        reply = transport(settings, context, Path(workspace_record['proposed']), folder,
                          remaining(record, datetime.datetime.now(datetime.timezone.utc)), review=False)
    except Exception:
        attempt['state'] = 'model_failed_candidate_unchanged'
        checkpoint()
        raise
    q.durable(folder / 'proposal.json', reply)
    attempt.update(proposal_sha256=q.digest(reply), model_invoked=reply.get('model_invoked') is True,
                   model=reply.get('model', settings['model']), effort=reply.get('effort', settings['effort']))
    lease.guard()
    if remaining(record, datetime.datetime.now(datetime.timezone.utc)) <= 0:
        attempt['state'] = 'expired'
        checkpoint()
        raise q.QueueError('Repair reply arrived after the original three-hour deadline; edits were not applied.')
    if reply.get('proposal', {}).get('status') in {'blocked', 'needs_input'}:
        attempt['state'] = 'needs_input'
        checkpoint()
        raise q.QueueError('Repair needs unavailable access or a project decision; inspect retained proposal.')
    changed = changes(q, workspace_record, policy)
    if not changed:
        attempt['state'] = 'no_progress'
        no_progress(q, record, attempt, checkpoint, 'Model made no project changes; complete artifacts and history retained.')
    q.durable(folder / 'changes.json', changed)
    attempt.update(state='reviewing', paths=sorted(changed))
    checkpoint()
    review_context = dict(context, changes=str(folder / 'changes.json'), repair_summary=reply.get('proposal', {}))
    try:
        review = transport(settings, review_context, Path(workspace_record['root']), folder,
                           remaining(record, datetime.datetime.now(datetime.timezone.utc)), review=True)
    except Exception:
        attempt['state'] = 'review_failed_candidate_unchanged'
        checkpoint()
        raise
    q.durable(folder / 'review.json', review)
    attempt['review_sha256'] = q.digest(review)
    lease.guard()
    if remaining(record, datetime.datetime.now(datetime.timezone.utc)) <= 0:
        attempt['state'] = 'expired'
        checkpoint()
        raise q.QueueError('Independent review exhausted the original repair budget; candidate retained.')
    if review.get('proposal', {}).get('approved') is not True:
        attempt['state'] = 'review_rejected'
        no_progress(q, record, attempt, checkpoint,
                    'Independent review rejected proposed repair: ' + review.get('proposal', {}).get('reason', 'See retained review.'))
    if changes(q, workspace_record, policy) != changed:
        raise q.QueueError('Repair artifacts changed during review; candidate retained.')
    attempt['state'] = 'applying'
    checkpoint()
    apply(q, workspace, workspace_record, changed)
    check_authored_whitespace(q, workspace)
    q.git(workspace, 'commit', '-m', 'Repair ' + value['id'] + ' with reviewed project artifacts')
    q.clean(workspace)
    attempt.update(state='applied_pending_validation', candidate=q.git(workspace, 'rev-parse', 'HEAD'),
                   completed_utc=q.utc(), after=q.digest(changed))
    record['consecutive_no_progress'] = 0
    checkpoint()
    return attempt['candidate']
