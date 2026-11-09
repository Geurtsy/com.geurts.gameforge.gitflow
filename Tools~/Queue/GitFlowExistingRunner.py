"""Explicit adapter to an existing committed IntegrationQueue owner; no new queue or publisher."""
from __future__ import annotations
import argparse
import base64
import time
import uuid
import ast
import ctypes
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from datetime import datetime,timezone

FORMAT=1

def checked(value):
    path=Path(os.path.abspath(value))
    if len(str(path))>=260:raise ValueError('Choose a short path; original scope preserved')
    for member in (path,*path.parents):
        if member.is_symlink() or getattr(member,'is_junction',lambda:False)():raise ValueError('Linked scope preserved')
    return path

def digest(raw):return hashlib.sha256(raw).hexdigest()

def run(arguments,root,timeout=45):
    environment=dict(os.environ,GIT_TERMINAL_PROMPT='0',GCM_INTERACTIVE='Never',GIT_OPTIONAL_LOCKS='0',PYTHONDONTWRITEBYTECODE='1')
    value=subprocess.run(arguments,cwd=root,capture_output=True,text=True,encoding='utf-8',errors='strict',timeout=timeout,env=environment,creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
    if value.returncode:raise ValueError('Existing owner command failed; retain its actual local evidence. Exit '+str(value.returncode))
    if len(value.stdout.encode('utf-8'))>1024*1024:raise ValueError('Existing owner response exceeds one MiB')
    return value.stdout

def git(root,*arguments):return run(['git','-C',str(root),*arguments],root).strip()

def common(root):return checked(git(root,'rev-parse','--path-format=absolute','--git-common-dir'))

def snapshot(project):
    root=checked(project);owner_common=common(root);control=owner_common/'integration_processor';configuration=checked(control/'config.json')
    if not configuration.is_file():raise ValueError('Existing IntegrationQueue is not configured; no setup is created')
    if (owner_common/'gameforge_queue/configuration.json').exists():raise ValueError('A modern GitFlow reservation also exists; reconcile ownership before adopting')
    raw=configuration.read_bytes();settings=json.loads(raw.decode('utf-8-sig'))
    if not isinstance(settings,dict) or checked(settings['repo'])!=root:raise ValueError('Existing configuration target differs; preserved')
    runner=checked(settings['runner']);workspace=checked(settings['integration_root'])
    if runner.name!='integration_queue.py' or not runner.is_file() or not workspace.is_dir():raise ValueError('Select the real existing supported runner and workspace')
    if common(runner.parent)!=owner_common or common(workspace)!=owner_common:raise ValueError('Runner and existing workspace must belong to the same owning Git common repository')
    cohort={}
    selector=':(icase,glob)**/*.py'
    if git(runner.parent,'status','--porcelain','--untracked-files=all','--',selector):raise ValueError('Runner cohort contains changed/untracked Python source; review and commit it first')
    tracked=set(git(runner.parent,'ls-files','--cached','-z','--',selector).split('\0'))
    for source in sorted(runner.parent.rglob('*.py')):
        checked(source);relative=source.relative_to(runner.parent).as_posix()
        if relative not in tracked:raise ValueError('Runner cohort contains changed/untracked Python source; review and commit it first')
        cohort[relative]=digest(source.read_bytes())
    if not cohort:raise ValueError('Runner source cohort is empty')
    functions={node.name for node in ast.parse(runner.read_text(encoding='utf-8-sig')).body if isinstance(node,(ast.FunctionDef,ast.AsyncFunctionDef))}
    if not {'processor_status','start_processor','stop_processor','processor_running'}<=functions:raise ValueError('Unknown existing owner API; no speculative recovery command')
    identity=dict(formatVersion=FORMAT,repository=str(root),commonDirectory=str(owner_common),configurationPath=str(configuration),configurationSha256=digest(raw),runner=str(runner),runnerCommit=git(runner.parent,'rev-parse','HEAD'),runnerHashes=cohort,integrationRoot=str(workspace),protocol='existing-codex-integration-queue')
    identity['scopeFingerprint']=digest(json.dumps({key:value for key,value in identity.items() if key!='runnerCommit'},sort_keys=True,separators=(',',':')).encode())
    return identity

def owner_status(identity,python):
    result=json.loads(run([str(python),'-X','utf8',identity['runner'],'--repo',identity['repository'],'processor-status'],identity['repository']))
    if not isinstance(result,dict) or not isinstance(result.get('running'),bool):raise ValueError('Existing owner returned unsupported liveness evidence')
    # No raw failures, commands, credential-bearing config or request bodies are displayed.
    return {name:result[name] for name in ('running','status','pid','phase','active_id','observed_utc') if name in result}

def inspect(project,python):
    identity=snapshot(project)
    return dict(identity=identity,owner=owner_status(identity,checked(python)),readyForAdoption=False,detail='Read-only observation. Adoption requires explicit reviewed exact fingerprint and a stopped owner. This is not a migration or new queue.')

def adopt(project,python,destination,expected_fingerprint):
    identity=snapshot(project);target=checked(destination)
    if identity['scopeFingerprint']!=expected_fingerprint:raise ValueError('Reviewed scope changed; original queue/configuration preserved')
    if target.exists():raise ValueError('Fresh private adoption record required; existing bytes preserved')
    if target==Path(identity['configurationPath']) or target.is_relative_to(Path(identity['integrationRoot'])) or target.is_relative_to(common(checked(project))):raise ValueError('Keep adapter record outside existing control/workspace ownership')
    status=owner_status(identity,checked(python))
    if status['running']:raise ValueError('Existing publisher is active; wait for supported stopped-owner handoff, never interrupt Unity')
    if (Path(identity['integrationRoot'])/'Temp/UnityLockfile').exists():raise ValueError('Native Unity ownership is uncertain; preserve its lock and wait for safe handoff')
    record=dict(identity=identity,python=str(checked(python)),pythonSha256=digest(checked(python).read_bytes()),adoptedUtc=datetime.now(timezone.utc).isoformat(),mode='existing-owner-adapter',createsQueue=False,changesPolicy=False)
    record['recordSha256']=digest(json.dumps(record,sort_keys=True,separators=(',',':')).encode())
    # The original owner remains authoritative. No lease/ref/stop/configuration is edited.
    target.parent.mkdir(parents=True,exist_ok=True)
    with target.open('x',encoding='utf-8') as stream:json.dump(record,stream,indent=2)
    return dict(state='adopted_existing_owner',record=str(target),scopeFingerprint=identity['scopeFingerprint'])

def enclosing_kill_job():
    """Publisher start must not be hidden inside an Editor action's kill-on-close job."""
    if os.name!='nt':return False
    from ctypes import wintypes
    kernel=ctypes.WinDLL('kernel32',use_last_error=True)
    kernel.GetCurrentProcess.restype=wintypes.HANDLE
    kernel.IsProcessInJob.argtypes=[wintypes.HANDLE,wintypes.HANDLE,ctypes.POINTER(wintypes.BOOL)]
    kernel.IsProcessInJob.restype=wintypes.BOOL
    present=wintypes.BOOL()
    if not kernel.IsProcessInJob(kernel.GetCurrentProcess(),None,ctypes.byref(present)):raise OSError('Cannot establish native host job context')
    # Conservative: independent persistent hosting must be explicitly established outside a bounded action job.
    return bool(present.value)

def dispatch(record_path,action,operator=False):
    path=checked(record_path);raw=path.read_bytes();record=json.loads(raw.decode('utf-8-sig'))
    if record.get('mode')!='existing-owner-adapter':raise ValueError('Unsupported adoption record; preserved')
    checksum=digest(json.dumps({key:value for key,value in record.items() if key!='recordSha256'},sort_keys=True,separators=(',',':')).encode())
    if record.get('recordSha256')!=checksum:raise ValueError('Modified adoption record preserved; review and explicitly re-adopt to a fresh record')
    identity=record['identity'];current=snapshot(identity['repository'])
    if current['scopeFingerprint']!=identity.get('scopeFingerprint'):raise ValueError('Configured runner/policy/root/source changed; explicit stopped-owner reviewed re-adoption required')
    python=checked(record['python'])
    if digest(python.read_bytes())!=record['pythonSha256']:raise ValueError('Selected interpreter changed; explicitly review/re-adopt')
    before=owner_status(current,python)
    if action=='status':return dict(state='observed_existing_owner',owner=before,scopeFingerprint=identity['scopeFingerprint'])
    if not operator:raise ValueError('Explicit operator action required; no queue command dispatched')
    if action not in ('start','resume','request-stop'):raise ValueError('Unsupported existing-owner action')
    if action in ('start','resume') and before['running']:return dict(state='already_running',owner=before)
    if action in ('start','resume') and enclosing_kill_job():raise ValueError('Independent host required. This bounded Windows job could terminate a publisher on close; do not launch through the Editor inspection bridge. Use the existing configured boot host outside its bounded action job.')
    arguments={'start':['processor-start'],'resume':['processor-start','--resume','--operator'],'request-stop':['processor-stop']}[action]
    output=json.loads(run([str(python),'-X','utf8',current['runner'],'--repo',current['repository'],*arguments],identity['repository']))
    if path.read_bytes()!=raw:raise ValueError('Adoption record changed during dispatch; inspect original outcome, never blindly retry')
    return dict(state='existing_owner_action_dispatched',action=action,reportedState=output.get('status','unknown'),owner=owner_status(identity,python),detail='Existing owner retains checks, repair, locks, leases, requests and publication. Start is not completed delivery; no model choice changed.')

def native_host_ready(directory, identity):
    """Verify the exact existing owned startup task and fresh protocol scope; never install or start another host."""
    if os.name != 'nt': raise ValueError('This installed-host bridge requires Windows Task Scheduler')
    directory = checked(directory); configuration = checked(directory/'config.json')
    config = json.loads(configuration.read_text(encoding='utf-8-sig'))
    capability = json.loads(checked(directory/'host-capabilities.json').read_text(encoding='utf-8-sig'))
    if config.get('repo') != identity['repository'] or config.get('runner') != identity['runner'] or config.get('runnerHashes') != identity['runnerHashes'] or not config.get('enabled'): raise ValueError('Host configuration does not match the reviewed existing runner')
    if capability.get('formatVersion') != 1 or capability.get('protocol') != 'existing-runner-host-v1' or not capability.get('enabled') or capability.get('runnerHashes') != identity['runnerHashes']: raise ValueError('Existing host does not advertise this supported control protocol; update/review it explicitly')
    heartbeat = datetime.fromisoformat(capability['heartbeatUtc'])
    if heartbeat.tzinfo is None or not -10 <= (datetime.now(timezone.utc)-heartbeat).total_seconds() <= 90: raise ValueError('Host heartbeat is stale; no control request dispatched')
    task_name = 'Geurts Archangels Queue - ' + Path(identity['repository']).name
    if not re.fullmatch('[A-Za-z0-9 _.-]{1,100}',task_name): raise ValueError('Unsupported task name; preserve scope')
    deployed = checked(directory/'worker/queue_worker.py')
    deployment = json.loads(checked(directory/'deployment.json').read_text(encoding='utf-8-sig'))
    if digest(deployed.read_bytes()).lower() != deployment.get('sha256','').lower(): raise ValueError('Modified/unowned host deployment preserved')
    def quote(value): return "'"+str(value).replace("'","''")+"'"
    expected_arguments = '-X utf8 "'+str(deployed)+'" --config "'+str(configuration)+'" serve'
    script = "$ownedTask=Get-ScheduledTask -TaskName "+quote(task_name)+" -ErrorAction Stop; $ownedSid=if($ownedTask.Principal.UserId.StartsWith('S-1-')){[Security.Principal.SecurityIdentifier]::new($ownedTask.Principal.UserId).Value}else{[Security.Principal.NTAccount]::new($ownedTask.Principal.UserId).Translate([Security.Principal.SecurityIdentifier]).Value}; [pscustomobject]@{ready=([string]$ownedTask.State -eq 'Running' -and $ownedTask.Description -like 'User-authorized optional Archangels supervisor.*' -and $ownedTask.Actions.Count -eq 1 -and [IO.Path]::GetFullPath($ownedTask.Actions[0].Execute) -eq "+quote(str(checked(config['python'])))+" -and [IO.Path]::GetFullPath($ownedTask.Actions[0].WorkingDirectory) -eq "+quote(str(deployed.parent))+" -and $ownedTask.Actions[0].Arguments -eq "+quote(expected_arguments)+" -and $ownedSid -eq [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -and $ownedTask.Triggers[0].CimClass.CimClassName -eq 'MSFT_TaskBootTrigger')}|ConvertTo-Json -Compress"
    encoded = base64.b64encode(script.encode('utf-16-le')).decode('ascii')
    native = json.loads(run(['powershell.exe','-NoProfile','-NonInteractive','-EncodedCommand',encoded],identity['repository']))
    if native.get('ready') is not True: raise ValueError('Exact owned independent boot task is not running for this account; no alternate task/host created')
    return directory


def request_host(record_path, host_state, action, operator=False, wait_seconds=0, expected_repository=None):
    """Dispatch only a durable narrow intent to the existing independent host, surviving bounded Editor/client cleanup."""
    if not operator or action not in ('start','resume','request-stop'): raise ValueError('Select an explicit supported operator host action')
    if not 0 <= wait_seconds <= 45: raise ValueError('Wait must be bounded to zero through45seconds')
    record=json.loads(checked(record_path).read_text(encoding='utf-8-sig'))
    checksum=digest(json.dumps({key:value for key,value in record.items() if key!='recordSha256'},sort_keys=True,separators=(',',':')).encode())
    if record.get('mode')!='existing-owner-adapter' or record.get('recordSha256')!=checksum: raise ValueError('Modified/unsupported adoption record preserved')
    if expected_repository is None or checked(expected_repository) != checked(record['identity']['repository']): raise ValueError('Selected repository does not match the adoption record; no host request dispatched')
    identity=snapshot(record['identity']['repository'])
    if identity['scopeFingerprint'] != record['identity'].get('scopeFingerprint'): raise ValueError('Reviewed existing owner scope changed; no host request dispatched')
    directory=native_host_ready(host_state,identity)
    identifier=uuid.uuid4().hex;requests=checked(directory/'control-requests');requests.mkdir(exist_ok=True)
    value=dict(formatVersion=1,requestId=identifier,createdUtc=datetime.now(timezone.utc).isoformat(),action=action,repository=identity['repository'],runner=identity['runner'],runnerHashes=identity['runnerHashes'],configurationSha256=identity['configurationSha256'])
    target=checked(requests/(identifier+'.json'));temporary=checked(requests/(identifier+'.tmp'))
    with temporary.open('x',encoding='utf-8') as stream:json.dump(value,stream,indent=2)
    if target.exists():raise ValueError('Request identity collision preserved')
    os.rename(temporary,target)
    receipt=checked(directory/'control-receipts'/(identifier+'.json'));deadline=time.monotonic()+wait_seconds
    while not receipt.exists() and time.monotonic()<deadline:time.sleep(min(.25,max(0,deadline-time.monotonic())))
    return dict(state='host_request_dispatched',requestId=identifier,action=action,receiptPath=str(receipt),receipt=json.loads(receipt.read_text(encoding='utf-8-sig')) if receipt.exists() else None,detail='Existing independent boot host owns execution; a bounded client exit/cancel does not undo the request. Read this exact receipt/owner state before retrying. Dispatched is not delivered.')


def main():
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='action',required=True)
    check=sub.add_parser('inspect');check.add_argument('--repository',required=True);check.add_argument('--python',default=sys.executable)
    adoption=sub.add_parser('adopt');adoption.add_argument('--repository',required=True);adoption.add_argument('--python',default=sys.executable);adoption.add_argument('--destination',required=True);adoption.add_argument('--expected-fingerprint',required=True);adoption.add_argument('--operator',action='store_true',required=True)
    for action in ('status','start','resume','request-stop'):
        command=sub.add_parser(action);command.add_argument('--record',required=True)
        if action!='status':command.add_argument('--operator',action='store_true',required=True)
    host=sub.add_parser('request-host');host.add_argument('--repository',required=True);host.add_argument('--record',required=True);host.add_argument('--host-state',required=True);host.add_argument('--operation',choices=('start','resume','request-stop'),required=True);host.add_argument('--operator',action='store_true',required=True);host.add_argument('--wait-seconds',type=int,default=0)
    arguments=parser.parse_args()
    try:
        result=inspect(arguments.repository,arguments.python) if arguments.action=='inspect' else adopt(arguments.repository,arguments.python,arguments.destination,arguments.expected_fingerprint) if arguments.action=='adopt' else request_host(arguments.record,arguments.host_state,arguments.operation,arguments.operator,arguments.wait_seconds,arguments.repository) if arguments.action=='request-host' else dispatch(arguments.record,arguments.action,getattr(arguments,'operator',False))
        print(json.dumps(result,indent=2));return 0
    except (OSError,ValueError,KeyError,subprocess.SubprocessError) as error:
        print(json.dumps(dict(state='blocked',cause=type(error).__name__,detail=str(error),originalOwner='Preserved; no reset/discard/lease deletion/check bypass')));return 1

if __name__=='__main__':sys.exit(main())
