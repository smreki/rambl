using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Rambl.Common.Interop;

/// <summary>Resolves native imports from explicit paths through one resolver per importing assembly.</summary>
internal static class NativeLibraryImports
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Assembly, ConcurrentDictionary<string, Lazy<nint>>> Libraries = [];

    /// <summary>Registers a library, retaining its module for the lifetime of the app.</summary>
    /// <param name="name">The name used by the managed imports.</param>
    /// <param name="path">The absolute path to its native library.</param>
    /// <param name="assembly">The importing assembly, or null for Rambl's own imports.</param>
    public static void Register(string name, string path, Assembly? assembly = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        assembly ??= typeof(NativeLibraryImports).Assembly;
        lock (Gate)
        {
            if (!Libraries.TryGetValue(assembly, out var imports))
            {
                imports = new(StringComparer.Ordinal);
                NativeLibrary.SetDllImportResolver(assembly, (library, _, _) =>
                    imports.TryGetValue(library, out var handle) ? handle.Value : 0);
                Libraries.Add(assembly, imports);
            }
            _ = imports.TryAdd(name, new Lazy<nint>(() => NativeLibrary.Load(path)));
        }
    }
}
