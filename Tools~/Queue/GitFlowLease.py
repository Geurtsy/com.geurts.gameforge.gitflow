"""Persistent Git leases: native FF arbitration and atomic publication fencing.

Wall-clock timestamps are audit metadata. Only locally observed, unchanged remote
objects and monotonic elapsed time permit automatic takeover. Never delete refs.
"""
import contextvars
import datetime as dt
import json
import os
from pathlib import Path
import re
import subprocess
import threading
import time
import uuid

PROTOCOL = 1
SECONDS = 1800
COMPATIBLE_SECONDS = frozenset({180, SECONDS})
HEARTBEAT = 30
TRANSPORT_TIMEOUT = 300
ACTIVE = contextvars.ContextVar("integration_lease", default=None)
OBSERVED = {}


class LeaseError(RuntimeError):
    """Ownership is unavailable or lost; preserve candidate and stop publication."""


class Contended(LeaseError):
    """A healthy/unknown owner or its observation window still blocks acquisition."""


class Rejected(LeaseError):
    """A completed Git operation was rejected, distinct from uncertain transport."""


def transport(q, repo, *arguments, timeout=TRANSPORT_TIMEOUT):
    if not 0 < timeout <= TRANSPORT_TIMEOUT:
        raise LeaseError("Invalid bounded lease transport timeout")
    environment = dict(os.environ, GIT_TERMINAL_PROMPT="0", GCM_INTERACTIVE="Never")
    try:
        result = q.process_run(["git", *map(str, arguments)], cwd=repo, env=environment,
                                timeout=timeout, capture_output=True, text=True,
                                encoding="utf-8", errors="replace", **q.console_options())
    except (OSError, subprocess.TimeoutExpired):
        OBSERVED.clear()
        raise
    if result.returncode:
        OBSERVED.clear()
        raise Rejected("Lease Git " + str(arguments[0]) + " failed: "
                         + ((result.stderr + result.stdout).strip()[-4000:] or f"exit {result.returncode}; no output"))
    return result.stdout.rstrip("\r\n")


def snapshot(q, repo, remote):
    """Use immutable objects without racing the controller's FETCH_HEAD."""
    rows = transport(q, repo, "ls-remote", remote, q.LOCK).splitlines()
    if not rows:
        return None, None
    oid = rows[0].split()[0]
    if not re.fullmatch(r"[0-9a-f]{40}", oid):
        raise LeaseError("Invalid remote lease object identity")
    transport(q, repo, "fetch", "--no-write-fetch-head", remote, oid)
    try:
        value = q.read_request(repo, oid)
    except (ValueError, q.QueueError) as error:
        raise LeaseError("Unreadable lock metadata; preserve it for operator review") from error
    return oid, value


def validate(value):
    if not isinstance(value, dict) or "lease_protocol" not in value:
        raise Contended("Legacy lock has no fencing protocol; confirmed-stopped operator migration is required")
    if type(value.get("lease_protocol")) is not int or value["lease_protocol"] != PROTOCOL:
        raise LeaseError("Unsupported/future lock protocol; automatic recovery refused")
    if (not isinstance(value.get("state"), str) or value["state"] not in {"owned", "released"}
            or not isinstance(value.get("owner"), str) or not re.fullmatch(r"[0-9a-f]{32}", value["owner"])
            or not isinstance(value.get("host"), str) or not 1 <= len(value["host"]) <= 128
            or type(value.get("lease_seconds")) is not int or value["lease_seconds"] not in COMPATIBLE_SECONDS
            or not isinstance(value.get("updated_utc"), str)):
        raise LeaseError("Malformed lease metadata; automatic recovery refused")
    try:
        stamp = dt.datetime.fromisoformat(value["updated_utc"])
        if stamp.tzinfo is None:
            raise ValueError("Missing audit timezone")
    except ValueError as error:
        raise LeaseError("Malformed lease audit timestamp") from error
    return value


