namespace Rambl.Features.Dictation;

/// <summary>Stands in on platforms that cannot copy or paste dictated text yet: the text is left alone.</summary>
public sealed class NullTextInserter : ITextInserter
{
    /// <inheritdoc/>
    public Task InsertAsync(string text, bool paste, CancellationToken cancellationToken) => Task.CompletedTask;
}
