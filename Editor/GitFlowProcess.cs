// IMPORTANT: Follow the manifest-selected Geurts Technical and Folder Structure techniques.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Windows-only bounded Git transport with suspended launch, inherited console suppression and job-owned descendant cleanup.</summary>
    internal static class GitFlowProcess
    {
        [StructLayout(LayoutKind.Sequential)] private struct Security { public int length; public IntPtr descriptor; [MarshalAs(UnmanagedType.Bool)] public bool inherit; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Startup
        {
            public int size; public string reserved, desktop, title; public int x, y, width, height, xCount, yCount, fill, flags;
            public short show, reservedSize; public IntPtr reservedBytes, input, output, error;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Child { public IntPtr process, thread; public int pid, tid; }
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimit
        { public long processTime, jobTime; public uint flags; public UIntPtr minWorking, maxWorking; public uint activeProcesses; public UIntPtr affinity; public uint priority, scheduling; }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong reads, writes, others, readBytes, writeBytes, otherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimit
        { public BasicLimit basic; public IoCounters io; public UIntPtr processMemory, jobMemory, peakProcess, peakJob; }

        /// <summary>Creates no console, assigns the still-suspended process to a kill-on-close job and reads bounded pipes.</summary>
        internal static string Run(ProcessStartInfo start, CancellationToken cancellation)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) throw new PlatformNotSupportedException("GitFlow inspection supports Windows only.");
            IntPtr job = IntPtr.Zero, outputRead = IntPtr.Zero, outputWrite = IntPtr.Zero, errorRead = IntPtr.Zero, errorWrite = IntPtr.Zero;
            IntPtr inputRead = IntPtr.Zero, inputWrite = IntPtr.Zero, environment = IntPtr.Zero; Child child = default;
            try
            {
                var security = new Security { length = Marshal.SizeOf<Security>(), inherit = true };
                Require(CreatePipe(out outputRead, out outputWrite, ref security, 0)); Require(SetHandleInformation(outputRead, 1, 0));
                Require(CreatePipe(out errorRead, out errorWrite, ref security, 0)); Require(SetHandleInformation(errorRead, 1, 0));
                Require(CreatePipe(out inputRead, out inputWrite, ref security, 0)); Close(ref inputWrite);
                job = CreateJobObject(IntPtr.Zero, null); Require(job != IntPtr.Zero);
                var limits = new ExtendedLimit { basic = new BasicLimit { flags = 0x2000 } }; // KILL_ON_JOB_CLOSE owns all Git/helper descendants.
                Require(SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<ExtendedLimit>()));
                string executable = ResolveGit();
                var startup = new Startup { size = Marshal.SizeOf<Startup>(), flags = 0x100, input = inputRead, output = outputWrite, error = errorWrite };
                string block = string.Join("\0", start.EnvironmentVariables.Keys.Cast<string>().OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Select(key => key + "=" + start.EnvironmentVariables[key])) + "\0\0";
                environment = Marshal.StringToHGlobalUni(block);
                // No inherited console, no shell and no DETACHED_PROCESS/CREATE_NEW_CONSOLE combination.
                Require(CreateProcess(executable, new StringBuilder("\"" + executable + "\" " + start.Arguments), IntPtr.Zero, IntPtr.Zero,
                    true, 0x08000000 | 0x00000004 | 0x00000400, environment, start.WorkingDirectory, ref startup, out child));
                Require(AssignProcessToJobObject(job, child.process)); // Assign before any Git/helper code can execute.
                Close(ref outputWrite); Close(ref errorWrite); Close(ref inputRead);
                using (var output = new StreamReader(new FileStream(new SafeFileHandle(outputRead, true), FileAccess.Read), new UTF8Encoding(false, true)))
                using (var error = new StreamReader(new FileStream(new SafeFileHandle(errorRead, true), FileAccess.Read), new UTF8Encoding(false, true)))
                {
                    outputRead = errorRead = IntPtr.Zero;
                    Task<string> stdout = ReadBounded(output, cancellation), stderr = ReadBounded(error, cancellation);
                    try
                    {
                        Require(ResumeThread(child.thread) != uint.MaxValue);
                        while (WaitForSingleObject(child.process, 25) == 0x102)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (stdout.IsFaulted || stderr.IsFaulted) throw new InvalidDataException("Git output failed decoding or exceeded one MiB.");
                        }
                        Require(GetExitCodeProcess(child.process, out uint code));
                        Close(ref job); // Kill surviving helpers before EOF; a pipe cannot outlive the refresh budget.
                        string text = stdout.GetAwaiter().GetResult(), failure = stderr.GetAwaiter().GetResult();
                        if (code != 0) throw new IOException("Git failed (exit " + code + "). " + failure.Substring(0, Math.Min(failure.Length, 2000)));
                        return text;
                    }
                    finally
                    {
                        Close(ref job); // Terminate descendants before disposing pipes during cancellation/failure.
                        try { Task.WhenAll(stdout, stderr).GetAwaiter().GetResult(); } catch { /* The owning operation retains the original failure. */ }
                    }
                }
            }
            finally
            {
                // A suspended child is also terminated if job assignment fails. No retry or escaped publisher is permitted.
                if (child.process != IntPtr.Zero) TerminateProcess(child.process, 1);
                Close(ref job); Close(ref child.thread); Close(ref child.process);
                Close(ref outputRead); Close(ref outputWrite); Close(ref errorRead); Close(ref errorWrite); Close(ref inputRead); Close(ref inputWrite);
                if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
            }
        }

        /// <summary>Resolves only a native Git executable, without shell/PATHEXT script dispatch.</summary>
        private static string ResolveGit()
        {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                string path = Path.Combine(directory.Trim('"'), "git.exe");
                if (File.Exists(path)) return Path.GetFullPath(path);
            }
            throw new FileNotFoundException("Git for Windows is unavailable on PATH. Install it through its supported distribution.");
        }

        /// <summary>Reads redirected bytes with strict UTF-8 and a one-MiB bound.</summary>
        private static async Task<string> ReadBounded(StreamReader reader, CancellationToken cancellation)
        {
            var value = new StringBuilder(); var buffer = new char[4096]; long bytes = 0; int count;
            while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            { cancellation.ThrowIfCancellationRequested(); bytes += Encoding.UTF8.GetByteCount(buffer, 0, count); if (bytes > GitFlowInspection.MaximumJsonBytes) throw new InvalidDataException("Git output exceeds one MiB."); value.Append(buffer, 0, count); }
            return value.ToString();
        }

        /// <summary>Raises the actual Windows failure without hiding unsupported process isolation.</summary>
        private static void Require(bool result) { if (!result) throw new Win32Exception(Marshal.GetLastWin32Error()); }
        /// <summary>Closes one owned native handle at most once.</summary>
        private static void Close(ref IntPtr handle) { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }

        /// <summary>Creates one inheritable anonymous pipe.</summary>
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref Security security, int size);
        /// <summary>Removes inheritance from a parent-only read handle.</summary>
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
        /// <summary>Creates the inspection's anonymous process job.</summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        /// <summary>Configures descendant cleanup before launch.</summary>
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int information, ref ExtendedLimit limits, int size);
        /// <summary>Starts a native process suspended with explicit redirected handles.</summary>
        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref Startup startup, out Child child);
        /// <summary>Contains the suspended process before it can create descendants.</summary>
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        /// <summary>Resumes the job-contained process exactly once.</summary>
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
        /// <summary>Waits a bounded interval on the process handle.</summary>
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        /// <summary>Reads the completed process outcome.</summary>
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        /// <summary>Terminates an owned suspended or cancelled process during cleanup.</summary>
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr process, uint code);
        /// <summary>Releases an owned native handle.</summary>
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
