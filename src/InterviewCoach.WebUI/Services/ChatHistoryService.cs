using System.Net.Http.Json;

using Microsoft.Extensions.AI;

namespace InterviewCoach.WebUI.Services;

/// <summary>Loads the stored turns of a conversation from the Agent API so a chat survives a refresh.</summary>
public sealed class ChatHistoryService(IHttpClientFactory httpClientFactory, ILogger<ChatHistoryService> logger)
{
    public async Task<IReadOnlyList<ChatMessage>> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("agent");
            var stored = await client.GetFromJsonAsync<StoredMessage[]>($"chat/{sessionId:D}/messages", cancellationToken) ?? [];

            return [.. stored.Select(m => new ChatMessage(m.Role == "user" ? ChatRole.User : ChatRole.Assistant, m.Text))];
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            // A failed restore must not block a fresh conversation.
            logger.LogError(ex, "Could not restore chat history for session {SessionId}", sessionId);
            return [];
        }
    }

    private sealed record StoredMessage(string Role, string Text);
}
