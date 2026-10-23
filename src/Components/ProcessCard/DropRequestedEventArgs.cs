namespace Rambl.Components;

/// <summary>Says which card was dropped onto another card and on which side.</summary>
/// <param name="draggedKey">The <see cref="ProcessCard.DragKey"/> of the card that was dragged.</param>
/// <param name="insertAfter">Whether it was dropped on the lower half of the target card, so it goes after it instead of before it.</param>
public sealed class DropRequestedEventArgs(string draggedKey, bool insertAfter) : EventArgs
{
    /// <summary>Gets the key of the card that was dragged.</summary>
    public string DraggedKey { get; } = draggedKey;

    /// <summary>Gets whether the dragged card goes after the card it was dropped on.</summary>
    public bool InsertAfter { get; } = insertAfter;
}
