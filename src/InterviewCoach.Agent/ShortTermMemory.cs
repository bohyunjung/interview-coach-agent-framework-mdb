using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

using InterviewCoach.Memory;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace InterviewCoach.Agent;

/// <summary>
/// Short-term memory: stores every user and agent turn in MongoDB without involving the LLM.
/// It wraps the outermost agent, so it sees the same traffic in single and handoff mode and
/// does not depend on how the Copilot CLI manages its own conversation state.
/// </summary>
internal static partial class ShortTermMemory
{
    private const string UnscopedConversation = "unscoped";

    public static AIAgent WithShortTermMemory(this AIAgent agent, IServiceProvider services)
    {
        var history = services.GetRequiredService<IChatHistoryRepository>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ShortTermMemory));

        return new AIAgentBuilder(agent)
            .Use(
                runFunc: async (messages, session, options, inner, cancellationToken) =>
                {
                    var input = messages.ToList();
                    var conversationId = await RecordUserTurnAsync(history, logger, input, cancellationToken);
                    var response = await inner.RunAsync(input, session, options, cancellationToken);
                    await SaveAsync(history, logger, new ChatTurn
                    {
                        ConversationId = conversationId,
                        Role = ChatRole.Assistant.Value,
                        AuthorName = response.Messages.LastOrDefault()?.AuthorName ?? response.AgentId,
                        Text = response.Text,
                    }, cancellationToken);

                    return response;
                },
                runStreamingFunc: (messages, session, options, inner, cancellationToken) =>
                    RecordStreamingAsync(history, logger, messages.ToList(), inner.RunStreamingAsync(messages, session, options, cancellationToken), cancellationToken))
            .Build();
    }

    internal static async IAsyncEnumerable<AgentResponseUpdate> RecordStreamingAsync(
        IChatHistoryRepository history,
        ILogger logger,
        IReadOnlyList<ChatMessage> input,
        IAsyncEnumerable<AgentResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var conversationId = await RecordUserTurnAsync(history, logger, input, cancellationToken);

        // In handoff mode several agents speak in one response, so each author becomes its own turn.
        var buffer = new StringBuilder();
        string? author = null;

        async Task FlushAsync()
        {
            if (buffer.Length > 0)
            {
                await SaveAsync(history, logger, new ChatTurn
                {
                    ConversationId = conversationId,
                    Role = ChatRole.Assistant.Value,
                    AuthorName = author,
                    Text = buffer.ToString(),
                }, CancellationToken.None);
                buffer.Clear();
            }
        }

        try
        {
            await foreach (var update in updates.WithCancellation(cancellationToken))
            {
                if (update.Role != ChatRole.User && !string.IsNullOrEmpty(update.Text))
                {
                    // Workflow updates carry the speaking agent in AgentId; AuthorName is usually empty.
                    var speaker = update.AuthorName ?? update.AgentId;
                    if (!string.Equals(author, speaker, StringComparison.Ordinal))
                    {
                        await FlushAsync();
                        author = speaker;
                    }

                    buffer.Append(update.Text);
                }

                yield return update;
            }
        }
        finally
        {
            // Keep whatever was streamed, even when the client cancels or the run fails midway.
            await FlushAsync();
        }
    }

    internal static async Task<string> RecordUserTurnAsync(
        IChatHistoryRepository history, ILogger logger, IReadOnlyList<ChatMessage> input, CancellationToken cancellationToken)
    {
        var conversationId = ExtractConversationId(input);

        // The UI resends the whole history on every request; only the latest user message is new.
        var latest = input.LastOrDefault(m => m.Role == ChatRole.User);
        if (latest is not null && !string.IsNullOrWhiteSpace(latest.Text))
        {
            await SaveAsync(history, logger, new ChatTurn
            {
                ConversationId = conversationId,
                Role = ChatRole.User.Value,
                Text = latest.Text,
            }, cancellationToken);
        }

        return conversationId;
    }

    // The WebUI announces the interview session in a system message ("SessionId: <guid>"),
    // so chat turns are keyed by the same ID as the episodic session record.
    internal static string ExtractConversationId(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages.Where(m => m.Role == ChatRole.System))
        {
            var match = SessionIdPattern().Match(message.Text ?? string.Empty);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return UnscopedConversation;
    }

    private static async Task SaveAsync(IChatHistoryRepository history, ILogger logger, ChatTurn turn, CancellationToken cancellationToken)
    {
        try
        {
            await history.AppendAsync(turn, cancellationToken);
        }
        catch (Exception ex)
        {
            // Memory must never break the conversation.
            logger.LogWarning(ex, "Could not save a chat turn for conversation {ConversationId}", turn.ConversationId);
        }
    }

    [GeneratedRegex(@"SessionId:\s*([0-9a-fA-F-]{36})")]
    private static partial Regex SessionIdPattern();
}
