using Xunit;
using InterviewCoach.Agent;
using InterviewCoach.Memory;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace InterviewCoach.Agent.Tests;

public class ShortTermMemoryTests
{
    private const string SessionId = "a1000000-0000-4000-8000-000000000001";

    private static List<ChatMessage> Conversation(params ChatMessage[] tail)
        => [new(ChatRole.System, "You are a coach."), new(ChatRole.System, $"SessionId: {SessionId}"), .. tail];

    private static async IAsyncEnumerable<AgentResponseUpdate> Stream(params (string? Author, string Text)[] parts)
    {
        foreach (var (author, text) in parts)
        {
            yield return new AgentResponseUpdate(ChatRole.Assistant, text) { AuthorName = author };
            await Task.Yield();
        }
    }

    [Fact]
    public void ExtractConversationId_UsesTheSessionIdSystemMessage()
        => Assert.Equal(SessionId, ShortTermMemory.ExtractConversationId(Conversation()));

    [Fact]
    public void ExtractConversationId_FallsBackWhenMissing()
        => Assert.Equal("unscoped", ShortTermMemory.ExtractConversationId([new(ChatRole.User, "hi")]));

    [Fact]
    public async Task RecordStreaming_SavesOnlyTheLatestUserTurnAndTheStreamedReply()
    {
        var history = new FakeHistory();
        var input = Conversation(new(ChatRole.User, "first"), new(ChatRole.Assistant, "reply"), new(ChatRole.User, "second"));

        var seen = new List<string>();
        await foreach (var update in ShortTermMemory.RecordStreamingAsync(history, NullLogger.Instance, input, Stream((null, "Hel"), (null, "lo"))))
        {
            seen.Add(update.Text);
        }

        Assert.Equal(["Hel", "lo"], seen);
        Assert.Collection(history.Turns,
            t => { Assert.Equal("user", t.Role); Assert.Equal("second", t.Text); Assert.Equal(SessionId, t.ConversationId); },
            t => { Assert.Equal("assistant", t.Role); Assert.Equal("Hello", t.Text); Assert.Equal(SessionId, t.ConversationId); });
    }

    [Fact]
    public async Task RecordStreaming_SplitsTurnsWhenTheHandoffAuthorChanges()
    {
        var history = new FakeHistory();

        await foreach (var _ in ShortTermMemory.RecordStreamingAsync(
            history, NullLogger.Instance, Conversation(new ChatMessage(ChatRole.User, "go")),
            Stream(("triage", "Routing."), ("receptionist", "Hi "), ("receptionist", "there"))))
        {
        }

        Assert.Equal(["user:", "assistant:triage", "assistant:receptionist"],
            history.Turns.Select(t => $"{t.Role}:{t.AuthorName}"));
        Assert.Equal("Hi there", history.Turns[^1].Text);
    }

    [Fact]
    public async Task RecordStreaming_FallsBackToAgentIdWhenAuthorNameIsEmpty()
    {
        var history = new FakeHistory();

        static async IAsyncEnumerable<AgentResponseUpdate> Updates()
        {
            yield return new AgentResponseUpdate(ChatRole.Assistant, "Routing.") { AgentId = "triage" };
            yield return new AgentResponseUpdate(ChatRole.Assistant, "Answer.") { AgentId = "memory_coach" };
            await Task.Yield();
        }

        await foreach (var _ in ShortTermMemory.RecordStreamingAsync(
            history, NullLogger.Instance, Conversation(new ChatMessage(ChatRole.User, "go")), Updates()))
        {
        }

        Assert.Equal(["user:", "assistant:triage", "assistant:memory_coach"],
            history.Turns.Select(t => $"{t.Role}:{t.AuthorName}"));
    }

    [Fact]
    public async Task RecordStreaming_KeepsTheChatAliveWhenMongoFails()
    {
        var history = new FakeHistory { Fail = true };

        var seen = new List<string>();
        await foreach (var update in ShortTermMemory.RecordStreamingAsync(
            history, NullLogger.Instance, Conversation(new ChatMessage(ChatRole.User, "hi")), Stream((null, "ok"))))
        {
            seen.Add(update.Text);
        }

        Assert.Equal(["ok"], seen);
    }

    private sealed class FakeHistory : IChatHistoryRepository
    {
        public List<ChatTurn> Turns { get; } = [];
        public bool Fail { get; set; }

        public Task AppendAsync(ChatTurn turn, CancellationToken ct = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("mongo down");
            }

            Turns.Add(turn);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ChatTurn>> GetAsync(string conversationId, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChatTurn>>(Turns);
    }
}
