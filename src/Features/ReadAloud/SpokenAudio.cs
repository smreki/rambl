using System.Runtime.InteropServices;
using Rambl.Features.Tts;

namespace Rambl.Features.ReadAloud;

/// <summary>What each sentence of one text sounded like, kept in a temporary file so a long text does not fill the memory.</summary>
/// <remarks>A sentence spoken again replaces what was kept for it. It can be used from any thread; the file is removed on <see cref="Dispose"/>.</remarks>
internal sealed class SpokenAudio : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<int, Take> takes = [];
    private FileStream? file;
    private bool disposed;

    /// <summary>Keeps the audio of a sentence.</summary>
    /// <param name="sentence">The sentence's index in the text.</param>
    /// <param name="audio">What it sounded like.</param>
    /// <exception cref="IOException">The temporary file cannot be written, for example because the disk is full.</exception>
    public void Add(int sentence, TtsAudio audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            file ??= new FileStream(
                Path.Combine(Path.GetTempPath(), $"rambl-reading-{Guid.NewGuid():N}.tmp"),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                64 * 1024,
                FileOptions.DeleteOnClose
            );
            var offset = file.Seek(0, SeekOrigin.End);
            file.Write(MemoryMarshal.AsBytes(audio.Samples.AsSpan()));
            takes[sentence] = new(offset, audio.Samples.Length, audio.SampleRate);
        }
    }

    /// <summary>Reads back the audio kept for a sentence.</summary>
    /// <param name="sentence">The sentence's index in the text.</param>
    /// <returns>The audio, or null when the sentence was not spoken.</returns>
    /// <exception cref="IOException">The temporary file cannot be read.</exception>
    public TtsAudio? Read(int sentence)
    {
        lock (gate)
        {
            if (file is null || !takes.TryGetValue(sentence, out var take))
                return null;

            var samples = new float[take.Length];
            _ = file.Seek(take.Offset, SeekOrigin.Begin);
            file.ReadExactly(MemoryMarshal.AsBytes(samples.AsSpan()));
            return new(samples, take.SampleRate);
        }
    }

    /// <summary>Removes the temporary file.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            file?.Dispose();
            file = null;
            takes.Clear();
        }
    }

    private readonly record struct Take(long Offset, int Length, int SampleRate);
}
