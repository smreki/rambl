namespace Rambl.Components;

/// <summary>
/// The first item of the dictation shortcut's group in the Post Processing list. It says that the processes below share the
/// dictation shortcut, so they are the default post-processing and run after every dictation, in the order shown. It is not a
/// process, so it cannot be dragged, opened or deleted.
/// </summary>
public partial class DictationStep : ContentView
{
    /// <summary>Creates the step.</summary>
    public DictationStep() => InitializeComponent();
}
