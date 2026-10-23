namespace Rambl.Features.Models;

/// <summary>The runtimes this build of Rambl can actually run, for one catalog.</summary>
/// <remarks>The catalog lists more runtimes than exist yet; a model for one that is not here can be downloaded but not used.</remarks>
public sealed class ModelRuntimeRegistry
{
    private readonly Dictionary<string, IModelRuntime> runtimes;

    /// <summary>Creates the registry.</summary>
    /// <param name="runtimes">The available runtimes; none on a platform without a runtime for this catalog.</param>
    /// <exception cref="ArgumentNullException"><paramref name="runtimes"/> is null.</exception>
    public ModelRuntimeRegistry(IEnumerable<IModelRuntime> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        this.runtimes = runtimes.ToDictionary(runtime => runtime.Id, StringComparer.Ordinal);
    }

    /// <summary>Finds an available runtime.</summary>
    /// <param name="runtimeId">The runtime's catalog id.</param>
    /// <returns>The runtime, or null when this build does not have it.</returns>
    public IModelRuntime? Find(string runtimeId) => runtimes.GetValueOrDefault(runtimeId);

    /// <summary>Finds an available runtime of one kind.</summary>
    /// <typeparam name="T">The kind of runtime, for example <c>ISttRuntime</c>.</typeparam>
    /// <param name="runtimeId">The runtime's catalog id.</param>
    /// <returns>The runtime, or null when this build does not have it or it is another kind.</returns>
    public T? Find<T>(string runtimeId)
        where T : class, IModelRuntime => Find(runtimeId) as T;
}
