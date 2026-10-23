using System.Diagnostics;

namespace Rambl.Features.LanguageModels.LlamaCpp;

/// <summary>Makes sure a child process does not outlive Rambl, even when Rambl is killed or crashes.</summary>
/// <remarks>
/// The model server holds gigabytes of memory, so an orphaned one would be a real cost. Windows provides this with a
/// job object; a platform without an implementation relies on Rambl stopping the process itself.
/// </remarks>
public interface IChildProcessGuard
{
    /// <summary>Ties a started process to the lifetime of Rambl.</summary>
    /// <param name="process">The process, which has just been started.</param>
    void Attach(Process process);
}
