"""Windowless, bounded child processes with Windows job-owned descendants."""
from contextlib import contextmanager
import ctypes
from ctypes import wintypes
import os
import subprocess
import time


if os.name == 'nt':
    class BasicLimits(ctypes.Structure):
        _fields_ = [('process_time', ctypes.c_int64), ('job_time', ctypes.c_int64),
            ('flags', wintypes.DWORD), ('minimum_working', ctypes.c_size_t), ('maximum_working', ctypes.c_size_t),
            ('active_processes', wintypes.DWORD), ('affinity', ctypes.c_size_t),
            ('priority', wintypes.DWORD), ('scheduling', wintypes.DWORD)]

    class Counters(ctypes.Structure):
        _fields_ = [(name, ctypes.c_uint64) for name in ['reads', 'writes', 'others', 'read_bytes', 'write_bytes', 'other_bytes']]

    class ExtendedLimits(ctypes.Structure):
        _fields_ = [('basic', BasicLimits), ('counters', Counters), ('process_memory', ctypes.c_size_t),
            ('job_memory', ctypes.c_size_t), ('peak_process', ctypes.c_size_t), ('peak_job', ctypes.c_size_t)]

    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
    kernel.CreateJobObjectW.restype = wintypes.HANDLE
    kernel.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
    kernel.SetInformationJobObject.restype = wintypes.BOOL
    kernel.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
    kernel.AssignProcessToJobObject.restype = wintypes.BOOL
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    native = ctypes.WinDLL('ntdll')
    native.NtResumeProcess.argtypes = [wintypes.HANDLE]
    native.NtResumeProcess.restype = ctypes.c_long


@contextmanager
def child_process(arguments, **options):
    """Assign a suspended native child before any helper can escape its owner."""
    job, process = None, None
    try:
        if os.name == 'nt':
            job = kernel.CreateJobObjectW(None, None)
            if not job:
                raise ctypes.WinError(ctypes.get_last_error())
            limits = ExtendedLimits()
            limits.basic.flags = 0x2000  # KILL_ON_JOB_CLOSE
            if not kernel.SetInformationJobObject(job, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
                raise ctypes.WinError(ctypes.get_last_error())
            options['creationflags'] = subprocess.CREATE_NO_WINDOW | 0x4  # CREATE_SUSPENDED
        process = subprocess.Popen(arguments, **options)
        if os.name == 'nt':
            if not kernel.AssignProcessToJobObject(job, wintypes.HANDLE(int(process._handle))):
                raise ctypes.WinError(ctypes.get_last_error())
            if native.NtResumeProcess(wintypes.HANDLE(int(process._handle))) != 0:
                raise OSError('Could not resume the contained child process.')
        yield process
    finally:
        if job:
            kernel.CloseHandle(job)
        if process is not None:
            if process.poll() is None:
                process.kill()
            process.wait(timeout=5)
            for stream in (process.stdin, process.stdout, process.stderr):
                if stream and not stream.closed:
                    stream.close()


def process_run(arguments, *, input=None, capture_output=False, timeout=300, cancelled=None, **options):
    """Subprocess.run equivalent with descendant cleanup on success, failure and timeout."""
    if input is not None:
        options['stdin'] = subprocess.PIPE
    else:
        options.setdefault('stdin', subprocess.DEVNULL)
    if capture_output:
        options['stdout'] = subprocess.PIPE
        options['stderr'] = subprocess.PIPE
    with child_process(arguments, **options) as process:
        deadline = time.monotonic() + timeout
        while True:
            if cancelled is not None and cancelled.is_set():
                raise RuntimeError('Owned process cancelled after its publication lease was lost.')
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise subprocess.TimeoutExpired(arguments, timeout)
            try:
                stdout, stderr = process.communicate(input=input, timeout=min(0.5, remaining) if cancelled is not None else remaining)
                break
            except subprocess.TimeoutExpired:
                input = None
                if cancelled is None:
                    raise
        return subprocess.CompletedProcess(arguments, process.returncode, stdout, stderr)