def verify_child(q, repo, remote, child, released=False, expected_owner=None):
    """Verify reads only; an exact-parent mismatch gets one bounded fresh observation.

    The original two-transport (600s) verification allowance is shared by every
    local/remote Git read here. No push, sleep, deadline extension or write retry
    is permitted. An owned lease still requires the exact child, never a successor.
    """
    deadline = time.monotonic() + 2 * TRANSPORT_TIMEOUT
    evidence = dict(expected_child=child, expected_owner=expected_owner, released=released,
                    observations=[], read_budget_seconds=2 * TRANSPORT_TIMEOUT, phase="initial_ref")
    def read(*arguments):
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise LeaseError("Lease verification read budget expired")
        result = transport(q, repo, *arguments, timeout=min(TRANSPORT_TIMEOUT, remaining))
        if time.monotonic() >= deadline:
            raise LeaseError("Lease verification read budget expired")
        return result
    def observed():
        rows = read("ls-remote", remote, q.LOCK).splitlines()
        if not rows:
            value = None
        else:
            parts = rows[0].split()
            if len(rows) != 1 or len(parts) != 2 or parts[1] != q.LOCK or not re.fullmatch(r"[0-9a-f]{40}", parts[0]):
                raise LeaseError("Malformed advertised lease ref identity")
            value = parts[0]
        evidence["observations"].append(value)
        return value
    def metadata(oid):
        return validate(json.loads(read("show", oid + ":" + q.REQUEST)))
    try:
        current = observed()
        evidence["phase"] = "expected_object"
        read("fetch", "--no-write-fetch-head", remote, child)
        read("cat-file", "-e", child + "^{commit}")
        expected = metadata(child)
        if (expected["state"] != ("released" if released else "owned")
                or expected_owner is not None and expected["owner"] != expected_owner):
            raise LeaseError("Expected lease state/owner differs from the publishing owner")
        if current == child:
            evidence.update(phase="complete", matched="exact_child")
            return evidence
        if current is None:
            raise LeaseError("Published lease ref is missing")
        evidence["phase"] = "mismatch_lineage"
        parents = read("rev-list", "--parents", "-n", "1", child).split()
        parent = parents[1] if len(parents) == 2 and parents[0] == child else None
        evidence["expected_parent"] = parent
        if current == parent:
            prior = metadata(parent)
            if not released and prior["state"] == "owned" and prior["owner"] != expected["owner"]:
                raise LeaseError("Observed parent has another owner")
            evidence["phase"] = "parent_ref_refresh"
            current = observed()  # Exactly one READ retry, only for the known direct parent.
            if current == child:
                evidence.update(phase="complete", matched="exact_child_after_parent")
                return evidence
            raise LeaseError("Exact parent observation did not resolve to the published child")
        if released:
            read("fetch", "--no-write-fetch-head", remote, current)
            metadata(current)
            if read("merge-base", child, current) == child:
                evidence.update(phase="complete", matched="released_descendant")
                return evidence
        raise LeaseError("Observed ref differs from the exact published lease child")
    except Exception as error:
        evidence["outcome"] = "failed"
        failure = LeaseError("Exact published lease ref verification failed; ownership is uncertain; "
                             + "expected=" + str(child) + "; observations=" + repr(evidence["observations"])
                             + "; " + type(error).__name__ + ": " + str(error))
        failure.verification = evidence
        raise failure from error


def visible_lock(q, repo, remote):
    oid, value = snapshot(q, repo, remote)
    if not oid:
        return None
    # Unknown/legacy locks remain visible to existing status/viewer consumers.
    try:
        return None if validate(value)["state"] == "released" else oid
    except LeaseError:
        return oid


