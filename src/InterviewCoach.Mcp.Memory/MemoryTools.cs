using System.ComponentModel;

using InterviewCoach.Memory;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace InterviewCoach.Mcp.Memory;

/// <summary>
/// Long-term memory tools. Each tool maps to one retrieval method:
/// filters for episodic lookups, sort + group input for recency questions,
/// MongoDB Vector Search on Atlas for semantic questions, and a single document for the profile.
/// </summary>
[McpServerToolType]
public class MemoryTools(
    ISessionRepository sessions,
    IFeedbackRepository feedback,
    IProfileRepository profiles,
    ILogger<MemoryTools> logger)
{
    // ---- Episodic recall (filter / sort) -----------------------------------------------------

    [McpServerTool(Name = "list_recent_interviews", Title = "List recent interviews", ReadOnly = true)]
    [Description("Episodic recall. Lists the most recent completed interviews (newest first) with company, role, date and feedback summary. Use for 'my last interviews' or 'when was my latest interview'.")]
    public async Task<IReadOnlyList<InterviewSummary>> ListRecentInterviewsAsync(
        [Description("How many interviews to return (1-50)")] int count = 5)
        => await sessions.ListRecentAsync(count);

    [McpServerTool(Name = "find_interviews_by_company", Title = "Find interviews by company", ReadOnly = true)]
    [Description("Episodic recall. Finds interviews for a company (case-insensitive, partial match) with date, role and feedback summary. Use for 'when was my interview at X' or 'summarize the feedback from X'.")]
    public async Task<IReadOnlyList<InterviewSummary>> FindInterviewsByCompanyAsync(
        [Description("Company name or part of it")] string companyName)
        => await sessions.FindByCompanyAsync(companyName);

    // ---- Semantic recall (aggregate / vector) --------------------------------------------------

    [McpServerTool(Name = "get_recent_feedback_items", Title = "Get feedback items from recent interviews", ReadOnly = true)]
    [Description("Returns individual strength/improvement items from the most recent N completed interviews, newest first. Use to find feedback that repeats across interviews ('common feedback across my last 5 interviews'): compare topics and texts and report what recurs and in how many interviews.")]
    public async Task<IReadOnlyList<FeedbackItemView>> GetRecentFeedbackItemsAsync(
        [Description("How many recent interviews to include (1-50)")] int interviewCount = 5,
        [Description("Optional filter: 'strength' or 'improvement'")] string? kind = null)
        => await feedback.GetRecentAsync(interviewCount, NormalizeKind(kind));

    [McpServerTool(Name = "search_feedback", Title = "Search feedback by meaning", ReadOnly = true)]
    [Description("Semantic recall with vector search over all feedback items. Use when the user describes a skill or trait in their own words ('which company praised my debugging?'). Results include company, role, date and similarity score; judge relevance from the text and score instead of assuming every hit matches.")]
    public async Task<IReadOnlyList<FeedbackItemView>> SearchFeedbackAsync(
        [Description("What to look for, in natural language")] string query,
        [Description("Optional filter: 'strength' or 'improvement'")] string? kind = null,
        [Description("Maximum number of results (1-20)")] int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new McpException("query must not be empty.");
        }

        return await feedback.SearchAsync(query, NormalizeKind(kind), limit);
    }

    [McpServerTool(Name = "get_user_profile", Title = "Get the user profile", ReadOnly = true)]
    [Description("Semantic memory about the user: target roles, recurring strengths and growth areas (with status and the interviews that evidence them). Returns no record for a new user.")]
    public async Task<UserProfile?> GetUserProfileAsync()
        => await profiles.GetAsync();

    // ---- Consolidation write path (summariser only) ---------------------------------------------

    [McpServerTool(Name = "save_session_feedback", Title = "Save session feedback")]
    [Description("Consolidates a finished interview into long-term memory: stores the feedback summary on the session and replaces the session's feedback items (each embedded for semantic search). Items are atomic: one strength or one improvement point each, with a short topic label and a self-contained sentence of evidence.")]
    public async Task<string> SaveSessionFeedbackAsync(
        [Description("The interview session ID")] string sessionId,
        [Description("The overall feedback summary shown to the user")] string feedbackSummary,
        [Description("Strength and improvement items extracted from the interview")] FeedbackItemInput[] items)
    {
        var session = await sessions.GetAsync(sessionId)
            ?? throw new McpException($"Interview session '{sessionId}' does not exist.");

        foreach (var item in items)
        {
            if (NormalizeKind(item.Kind) is null || string.IsNullOrWhiteSpace(item.Topic) || string.IsNullOrWhiteSpace(item.Text))
            {
                throw new McpException("Every item needs Kind ('strength' or 'improvement'), Topic and Text.");
            }
        }

        var normalized = items.Select(i => i with { Kind = NormalizeKind(i.Kind)! }).ToList();

        await sessions.SetFeedbackAsync(sessionId, feedbackSummary);
        await feedback.ReplaceForSessionAsync(session, normalized);
        logger.LogInformation("Saved {Count} feedback items for session '{Id}'", normalized.Count, sessionId);

        return $"Saved feedback summary and {normalized.Count} feedback items.";
    }

    [McpServerTool(Name = "save_user_profile", Title = "Save the user profile")]
    [Description("Updates the single user profile after an interview. Call get_user_profile first, merge the new interview into the existing topics (reuse the existing topic wording when it is the same theme), and send the complete lists. Growth area Status is 'recurring', 'improving' or 'resolved'.")]
    public async Task<UserProfile> SaveUserProfileAsync(
        [Description("Short narrative of the user's current standing")] string summary,
        [Description("Roles the user is preparing for")] string[] targetRoles,
        [Description("Strength topics with the session IDs that evidence them")] ProfileTopic[] strengths,
        [Description("Growth area topics with Status and the session IDs that evidence them")] ProfileTopic[] growthAreas)
    {
        var existing = await profiles.GetAsync() ?? new UserProfile();
        var recent = await sessions.ListRecentAsync(1);

        existing.Summary = summary;
        existing.TargetRoles = [.. targetRoles];
        existing.Strengths = [.. strengths];
        existing.GrowthAreas = [.. growthAreas];
        existing.InterviewCount = (int)await sessions.CountCompletedAsync();
        existing.LastSessionId = recent.FirstOrDefault()?.Id ?? existing.LastSessionId;

        await profiles.SaveAsync(existing);
        logger.LogInformation("Saved user profile covering {Count} interviews", existing.InterviewCount);

        return existing;
    }

    private static string? NormalizeKind(string? kind)
        => kind?.Trim().ToLowerInvariant() switch
        {
            MemoryConstants.KindStrength => MemoryConstants.KindStrength,
            MemoryConstants.KindImprovement => MemoryConstants.KindImprovement,
            _ => null,
        };
}
