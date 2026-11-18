using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Rambl.Platforms.Windows.Meetings;

/// <summary>The processes running at one moment, with their parents, for finding the process that stands for an app.</summary>
/// <remarks>
/// Taken from a system snapshot, which needs no access to the processes themselves, so protected and elevated processes
/// are listed too. Nothing here throws for a process that cannot be opened.
/// </remarks>
internal sealed class ProcessSnapshot
{
    private const uint SnapProcess = 0x2;
    private const uint QueryLimitedInformation = 0x1000;
    private const int MaxDepth = 64;
    private static readonly nint InvalidHandle = -1;
    private static readonly ConcurrentDictionary<string, string?> Descriptions = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<uint, (uint Parent, string Name)> processes;

    private ProcessSnapshot(Dictionary<uint, (uint Parent, string Name)> processes) => this.processes = processes;

    /// <summary>Lists the processes running now.</summary>
    /// <returns>The snapshot, which is empty when Windows refuses one.</returns>
    public static ProcessSnapshot Take()
    {
        var processes = new Dictionary<uint, (uint Parent, string Name)>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == InvalidHandle)
            return new(processes);

        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    if (entry.ProcessId != 0)
                        processes[entry.ProcessId] = (entry.ParentProcessId, StripExe(entry.ExeFile));
                } while (Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            _ = CloseHandle(snapshot);
        }

        return new(processes);
    }

    /// <summary>Gets the name of a process.</summary>
    /// <param name="processId">The process id.</param>
    /// <returns>The process name without <c>.exe</c>, or null when the process is not in the snapshot.</returns>
    public string? NameOf(uint processId) => processes.TryGetValue(processId, out var process) ? process.Name : null;

    /// <summary>Finds the process to capture so that its process tree covers the whole app.</summary>
    /// <param name="processId">A process of the app, for example the helper process that plays a browser's sound.</param>
    /// <returns>The topmost ancestor with the same process name, or <paramref name="processId"/> itself.</returns>
    public uint RootOf(uint processId)
    {
        if (!processes.TryGetValue(processId, out var process))
            return processId;

        var root = processId;
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            if (
                process.Parent == 0
                || process.Parent == root
                || !processes.TryGetValue(process.Parent, out var parent)
                || !string.Equals(parent.Name, process.Name, StringComparison.OrdinalIgnoreCase)
            )
                break;

            root = process.Parent;
            process = parent;
        }

        return root;
    }

    /// <summary>Finds a running app by its process name.</summary>
    /// <param name="processName">The process name without <c>.exe</c>.</param>
    /// <returns>The id of the app's root process, or null when no process has that name.</returns>
    public uint? FindRoot(string processName)
    {
        uint? found = null;
        foreach (var (id, process) in processes)
        {
            if (!string.Equals(process.Name, processName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Several instances can run side by side; the lowest root id keeps the answer the same from call to call.
            var root = RootOf(id);
            if (found is null || root < found)
                found = root;
        }

        return found;
    }

    /// <summary>Gets the name to show for a process.</summary>
    /// <param name="processId">The process id.</param>
    /// <returns>The description in the program file, for example <c>Zoom Workplace</c>; otherwise the process name.</returns>
    public string DisplayNameOf(uint processId)
    {
        var name = NameOf(processId) ?? string.Empty;
        return ImagePath(processId) is { } path && Descriptions.GetOrAdd(path, ReadDescription) is { } description
            ? description
            : name;
    }

    private static string? ReadDescription(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            return string.IsNullOrEmpty(description) ? null : description;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? ImagePath(uint processId)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, processId);
        if (handle == 0)
            return null;

        try
        {
            var buffer = new char[1024];
            var length = (uint)buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref length) ? new string(buffer, 0, (int)length) : null;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    private static string StripExe(string file) =>
        file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll")]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, [Out] char[] name, ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
