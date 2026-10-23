using Rambl.Common.Input;

namespace Rambl.Components;

/// <summary>One command of a voice command set on its edit screen: its name and description, its keys, and Edit and Delete.</summary>
/// <remarks>It holds plain values and knows nothing about command sets.</remarks>
public partial class CommandRow : ContentView
{
    private string nameText = string.Empty;
    private string descriptionText = string.Empty;
    private IReadOnlyList<Shortcut> presses = [];

    /// <summary>Creates the row.</summary>
    public CommandRow() => InitializeComponent();

    /// <summary>Occurs when Edit is clicked.</summary>
    public event EventHandler? EditRequested;

    /// <summary>Occurs when Delete is clicked.</summary>
    public event EventHandler? DeleteRequested;

    /// <summary>Gets or sets the command's name.</summary>
    public string NameText
    {
        get => nameText;
        set
        {
            nameText = value ?? string.Empty;
            Refresh();
        }
    }

    /// <summary>Gets or sets what the model reads about the command; empty shows a muted note.</summary>
    public string DescriptionText
    {
        get => descriptionText;
        set
        {
            descriptionText = value ?? string.Empty;
            Refresh();
        }
    }

    /// <summary>Gets or sets the key presses, in order.</summary>
    public IReadOnlyList<Shortcut> Presses
    {
        get => presses;
        set
        {
            presses = value ?? [];
            Refresh();
        }
    }

    private void Refresh()
    {
        NameLabel.Text = NameText;
        DescriptionLabel.Text = DescriptionText.Length > 0 ? DescriptionText : "No description: the model only reads the name.";
        DescriptionLabel.FontAttributes = DescriptionText.Length > 0 ? FontAttributes.None : FontAttributes.Italic;
        Caps.Presses = Presses;
        EditButton.AccessibleName = $"Edit {NameText}";
        DeleteButton.AccessibleName = $"Delete {NameText}";
    }

    private void OnEditClicked(object? sender, EventArgs e) => EditRequested?.Invoke(this, EventArgs.Empty);

    private void OnDeleteClicked(object? sender, EventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
}
