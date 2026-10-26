using InterviewCoach.Agent;
using InterviewCoach.Memory;

using Xunit;

namespace InterviewCoach.Agent.Tests;

public class ChatHistoryViewTests
{
    private static ChatTurn Turn(string role, string text, string? author = null)
        => new() { Role = role, Text = text, AuthorName = author };

    [Fact]
    public void Collapse_MergesConsecutiveAssistantTurnsFromAHandoff()
    {
        var view = ChatHistoryView.Collapse(
        [
            Turn("user", "start"),
            Turn("assistant", "Routing.", "triage"),
            Turn("assistant", "Hello!", "receptionist"),
            Turn("user", "answer"),
            Turn("assistant", "Next question.", "behavioral"),
        ]);

        Assert.Equal(["user:start", "assistant:Routing.\n\nHello!", "user:answer", "assistant:Next question."],
            view.Select(m => $"{m.Role}:{m.Text}"));
    }

    [Fact]
    public void Collapse_SkipsEmptyTurns()
        => Assert.Single(ChatHistoryView.Collapse([Turn("assistant", "  "), Turn("user", "hi")]));
}
