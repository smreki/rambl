using Rambl.Common.Input;

namespace Rambl.Features.VoiceCommands;

/// <summary>One command of a command set: what the decision model reads, and the keys Rambl presses when it is picked.</summary>
/// <param name="Name">The name, unique in its set; the model reads it.</param>
/// <param name="Description">How a person would ask for it, ideally a few ways of saying it; the model reads it. May be empty.</param>
/// <param name="Keys">The key presses, in the order they are sent; each is a key with its modifiers, such as Ctrl+A.</param>
public sealed record VoiceCommand(string Name, string Description, IReadOnlyList<Shortcut> Keys)
{
    /// <summary>Gets the text the model reads for this command, such as <c>Select all: select everything, highlight it all</c>.</summary>
    public string OptionText => string.IsNullOrWhiteSpace(Description) ? Name : $"{Name}: {Description.Trim()}";

    /// <summary>Gets the key presses as text, such as <c>Home → Shift+End → Delete</c>.</summary>
    public string KeysText => DescribeKeys(Keys);

    /// <summary>Writes key presses as text, presses separated by arrows.</summary>
    /// <param name="keys">The presses.</param>
    /// <returns>For example <c>Home → Shift+End → Delete</c>; empty for none.</returns>
    public static string DescribeKeys(IEnumerable<Shortcut> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return string.Join(" → ", keys.Select(key => key.ToString()));
    }
}
