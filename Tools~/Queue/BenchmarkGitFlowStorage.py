"""Disposable measured comparison: three retained full clones versus one shared reusable workspace.

This is a fixed synthetic workload, not a prediction of production savings or
physical disk allocation. Every measured byte exists at the observation point.
"""
import argparse
import json
import os
from pathlib import Path
import random
import tempfile
import time

import GitFlowQueue as queue
from GitFlowStorage import inspect_storage


def measure():
    started = time.monotonic()
    with tempfile.TemporaryDirectory(prefix='gq_storage_', dir='C:/' if os.name == 'nt' else None) as temporary:
        root = Path(temporary)
        original = root / 'Original'
        original.mkdir()
        queue.git(original, 'init', '-b', 'main')
        queue.git(original, 'config', 'user.name', 'GitFlow storage acceptance')
        queue.git(original, 'config', 'user.email', 'acceptance@example.invalid')
        (original / 'Source.bin').write_bytes(random.Random(7).randbytes(2 * 1024 * 1024))
        (original / '.gitignore').write_text('Library/\n')
        queue.git(original, 'add', '.')
        queue.git(original, 'commit', '-m', 'Fixed representative binary source')
        baseline = root / 'Baseline'; baseline.mkdir()
        reusable = root / 'Reusable'; reusable.mkdir()
        workspace = reusable / 'Workspace'
        queue.git(original, 'worktree', 'add', '--detach', str(workspace), 'HEAD')
        samples = []
        for request in range(1, 4):
            clone = baseline / ('Request' + str(request))
            queue.git(root, 'clone', '--no-local', str(original), str(clone))
            for folder in [clone, workspace]:
                library = folder / 'Library'; library.mkdir(exist_ok=True)
                cache = library / 'RepresentativeCache.bin'
                if not cache.exists(): cache.write_bytes(b'C' * (16 * 1024 * 1024))
            evidence = reusable / 'Evidence'; evidence.mkdir(exist_ok=True)
            (evidence / ('request_' + str(request) + '.log')).write_bytes(b'E' * 65536)
            (clone / 'Evidence.log').write_bytes(b'E' * 65536)
            samples.append(dict(request=request,
                retained_full_clones=inspect_storage([original, baseline]),
                shared_reusable_workspace=inspect_storage([original, reusable])))
        return dict(schema=1, workload=dict(requests=3, source_bytes=2*1024*1024, cache_bytes=16*1024*1024,
                    evidence_bytes_per_request=65536), samples=samples, elapsed_seconds=time.monotonic()-started,
                    limits='Unique logical bytes, not allocated clusters. Synthetic retained-clone baseline; no production workspace was modified.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True)
    arguments = parser.parse_args()
    result = measure()
    queue.durable(arguments.output, result)
    print(json.dumps(dict(output=arguments.output, elapsed_seconds=result['elapsed_seconds'])))
