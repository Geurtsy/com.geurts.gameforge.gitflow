"""Supported silent native Unity adapters; no live-project edits or player builds."""
import argparse
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as xml

from GitFlowQueue import QueueError, durable, plain
from GitFlowProcess import process_run


def execute(arguments):
    root, editor, evidence = plain(arguments.repository), plain(arguments.unity), plain(arguments.evidence)
    if not editor.is_file():
        raise QueueError('Configured Unity Editor is unavailable.')
    evidence.mkdir(parents=True, exist_ok=True)
    log = evidence / 'Unity.log'
    command = [str(editor), '-batchmode', '-nographics', '-nosound', '-projectPath', str(root), '-logFile', str(log)]
    if arguments.action == 'synchronize-version':
        command.extend(['-quit', '-executeMethod', 'Geurts.GameForge.GitFlow.Editor.GitFlowNativeGate.SynchronizeVersion',
                        '-gitFlowVersion', arguments.version])
    elif arguments.action == 'compile-project':
        command.extend(['-quit', '-executeMethod', 'Geurts.GameForge.GitFlow.Editor.GitFlowNativeGate.CompilationCompleted',
                        '-gitFlowEvidence', str(evidence), '-gitFlowRun', arguments.run, '-gitFlowCandidate', arguments.candidate])
    else:
        if not arguments.test_filter:
            raise QueueError('Select the project-owned required test filter before configuring publication.')
        command.extend(['-runTests', '-testPlatform', arguments.test_platform, '-testFilter', arguments.test_filter,
                        '-testResults', str(evidence / 'Results.xml')])
    with (evidence / 'process.log').open('wb') as output:
        result = process_run(command, cwd=root, stdout=output, stderr=output, timeout=7200)
    if result.returncode or not log.is_file():
        raise QueueError('Unity did not complete successfully; inspect retained Unity.log and process.log.')
    text = log.read_text(encoding='utf-8-sig', errors='replace')
    if re.search(r'\b(?:warning|error)\s+(?:CS|BC|IL|NU)\d+|scripts have compiler errors|compilation failed', text, re.I):
        raise QueueError('Compiler warning/error in the complete Unity log; required gate failed.')
    if arguments.action == 'run-tests':
        report = xml.parse(evidence / 'Results.xml').getroot()
        if (report.attrib.get('result') != 'Passed' or int(report.attrib.get('failed', '0')) != 0
                or int(report.attrib.get('skipped', '0')) != 0 or int(report.attrib.get('passed', '0')) < 1):
            raise QueueError('Required native tests did not all pass, or no tests ran.')
        durable(evidence / 'result.json', dict(run=arguments.run, candidate=arguments.candidate, status='passed',
                warnings=0, errors=0, skipped=0, tests=int(report.attrib['passed'])))
    elif arguments.action == 'compile-project' and not (evidence / 'result.json').is_file():
        raise QueueError('Native compilation callback did not produce its exact-run report.')


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