class Lease:
    def __init__(self, q, repo, remote, clock=None):
        self.q, self.repo, self.remote = q, repo, remote
        self.clock = clock or time.monotonic
        self.identity = (q.git(repo, "remote", "get-url", remote), q.LOCK)
        self.owner = uuid.uuid4().hex
        self.oid, self.deadline, self.failure, self.closed = None, 0, None, False
        self.attempted_oid, self.verification = None, None
        self.mutex, self.stop = threading.RLock(), threading.Event()
        self.thread = None
        self.work = {}

    def metadata(self, state, operation):
        return dict(lease_protocol=PROTOCOL, state=state, owner=self.owner,
                    host=os.environ.get("COMPUTERNAME", "unknown"), lease_seconds=SECONDS,
                    updated_utc=dt.datetime.now(dt.timezone.utc).isoformat(), operation=operation,
                    process_id=os.getpid(), request=self.work.get("request"), stage=self.work.get("state"))

    def failed(self, error):
        OBSERVED.pop(self.identity, None)
        if getattr(error, "verification", None) is not None:
            self.verification = error.verification
        self.failure = type(error).__name__ + ": " + (str(error) or "no exception message")
        self.stop.set()

    def guard(self, write=False):
        if self.closed:
            raise LeaseError("Released/closed lease owner cannot publish again")
        if self.failure:
            raise LeaseError("Lease owner fenced: " + self.failure)
        if not self.oid or self.clock() >= self.deadline:
            self.failed(LeaseError("Local monotonic ownership deadline expired"))
            raise LeaseError("Lease owner fenced: " + self.failure)
        if write and self.deadline - self.clock() < TRANSPORT_TIMEOUT:
            self.failed(LeaseError("Insufficient ownership time for a bounded Git publication"))
            raise LeaseError("Lease owner fenced: " + self.failure)

    def acquire(self):
        with self.mutex:
            if self.oid or self.failure or self.closed:
                raise LeaseError("A lease instance cannot reacquire after ownership or failure")
            try:
                oid, value = snapshot(self.q, self.repo, self.remote)
                if oid:
                    validate(value)
                    if value["state"] == "owned":
                        now = self.clock()
                        prior = OBSERVED.get(self.identity)
                        if not prior or prior[0] != oid or now < prior[1]:
                            OBSERVED[self.identity] = (oid, now)
                            raise Contended("Lease is owned; observing its unchanged object for automatic recovery")
                        if now - prior[1] < value["lease_seconds"]:
                            raise Contended("Lease observation window has not expired")
                parent = oid or self.q.git(self.repo, "rev-parse", self.remote + "/main")
                dispatch = self.clock()
                child = self.q.object_commit(self.repo, parent, self.metadata("owned", "claim"), "Integration lease claim")
                self.attempted_oid = child
                try:
                    transport(self.q, self.repo, "push", self.remote, child + ":" + self.q.LOCK)
                except Rejected:
                    current, metadata = snapshot(self.q, self.repo, self.remote)
                    if current and current not in {oid, child}:
                        validate(metadata)
                        raise Contended("Another contender won lease acquisition") from None
                    raise
                self.verification = verify_child(self.q, self.repo, self.remote, child, expected_owner=self.owner)
                self.oid, self.deadline = child, dispatch + SECONDS
                self.guard()
                OBSERVED.pop(self.identity, None)
                return self
            except Contended:
                raise
            except Exception as error:
                self.failed(error)
                raise LeaseError("Lease acquisition failed; preserve candidate: " + self.failure) from error

    def advance(self, state="owned", operation="heartbeat", updates=()):
        with self.mutex:
            self.guard()
            self.attempted_oid, self.verification = None, None
            dispatch = self.clock()
            try:
                child = self.q.object_commit(self.repo, self.oid, self.metadata(state, operation),
                                             "Integration lease " + operation)
                self.guard(write=True)
                # The lease ref and every delivery/control target change together.
                # A successor of our held OID makes our child a rejected sibling.
                self.attempted_oid = child
                transport(self.q, self.repo, "push", "--atomic", self.remote,
                          child + ":" + self.q.LOCK, *updates)
                self.verification = verify_child(self.q, self.repo, self.remote, child,
                                                 released=state == "released", expected_owner=self.owner)
                # A late ACK/verification must not revive the previous ownership.
                # Keep the old deadline until the whole operation proves ownership.
                self.guard()
                self.oid, self.deadline = child, dispatch + SECONDS
                self.guard()
                return child
            except Exception as error:
                self.failed(error)
                raise LeaseError("Lease publication fenced; result may need receipt recovery: " + self.failure) from error

    def publish(self, root, remote, refspecs):
        try:
            return self._publish(root, remote, refspecs)
        except Exception as error:
            self.failed(error)
            raise LeaseError("Lease publication fenced: " + self.failure) from error

    def _publish(self, root, remote, refspecs):
        with self.mutex:
            self.guard()
            if self.q.git(root, "remote", "get-url", remote) != self.identity[0]:
                raise LeaseError("Publication remote differs from the owning lease")
            updates = []
            for spec in refspecs:
                source, target = spec.split(":", 1)
                if source.startswith("+") or not target.startswith("refs/heads/") or target == self.q.LOCK:
                    raise LeaseError("Unsafe fenced publication refspec")
                oid = self.q.git(root, "rev-parse", source)
                # Independent configured clones may have a different object store.
                root_common = (Path(root) / self.q.git(root, "rev-parse", "--git-common-dir")).resolve()
                owner_common = (Path(self.repo) / self.q.git(self.repo, "rev-parse", "--git-common-dir")).resolve()
                if root_common != owner_common:
                    transport(self.q, self.repo, "fetch", "--no-write-fetch-head", str(root), oid)
                updates.append(oid + ":" + target)
            return self.advance(operation="publish", updates=updates)

    def start(self):
        def beat():
            while not self.stop.wait(HEARTBEAT):
                try:
                    self.advance()
                except LeaseError:
                    return
        self.thread = threading.Thread(target=beat, name="integration-lease-heartbeat", daemon=True)
        self.thread.start()

    def close(self):
        self.stop.set()
        if self.thread:
            # An owned heartbeat can push, inspect and fetch its exact child.
            self.thread.join(TRANSPORT_TIMEOUT * 3 + 5)
            if self.thread.is_alive():
                self.failed(LeaseError("Heartbeat failed to stop within its bounded shutdown window"))
        try:
            if self.failure:
                raise LeaseError("Lease owner fenced: " + self.failure)
            self.advance(state="released", operation="release")
        finally:
            self.closed = True


def confirmed_release(q, repo, remote, expected):
    """Exact-SHA stopped-owner migration/release; preserve permanent fencing lineage."""
    oid, value = snapshot(q, repo, remote)
    if oid != expected:
        raise LeaseError("Lock changed; recovery refused")
    if isinstance(value, dict) and "lease_protocol" in value:
        validate(value)
    lease = Lease(q, repo, remote)
    child = q.object_commit(repo, oid, lease.metadata("released", "confirmed stopped-owner migration"),
                            "Integration lease confirmed release")
    transport(q, repo, "push", remote, child + ":" + q.LOCK)
    verify_child(q, repo, remote, child, released=True, expected_owner=lease.owner)
    return child
