namespace Rambl.Components;

/// <summary>One pre-processing step of a read-aloud profile: provider, model, time limit and instruction.</summary>
/// <remarks>
/// The model select only reports a click (<see cref="ModelSelectClicked"/>): the screen owns the menu, which needs room
/// outside the card. The two Insert buttons put a variable at the caret.
/// </remarks>
public partial class StepEditor : ContentView
{
    private const string OriginalVariable = "{{original}}";
    private const string ProcessedVariable = "{{processed}}";

    private bool settingTimeouts;
    private bool settingInstruction;

    /// <summary>Creates an empty step.</summary>
    public StepEditor()
    {
        InitializeComponent();
        ProviderSelect.SetItems(["Local"], 0);
    }

    /// <summary>Occurs when Move up is used.</summary>
    public event EventHandler? MoveUpClicked;

    /// <summary>Occurs when Move down is used.</summary>
    public event EventHandler? MoveDownClicked;

    /// <summary>Occurs when Remove is used.</summary>
    public event EventHandler? RemoveClicked;

    /// <summary>Occurs when the model select is clicked, so the screen can open its menu at <see cref="ModelAnchor"/>.</summary>
    public event EventHandler? ModelSelectClicked;

    /// <summary>Occurs when another time limit is picked, with its index; not when <see cref="SetTimeoutOptions"/> sets one.</summary>
    public event EventHandler<int>? TimeoutChanged;

    /// <summary>Occurs when the instruction is edited.</summary>
    public event EventHandler? InstructionChanged;

    /// <summary>Gets the model select, for placing its menu.</summary>
    public View ModelAnchor => ModelSelect;

    /// <summary>Gets or sets the step's number, from 1; it is also its title.</summary>
    public int Number
    {
        get => int.TryParse(NumberLabel.Text, out var number) ? number : 0;
        set
        {
            NumberLabel.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            TitleLabel.Text = $"Step {value}";
            MoveUpButton.AccessibleName = $"Move step {value} up";
            MoveDownButton.AccessibleName = $"Move step {value} down";
            RemoveButton.AccessibleName = $"Remove step {value}";
        }
    }

    /// <summary>Gets or sets whether the step can move up.</summary>
    public bool CanMoveUp
    {
        get => MoveUpButton.IsEnabled;
        set
        {
            MoveUpButton.IsEnabled = value;
            MoveUpButton.Opacity = value ? 1 : 0.45;
        }
    }

    /// <summary>Gets or sets whether the step can move down.</summary>
    public bool CanMoveDown
    {
        get => MoveDownButton.IsEnabled;
        set
        {
            MoveDownButton.IsEnabled = value;
            MoveDownButton.Opacity = value ? 1 : 0.45;
        }
    }

    /// <summary>Gets or sets the instruction.</summary>
    public string Instruction
    {
        get => InstructionEditor.Text;
        set
        {
            settingInstruction = true;
            InstructionEditor.Text = value;
            settingInstruction = false;
        }
    }

    /// <summary>Shows the chosen model.</summary>
    /// <param name="name">Its name, or "Choose a model".</param>
    /// <param name="detail">The muted line under it, or empty for none.</param>
    /// <param name="help">The text beside the select, such as why the model cannot run.</param>
    public void ShowModel(string name, string detail, string help)
    {
        ModelNameLabel.Text = name;
        ModelDetailLabel.Text = detail;
        ModelDetailLabel.IsVisible = detail.Length > 0;
        ModelHelpLabel.Text = help;
        SemanticProperties.SetDescription(ModelSelectButton, $"Model of step {Number}: {name}");
    }

    /// <summary>Fills the time limit select.</summary>
    /// <param name="labels">The time limits as shown.</param>
    /// <param name="selected">The index of the one in use.</param>
    public void SetTimeoutOptions(IReadOnlyList<string> labels, int selected)
    {
        settingTimeouts = true;
        TimeoutSelect.SetItems(labels, selected);
        settingTimeouts = false;
    }

    private void OnTimeoutChanged(object? sender, EventArgs e)
    {
        if (!settingTimeouts && TimeoutSelect.SelectedIndex >= 0)
            TimeoutChanged?.Invoke(this, TimeoutSelect.SelectedIndex);
    }

    private void OnInstructionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!settingInstruction && e.PropertyName == nameof(TextArea.Text))
            InstructionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnInsertOriginalClicked(object? sender, EventArgs e) => InstructionEditor.InsertText(OriginalVariable);

    private void OnInsertProcessedClicked(object? sender, EventArgs e) => InstructionEditor.InsertText(ProcessedVariable);

    private void OnMoveUpClicked(object? sender, EventArgs e) => MoveUpClicked?.Invoke(this, EventArgs.Empty);

    private void OnMoveDownClicked(object? sender, EventArgs e) => MoveDownClicked?.Invoke(this, EventArgs.Empty);

    private void OnRemoveClicked(object? sender, EventArgs e) => RemoveClicked?.Invoke(this, EventArgs.Empty);

    private void OnModelSelectClicked(object? sender, EventArgs e) => ModelSelectClicked?.Invoke(this, EventArgs.Empty);
}
