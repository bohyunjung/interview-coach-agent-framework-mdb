using Microsoft.Extensions.AI;

namespace InterviewCoach.Agent;

/// <summary>Splits the Memory MCP server's tools by role, so each agent only sees what it needs.</summary>
internal static class MemoryToolSets
{
    // Episodic write path: the session record maintained while interviewing.
    private static readonly HashSet<string> Session =
    [
        "add_interview_session", "get_interview_session", "update_interview_session", "complete_interview_session",
    ];

    // Long-term recall (read-only): episodic filters, recency lookups, vector search and the profile.
    private static readonly HashSet<string> Recall =
    [
        "list_recent_interviews", "find_interviews_by_company", "get_recent_feedback_items", "search_feedback", "get_user_profile",
    ];

    // Consolidation: only the agent that finishes an interview may write long-term memory.
    private static readonly HashSet<string> Consolidate =
    [
        "save_session_feedback", "get_user_profile", "save_user_profile",
    ];

    public static IList<AITool> SessionTools(IEnumerable<AITool> all) => Pick(all, Session);

    public static IList<AITool> RecallTools(IEnumerable<AITool> all) => Pick(all, Recall);

    public static IList<AITool> ConsolidationTools(IEnumerable<AITool> all) => Pick(all, Consolidate);

    private static IList<AITool> Pick(IEnumerable<AITool> all, HashSet<string> names)
        => [.. all.Where(t => names.Contains(t.Name))];
}
