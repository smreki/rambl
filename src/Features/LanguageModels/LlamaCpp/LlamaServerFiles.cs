namespace Rambl.Features.LanguageModels.LlamaCpp;

/// <summary>Finds the llama.cpp server that is shipped beside the app.</summary>
/// <remarks>
/// The build puts the files of one pinned llama.cpp release into a <c>llama.cpp</c> folder next to the executable (see
/// <c>LlamaCppVersion</c> in <c>Rambl.csproj</c>).
/// </remarks>
internal static class LlamaServerFiles
{
    /// <summary>The folder next to the executable that holds the release's files.</summary>
    internal const string FolderName = "llama.cpp";

    /// <summary>Gets the path where the server executable is expected.</summary>
    internal static string ServerPath =>
        Path.Combine(AppContext.BaseDirectory, FolderName, OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server");

    /// <summary>Checks that the server is there.</summary>
    /// <param name="reason">When it is not, why, for the user.</param>
    /// <returns>Whether the server can be started.</returns>
    internal static bool IsAvailable(out string reason)
    {
        if (File.Exists(ServerPath))
        {
            reason = string.Empty;
            return true;
        }

        reason = "The llama.cpp server is missing from this installation.";
        return false;
    }
}
