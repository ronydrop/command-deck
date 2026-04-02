using CommunityToolkit.Mvvm.Input;

namespace CommandDeck.Models;

/// <summary>
/// One clickable option in the Dynamic Island (e.g. numbered answer for <see cref="AiAgentState.WaitingInput"/>).
/// </summary>
public sealed class DynamicIslandChoiceOption
{
    public string Label { get; }
    public IRelayCommand Command { get; }

    public DynamicIslandChoiceOption(string label, IRelayCommand command)
    {
        Label = label;
        Command = command;
    }
}
