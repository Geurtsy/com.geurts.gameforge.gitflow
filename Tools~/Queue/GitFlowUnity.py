"""Supported silent native Unity adapters; no live-project edits or player builds."""
import argparse
import json
from pathlib import Path
import re
import sys
import time
import xml.etree.ElementTree as xml

from GitFlowQueue import QueueError, durable, plain, file_digest
from GitFlowProcess import process_run


def execute(arguments):
    deadline = time.monotonic() + 7200
    root, editor, evidence = plain(arguments.repository), plain(arguments.unity), plain(arguments.evidence)
    if not editor.is_file():
        raise QueueError('Configured Unity Editor is unavailable.')
    if arguments.action == 'compile-project' and (evidence == root or evidence.is_relative_to(root)):
        raise QueueError('Full compiler evidence must be outside the project; select the publisher workspace Evidence folder.')
    evidence.mkdir(parents=True, exist_ok=True)
    if any((evidence / name).exists() for name in ['Unity.log', 'compilation.json', 'result.json', 'version-proof.json', 'Results.xml']):
        raise QueueError('Native evidence output already exists. Preserve it and select a fresh invocation folder.')
    log = evidence / 'Unity.log'
    command = [str(editor), '-batchmode', '-nographics', '-nosound', '-projectPath', str(root), '-logFile', str(log)]
    if arguments.action == 'synchronize-version':
        command.extend(['-quit', '-executeMethod', 'Geurts.GameForge.GitFlow.Editor.GitFlowNativeGate.SynchronizeVersion',
                        '-gitFlowVersion', arguments.version, '-gitFlowEvidence', str(evidence), '-gitFlowVersionProofProtocol', '1'])
    elif arguments.action == 'compile-project':
        command.extend(['-executeMethod', 'Geurts.GameForge.GitFlow.Editor.GitFlowNativeGate.BeginCompilation',
                        '-gitFlowEvidence', str(evidence), '-gitFlowRun', arguments.run, '-gitFlowCandidate', arguments.candidate,
                        '-gitFlowCompilationProtocol', '1'])
    else:
        if not arguments.test_filter:
            raise QueueError('Select the project-owned required test filter before configuring publication.')
        command.extend(['-runTests', '-testPlatform', arguments.test_platform, '-testFilter', arguments.test_filter,
                        '-testResults', str(evidence / 'Results.xml')])
    with (evidence / 'process.log').open('wb') as output:
        result = process_run(command, cwd=root, stdout=output, stderr=output, timeout=max(1, deadline - time.monotonic()))
    if not log.is_file():
        raise QueueError('Unity did not complete successfully; inspect retained Unity.log and process.log.')
    text = log.read_text(encoding='utf-8-sig', errors='replace')
    diagnostics = [line for line in text.splitlines() if re.search(r'\b(?:warning|error)\s+[A-Z]+\d+|shader (?:warning|error)|scripts have compiler errors|compilation failed|Asset .*\.(?:cs|asmdef|asmref|dll|shader|compute|uxml|uss) has no meta file.*immutable folder', line, re.I)]
    if diagnostics:
        existing = json.loads((evidence / 'diagnostics.json').read_text(encoding='utf-8-sig')) if (evidence / 'diagnostics.json').exists() else {}
        durable(evidence / 'diagnostics.json', dict(existing, complete_matching_diagnostics=sorted(set(diagnostics + existing.get('complete_matching_diagnostics', []))), full_log_sha256=file_digest(log)))
        raise QueueError('Compiler warning/error in the complete Unity log; required gate failed.')
    if result.returncode:
        raise QueueError('Unity did not complete successfully; inspect retained Unity.log and process.log.')
    if arguments.action == 'synchronize-version':
        proof = json.loads((evidence / 'version-proof.json').read_text(encoding='utf-8-sig'))
        if proof.get('protocol') != 1 or proof.get('version') != arguments.version or plain(proof.get('project', '')) != root:
            raise QueueError('Native version proof does not match this exact publisher project and assigned version.')
    if arguments.action == 'run-tests':
        report = xml.parse(evidence / 'Results.xml').getroot()
        if (report.attrib.get('result') != 'Passed' or int(report.attrib.get('failed', '0')) != 0
                or int(report.attrib.get('skipped', '0')) != 0 or int(report.attrib.get('passed', '0')) < 1):
            raise QueueError('Required native tests did not all pass, or no tests ran.')
        durable(evidence / 'result.json', dict(run=arguments.run, candidate=arguments.candidate, status='passed',
                warnings=0, errors=0, skipped=0, tests=int(report.attrib['passed'])))
    elif arguments.action == 'compile-project':
        report = json.loads((evidence / 'compilation.json').read_text(encoding='utf-8-sig'))
        if (report.get('full_script_rebuild_requested') is not True or report.get('pipeline_started') is not True
                or report.get('expected_assemblies', 0) < 1 or report.get('run') != arguments.run or report.get('candidate') != arguments.candidate
                or report.get('observed_warnings') != 0 or report.get('observed_errors') != 0):
            raise QueueError('Native compilation callback did not prove a full script rebuild.')
        compatibility = plain(report.get('compatibility_package_root', ''))
        package = json.loads((compatibility / 'package.json').read_text(encoding='utf-8-sig'))
        qualifier = compatibility / 'Tools~' / 'qualify_compilers.py'
        if (package.get('name') != 'com.geurts.gameforge.compatibility' or package.get('version') != report.get('compatibility_version')
                or not qualifier.is_file()):
            raise QueueError('Native compilation did not identify the required installed compiler qualifier.')
        # Compiler copies can contain licensed derived binaries. Keep them outside the flat portable gate artifacts.
        private = evidence.with_name(evidence.name + '.compiler-private')
        private_log = plain(evidence.with_name(evidence.name + '.compiler-private-process.log'))
        if private_log.exists():
            raise QueueError('Private compiler process evidence already exists; select a fresh invocation folder.')
        qualifier_hash = file_digest(qualifier)
        with private_log.open('xb') as output:
            qualified = process_run([sys.executable, str(qualifier), '--project', str(root), '--unity-editor', str(editor),
                    '--destination', str(private)], cwd=root, stdout=output, stderr=output, timeout=max(1, deadline - time.monotonic()))
        qualification = json.loads((private / 'qualification.json').read_text(encoding='utf-8-sig'))
        assemblies = qualification.get('assemblies', [])
        complete = (qualified.returncode == 0 and qualification.get('passed') is True
                and qualification.get('diagnosticPolicyVersion') == 'native-default-diagnostics-v1'
                and qualification.get('inputsUnchanged') is True and qualification.get('diagnosticPolicyCopiesUnchanged') is True
                and qualification.get('nativeResponseCount', 0) > 0 and len(assemblies) == qualification['nativeResponseCount']
                and all(item.get('exitCode') == 0 and item.get('diagnosticCount') == 0 for item in assemblies)
                and file_digest(qualifier) == qualifier_hash)
        durable(evidence / 'compiler-qualification.json', dict(run=arguments.run, candidate=arguments.candidate,
                status='passed' if complete else 'failed', diagnostic_policy_version=qualification.get('diagnosticPolicyVersion'),
                assemblies=len(assemblies), diagnostics=sum(item.get('diagnosticCount', 0) for item in assemblies),
                inputs_unchanged=qualification.get('inputsUnchanged'), policy_copies_unchanged=qualification.get('diagnosticPolicyCopiesUnchanged'),
                qualifier_sha256=qualifier_hash, private_report_sha256=file_digest(private / 'qualification.json'),
                private_evidence_scope='Owner-local sibling folder; source/reference inventory and derived binaries are not portable artifacts.'))
        if not complete:
            raise QueueError('Unsuppressed full compiler qualification failed; preserve the private owner evidence before retrying.')
        durable(evidence / 'result.json', dict(run=arguments.run, candidate=arguments.candidate, status='passed', warnings=0, errors=0, skipped=0,
                full_script_rebuild=True, native_report_sha256=file_digest(evidence / 'compilation.json'), full_log_sha256=file_digest(log),
                compiler_qualification_sha256=file_digest(evidence / 'compiler-qualification.json')))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['synchronize-version', 'compile-project', 'run-tests'])
    parser.add_argument('--repository', required=True)
    parser.add_argument('--unity', required=True)
    parser.add_argument('--evidence', required=True)
    parser.add_argument('--version')
    parser.add_argument('--run')
    parser.add_argument('--candidate')
    parser.add_argument('--test-filter')
    parser.add_argument('--test-platform', choices=['EditMode', 'PlayMode'], default='EditMode')
    try:
        execute(parser.parse_args())
    except Exception as error:
        print(type(error).__name__ + ': ' + str(error))
        sys.exit(1)
