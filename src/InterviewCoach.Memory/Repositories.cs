namespace InterviewCoach.Memory;

public interface ISessionRepository
{
    Task<InterviewSession> AddAsync(InterviewSession session, CancellationToken ct = default);
    Task<InterviewSession?> GetAsync(string id, CancellationToken ct = default);
    /// <summary>Replaces the document fields, appends the transcript, and returns null if the session does not exist.</summary>
    Task<InterviewSession?> UpdateAsync(InterviewSession update, CancellationToken ct = default);
    Task<InterviewSession?> CompleteAsync(string id, CancellationToken ct = default);
    Task<bool> SetFeedbackAsync(string id, string feedback, CancellationToken ct = default);
    Task<IReadOnlyList<InterviewSummary>> ListRecentAsync(int count, CancellationToken ct = default);
    Task<IReadOnlyList<InterviewSummary>> FindByCompanyAsync(string companyName, CancellationToken ct = default);
    Task<long> CountCompletedAsync(CancellationToken ct = default);
}

public interface IFeedbackRepository
{
    /// <summary>Replaces all feedback items of a session, embedding them first.</summary>
    Task ReplaceForSessionAsync(InterviewSession session, IReadOnlyList<FeedbackItemInput> items, CancellationToken ct = default);
    /// <summary>Items from the most recent completed sessions, newest session first.</summary>
    Task<IReadOnlyList<FeedbackItemView>> GetRecentAsync(int sessionCount, string? kind, CancellationToken ct = default);
    /// <summary>Semantic search with MongoDB Vector Search on Atlas.</summary>
    Task<IReadOnlyList<FeedbackItemView>> SearchAsync(string query, string? kind, int limit, CancellationToken ct = default);
}

public interface IProfileRepository
{
    Task<UserProfile?> GetAsync(CancellationToken ct = default);
    Task SaveAsync(UserProfile profile, CancellationToken ct = default);
}

public interface IChatHistoryRepository
{
    Task AppendAsync(ChatTurn turn, CancellationToken ct = default);
    Task<IReadOnlyList<ChatTurn>> GetAsync(string conversationId, int limit, CancellationToken ct = default);
}

public interface IEmbeddingService
{
    string Model { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, EmbeddingInputType inputType, CancellationToken ct = default);
}

public enum EmbeddingInputType
{
    Document,
    Query
}
