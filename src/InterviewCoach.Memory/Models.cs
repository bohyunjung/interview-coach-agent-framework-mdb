using MongoDB.Bson;

namespace InterviewCoach.Memory;

/// <summary>Episodic memory: one interview session.</summary>
public class InterviewSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? CompanyName { get; set; }
    public string? RoleTitle { get; set; }
    public string? ResumeLink { get; set; }
    public string? ResumeText { get; set; }
    public bool ProceedWithoutResume { get; set; }
    public string? JobDescriptionLink { get; set; }
    public string? JobDescriptionText { get; set; }
    public bool ProceedWithoutJobDescription { get; set; }
    public string? Transcript { get; set; }
    public string? Feedback { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

/// <summary>Compact view of a session for recall; leaves out the heavy transcript and documents.</summary>
public record InterviewSummary(string Id, string? CompanyName, string? RoleTitle, DateTime CreatedAt, bool IsCompleted, string? Feedback);

/// <summary>Semantic memory: one strength or improvement point extracted from a session.</summary>
public class FeedbackItem
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? RoleTitle { get; set; }
    public string Kind { get; set; } = MemoryConstants.KindImprovement;
    public string Topic { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? TextHash { get; set; }
    public string? EmbeddingModel { get; set; }
    public float[]? Embedding { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A feedback item without its embedding, optionally with a vector similarity score.</summary>
public record FeedbackItemView(
    string Id,
    string SessionId,
    string? CompanyName,
    string? RoleTitle,
    string Kind,
    string Topic,
    string Text,
    DateTime CreatedAt,
    double? Score = null);

public record FeedbackItemInput(string Kind, string Topic, string Text);

/// <summary>Semantic memory about the user across all sessions.</summary>
public class UserProfile
{
    public string Id { get; set; } = MemoryConstants.ProfileId;
    public string? DisplayName { get; set; }
    public List<string> TargetRoles { get; set; } = [];
    public int InterviewCount { get; set; }
    public string? LastSessionId { get; set; }
    public string? Summary { get; set; }
    public List<ProfileTopic> Strengths { get; set; } = [];
    public List<ProfileTopic> GrowthAreas { get; set; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ProfileTopic
{
    public string Topic { get; set; } = string.Empty;
    // Only used for growth areas: recurring, improving or resolved.
    public string? Status { get; set; }
    public List<string> SessionIds { get; set; } = [];
}

/// <summary>Short-term memory: one chat turn.</summary>
public class ChatTurn
{
    public ObjectId Id { get; set; }
    public string ConversationId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? AuthorName { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
