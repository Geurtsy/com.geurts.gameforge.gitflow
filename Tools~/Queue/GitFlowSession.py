"""Parent-owned restoration of explicitly declared native session outputs.

Only trusted operator policy declares these exact tracked paths. The parent
survives cancellation of the native child job; unknown files remain untouched.
"""
from pathlib import Path
import re


def validate(q, names):
    if not isinstance(names, list) or any(not isinstance(name, str) for name in names) or len(names) != len(set(names)):
        raise q.QueueError('Native session outputs must be a unique list of literal relative paths.')
    for name in names:
        if (not isinstance(name, str) or not name or name == '.' or '\\' in name
                or name.startswith('/') or ':' in name or any(part in {'.', '..', '.git'} for part in name.split('/'))
                or any(mark in name for mark in '*?[')):
            raise q.QueueError('Native session output must be an exact relative project path.')


class Snapshot:
    def __init__(self, q, root, folder, names):
        validate(q, names)
        self.q, self.root, self.folder = q, root, folder
        self.head = q.git(root, 'rev-parse', 'HEAD')
        self.index = Path(q.git(root, 'rev-parse', '--path-format=absolute', '--git-path', 'index'))
        self.index_before = self.index.read_bytes()
        self.files = {}
        records = {}
        for number, name in enumerate(names):
            path = q.plain(root / name)
            if not path.is_file():
                raise q.QueueError('Declared native session output is missing: ' + name)
            data = path.read_bytes()
            artifact = 'session-original-' + str(number) + '.bin'
            (folder / artifact).write_bytes(data)
            self.files[name] = data
            records[name] = dict(artifact=artifact, sha256=q.digest(data))
        # Native Git still applies the real clean filters for each original path.
        # Bounded argv batches avoid two Windows process launches per file, with
        # ample command-line headroom under the supported 259-character paths.
        for offset in range(0, len(names), 64):
            selected = names[offset:offset + 64]
            blobs = q.run(['git', 'hash-object', '--', *selected], root).decode('ascii').splitlines()
            committed = q.run(['git', 'rev-parse', *[self.head + ':' + name for name in selected]], root).decode('ascii').splitlines()
            if (len(blobs) != len(selected) or len(committed) != len(selected)
                    or any(not re.fullmatch('[0-9a-f]{40}', value) for value in blobs + committed)):
                raise q.QueueError('Native session Git identity batch was incomplete or malformed.')
            for name, blob, expected in zip(selected, blobs, committed):
                if blob != expected:
                    raise q.QueueError('Native session original differs from committed candidate: ' + name)
                records[name]['blob'] = blob
        # hash-object reads worktree paths. Bind its result back to the captured
        # raw originals before launching any native child; races remain failures.
        if q.git(root, 'rev-parse', 'HEAD') != self.head or self.index.read_bytes() != self.index_before:
            raise q.QueueError('Native session capture changed HEAD/index; originals retained.')
        for name, data in self.files.items():
            if q.plain(root / name).read_bytes() != data:
                raise q.QueueError('Native session original changed during capture: ' + name)
        q.durable(folder / 'session-originals.json', dict(head=self.head, index=q.digest(self.index_before), files=records))

    def restore(self):
        q = self.q
        if q.git(self.root, 'rev-parse', 'HEAD') != self.head or self.index.read_bytes() != self.index_before:
            raise q.QueueError('Native gate changed HEAD/index; session originals retained without restoration.')
        # Preflight the complete set before the first mutation. New/deleted paths
        # and reparse replacements are source divergence, never disposable caches.
        for name in self.files:
            if not q.plain(self.root / name).is_file():
                raise q.QueueError('Native session output was deleted; preserve all remaining work: ' + name)
        restored = []
        for name, data in self.files.items():
            path = q.plain(self.root / name)
            if path.read_bytes() != data:
                path.write_bytes(data)
                restored.append(name)
        q.durable(self.folder / 'session-restoration.json', dict(head=self.head, restored=restored,
                  index_unchanged=self.index.read_bytes() == self.index_before, owner='parent controller'))
