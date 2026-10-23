namespace Rambl.Features.Decisions;

/// <summary>A multiple-choice question for a decision model: which of the options fits the state.</summary>
/// <param name="Instruction">The question, such as "Which command did the user ask for?".</param>
/// <param name="Options">The answers to choose from, each as text the model reads; at least two.</param>
/// <param name="State">What the question is about, such as what the user said.</param>
public sealed record DecisionQuestion(string Instruction, IReadOnlyList<string> Options, string State);
