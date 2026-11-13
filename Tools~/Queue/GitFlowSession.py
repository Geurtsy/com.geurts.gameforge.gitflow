"""Parent-owned restoration of explicitly declared native session outputs.

Only trusted operator policy declares these exact tracked paths. The parent
survives cancellation of the native child job; unknown files remain untouched.
"""
from pathlib import Path


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
            blob = q.run(['git', 'hash-object', '--stdin', '--path=' + name], root, data=data).decode().strip()
            if blob != q.git(root, 'rev-parse', 'HEAD:' + name):
                raise q.QueueError('Native session original differs from committed candidate: ' + name)
            artifact = 'session-original-' + str(number) + '.bin'
            (folder / artifact).write_bytes(data)
            self.files[name] = data
            records[name] = dict(artifact=artifact, sha256=q.digest(data), blob=blob)
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
