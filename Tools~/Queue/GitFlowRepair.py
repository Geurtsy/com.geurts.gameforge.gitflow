"""Three-hour local-Codex exact-edit repair. Independent queue/gate authority is immutable."""
import datetime
from pathlib import Path
import re

WINDOW_SECONDS = 10800


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
    if record['attempts'] and record['attempts'][-1]['state'] in {'requesting', 'applying'}:
        raise q.QueueError('Interrupted repair has an uncertain outcome. Preserve its exact proposal/candidate and reconcile before continuation.')
    failure_content = str(failure)
    if getattr(failure, 'folder', None):
        folder = q.plain(failure.folder)
        diagnostics_path = folder / 'diagnostics.json'
        if diagnostics_path.is_file():
            diagnostics = q.read_json(diagnostics_path)
            failure_content = q.encoded(diagnostics.get('complete_matching_diagnostics', diagnostics))
        elif (folder / 'output.log').is_file():
            failure_content = (folder / 'output.log').read_text(encoding='utf-8-sig', errors='replace')
    failure_identity = q.digest(failure_content.replace(str(workspace), '{workspace}').encode())
    if (record['attempts'] and record['attempts'][-1]['state'] == 'applied_pending_validation'
            and record['attempts'][-1].get('failure_evidence') == failure_identity):
        record['attempts'][-1]['state'] = 'no_progress_after_validation'
        checkpoint()
        raise q.QueueError('No meaningful progress: independently repeated validation produced the same failure after the source repair.')
    protected = set(policy.get('protected_paths', [])) | {policy['version_file']} | set(policy.get('native_version_outputs', []))
    for check in policy['checks']:
        protected.update(arg for arg in check['command'] if not arg.startswith(('-', '{')))
    changed = q.git(workspace, 'diff', '--name-only', value['base'], value['source']).splitlines()
    conflicts = set(q.git(workspace, 'diff', '--name-only', '--diff-filter=U').splitlines())
    files = {}
    for relative in changed:
        path = Path(relative)
        parts = {part.casefold() for part in path.parts}
        if (relative in protected or any(relative.startswith(item.rstrip('/') + '/') for item in protected)
                or parts & {'tools', 'tools~', 'tests', 'tests~', 'test', 'testing', 'editor', 'projectsettings', 'packages', '.github', '.codex', '_thirdparty', 'plugins', '_generated'}
                or path.suffix.casefold() not in {'.cs', '.py'}):
            continue
        target = q.plain(workspace / path)
        if not target.is_file():
            continue
        raw = target.read_bytes()
        if len(raw) > 32768 or b'\0' in raw:
            raise q.QueueError('Repair context cannot include the entire required file within 32768 bytes: ' + relative)
        files[relative] = dict(sha256=q.digest(raw), text=raw.decode('utf-8-sig'), bom=raw.startswith(b'\xef\xbb\xbf'))
    if not files or conflicts - set(files):
        raise q.QueueError('No complete supported repair scope, or conflicts include protected/native files: ' + ', '.join(sorted(conflicts - set(files))))
    before = fingerprint(files)
    if before in record['seen']:
        raise q.QueueError('No meaningful progress: this source candidate was already attempted in the original repair window.')
    diagnostics = {}
    if getattr(failure, 'folder', None):
        folder = q.plain(failure.folder)
        selected = folder / 'diagnostics.json'
        if not selected.exists():
            selected = folder / 'output.log'
        if selected.is_file():
            if selected.stat().st_size > 16384:
                raise q.QueueError('Complete failure diagnostics exceed the bounded repair context; retain them for operator review.')
            diagnostics[selected.name] = selected.read_text(encoding='utf-8-sig')
    context = q.encoded(dict(request=value['id'], source=value['source'], candidate=q.git(workspace, 'rev-parse', 'HEAD'),
                            failure=str(failure), diagnostics=diagnostics, allowed_files=files,
                            rule='Complete file text is provided. Change only a unique necessary fragment; no whole-file replacement, test edits or policy edits.'))
    if len(context.encode()) > 65536:
        raise q.QueueError('Complete repair context exceeds 65536 bytes; no partial conflict context was sent.')
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
        checkpoint()
        raise q.QueueError('No meaningful progress: model supplied no bounded exact edits.')
    replacements = {}
    for edit in edits:
        if set(edit) != {'path', 'expected_sha256', 'old_text', 'new_text'} or not all(isinstance(item, str) for item in edit.values()):
            raise q.QueueError('Malformed edit proposal; source unchanged.')
        relative = edit['path']
        if relative not in files or relative in replacements or edit['expected_sha256'] != files[relative]['sha256']:
            raise q.QueueError('Edit targets a protected, repeated or stale source; source unchanged.')
        original, old, new = files[relative]['text'], edit['old_text'], edit['new_text']
        if not old or old == original or original.count(old) != 1 or old == new:
            raise q.QueueError('Edit must replace one exact necessary fragment; source unchanged.')
        updated = original.replace(old, new, 1)
        if any(marker in updated for marker in ['<<<<<<<', '=======', '>>>>>>>', '|||||||']):
            raise q.QueueError('Proposed source retains conflict markers; source unchanged.')
        replacements[relative] = dict(files[relative], text=updated)
    after_files = dict(files, **replacements)
    after = fingerprint(after_files)
    if after == before or after in record['seen']:
        attempt['state'] = 'no_progress'
        checkpoint()
        raise q.QueueError('No meaningful progress: whitespace-only or repeated source proposal; source unchanged.')
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
    q.git(workspace, 'diff', '--cached', '--check')
    q.git(workspace, 'commit', '-m', 'Repair ' + value['id'] + ' with source-bound proposal')
    q.clean(workspace)
    attempt.update(state='applied_pending_validation', candidate=q.git(workspace, 'rev-parse', 'HEAD'), completed_utc=q.utc())
    checkpoint()
    return attempt['candidate']
