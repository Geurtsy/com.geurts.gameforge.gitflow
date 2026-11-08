"""Compare the exact previous reader with the candidate on 314 disposable refs."""
import argparse
import ast
import json
import os
from pathlib import Path
import sys
import time
from unittest import mock

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', required=True, type=Path, help='New task-owned JSON report path; parent must exist.')
arguments = parser.parse_args()
if arguments.output.exists() or not arguments.output.parent.is_dir():
    parser.error('Choose a missing output file in an existing task-owned directory.')
root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / 'Tools~/Queue'))
import GitFlowQueue as queue
from TestGitFlowQueue import ReferenceReadAcceptance
queue.plain(arguments.output)

os.environ.update(GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM='1',
                  GIT_TERMINAL_PROMPT='0', GCM_INTERACTIVE='Never')
source = queue.run(['git', '-c', 'safe.directory=' + root.as_posix(),
                    '-C', str(root), 'show',
                    '2e5ab9ab08cc6a3bcd505f7dc16e71a2cded494c:Tools~/Queue/GitFlowQueue.py'], root).decode()
previous = next(node for node in ast.parse(source).body if isinstance(node, ast.FunctionDef) and node.name == 'entries')
namespace = dict(queue.__dict__)
exec(compile(ast.Module(body=[previous], type_ignores=[]), '<exact previous entries>', 'exec'), namespace)

fixture = ReferenceReadAcceptance()
try:
    fixture.setUp()
    baseline = fixture.root / 'Baseline'
    os.environ.update(GIT_CONFIG_COUNT='4', GIT_CONFIG_KEY_3='safe.directory', GIT_CONFIG_VALUE_3=baseline.as_posix())
    queue.git(fixture.root, 'clone', '--branch', 'main', '--no-hardlinks', str(fixture.reader), str(baseline))
    queue.git(baseline, 'remote', 'set-url', 'origin', str(fixture.remote))
    fixture.records(314)
    print('Prepared 314 local request refs; comparing unchanged records.', flush=True)

    def measure(reader, repository):
        started = time.monotonic()
        with mock.patch.object(queue, 'run', wraps=queue.run) as calls:
            values = reader(repository, 'origin')
        commands = [call.args[0] for call in calls.call_args_list]
        return values, dict(seconds=round(time.monotonic() - started, 6),
                            git_processes=len(commands),
                            fetch_commands=sum(command[1] == 'fetch' for command in commands),
                            ls_remote_commands=sum(command[1] == 'ls-remote' for command in commands),
                            blob_batch_commands=sum(command[1:] == ['cat-file', '--batch'] for command in commands))

    before, old = measure(namespace['entries'], baseline)
    print('Previous reader completed: ' + json.dumps(old), flush=True)
    after, cold = measure(queue.entries, fixture.reader)
    again, warm = measure(queue.entries, fixture.reader)
    if before != after or after != again:
        raise AssertionError('Readers disagree on request identities, contents or order')
    if old['fetch_commands'] != 314 or cold['fetch_commands'] != 1 or warm['fetch_commands'] != 0:
        raise AssertionError('Unexpected reference transport counts')
    result = dict(schema=1, records=314, transport='absolute local bare Git repository; no GitHub or LFS',
                  baseline_commit='2e5ab9ab08cc6a3bcd505f7dc16e71a2cded494c',
                  identical_results=True, baseline=old, candidate_cold=cold, candidate_warm=warm,
                  scope='Synthetic local fixture; not a production duration, network-byte or LFS savings forecast.')
    queue.plain(arguments.output)
    with arguments.output.open('x', encoding='utf-8', newline='') as output:
        output.write(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result), flush=True)
finally:
    fixture.doCleanups()
