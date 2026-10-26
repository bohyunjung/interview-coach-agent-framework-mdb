using InterviewCoach.Memory;

namespace InterviewCoach.Agent;

/// <summary>A chat message as the UI shows it when a conversation is restored.</summary>
public record ChatHistoryMessage(string Role, string Text, DateTime CreatedAt);

internal static class ChatHistoryView
{
    /// <summary>
    /// In handoff mode one answer is stored as several assistant turns (one per agent).
    /// The UI shows it as a single reply, so consecutive assistant turns are merged back.
    /// </summary>
    public static IReadOnlyList<ChatHistoryMessage> Collapse(IEnumerable<ChatTurn> turns)
    {
        var result = new List<ChatHistoryMessage>();
        foreach (var turn in turns)
        {
            if (string.IsNullOrWhiteSpace(turn.Text))
            {
                continue;
            }

            if (turn.Role == "assistant" && result.Count > 0 && result[^1].Role == "assistant")
            {
                var last = result[^1];
                result[^1] = last with { Text = $"{last.Text}\n\n{turn.Text}" };
            }
            else
            {
                result.Add(new ChatHistoryMessage(turn.Role, turn.Text, turn.CreatedAt));
            }
        }

        return result;
    }
}
