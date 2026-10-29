"""Read-only storage accounting. Never follows links, deletes files or opens Unity."""
import os
from pathlib import Path
import stat
import time


def category(relative):
    """Keep imported caches, immutable evidence and authored inputs distinct."""
    parts = [part.casefold() for part in relative.parts]
    if '.git' in parts or 'objects' in parts and ('git' in parts or 'control' in parts):
        return 'git_data'
    if 'library' in parts:
        return 'unity_library'
    if any(part in {'temp', 'obj', '__pycache__'} for part in parts):
        return 'temporary_data'
    if any(part in {'evidence', 'receipts', 'logs'} for part in parts):
        return 'logs_and_evidence'
    if any(part in {'archives', 'recovery'} for part in parts) or relative.name.lower().endswith(('.zip', '.zst', '.bundle', '.tar')):
        return 'archives'
    return 'source_and_assets'


def inspect_storage(roots, maximum_seconds=120):
    """Measure logical bytes and file identity deduplication; inaccessible paths remain explicit."""
    started = time.monotonic()
    seen = set()
    result = {'schema': 1, 'measurement': 'logical_file_bytes', 'roots': [], 'total_bytes': 0,
              'unique_file_bytes': 0, 'complete': True, 'issues': []}
    for supplied in roots:
        root = Path(supplied).absolute()
        item = {'root': str(root), 'categories': {}, 'bytes': 0, 'files': 0}
        result['roots'].append(item)
        pending = [root]
        while pending:
            current = pending.pop()
            if time.monotonic() - started > maximum_seconds:
                result['complete'] = False
                result['issues'].append('Time budget reached; remaining paths were not counted.')
                return result
            try:
                metadata = current.lstat()
                if current.is_symlink() or getattr(metadata, 'st_file_attributes', 0) & 0x400:
                    result['complete'] = False
                    result['issues'].append('Link/reparse path excluded: ' + str(current))
                    continue
                if stat.S_ISDIR(metadata.st_mode):
                    with os.scandir(current) as entries:
                        pending.extend(Path(entry.path) for entry in entries)
                elif stat.S_ISREG(metadata.st_mode):
                    key = 'git_data' if root.name.casefold() == '.git' else category(current.relative_to(root))
                    item['categories'][key] = item['categories'].get(key, 0) + metadata.st_size
                    item['files'] += 1
                    item['bytes'] += metadata.st_size
                    result['total_bytes'] += metadata.st_size
                    identity = (metadata.st_dev, metadata.st_ino)
                    if identity not in seen:
                        seen.add(identity)
                        result['unique_file_bytes'] += metadata.st_size
            except OSError as error:
                result['complete'] = False
                result['issues'].append(type(error).__name__ + ': ' + str(current))
    result['elapsed_seconds'] = round(time.monotonic() - started, 3)
    return result
