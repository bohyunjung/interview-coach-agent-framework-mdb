using Xunit;
using InterviewCoach.Mcp.Memory;
using InterviewCoach.Memory;

using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol;

using MongoDB.Bson;

namespace InterviewCoach.Agent.Tests;

public class MemoryToolsTests
{
    private readonly FakeSessions _sessions = new();
    private readonly FakeFeedback _feedback = new();
    private readonly FakeProfiles _profiles = new();

    private MemoryTools Tools() => new(_sessions, _feedback, _profiles, NullLogger<MemoryTools>.Instance);

    [Fact]
    public async Task SaveSessionFeedback_StoresSummaryAndNormalizedItems()
    {
        _sessions.Stored = new InterviewSession { Id = "s1", CompanyName = "Acme" };

        var result = await Tools().SaveSessionFeedbackAsync("s1", "Good session.",
            [new("Strength", "failure handling", "Used idempotency keys."), new("improvement", "metrics", "Gave no numbers.")]);

        Assert.Contains("2 feedback items", result);
        Assert.Equal("Good session.", _sessions.Stored.Feedback);
        Assert.Equal(["strength", "improvement"], _feedback.Saved.Select(i => i.Kind));
    }

    [Fact]
    public async Task SaveSessionFeedback_RejectsInvalidKindsBeforeWriting()
    {
        _sessions.Stored = new InterviewSession { Id = "s1" };

        await Assert.ThrowsAsync<McpException>(() =>
            Tools().SaveSessionFeedbackAsync("s1", "x", [new("praise", "topic", "text")]));

        Assert.Null(_sessions.Stored.Feedback);
        Assert.Empty(_feedback.Saved);
    }

    [Fact]
    public async Task SaveSessionFeedback_FailsForUnknownSession()
        => await Assert.ThrowsAsync<McpException>(() => Tools().SaveSessionFeedbackAsync("missing", "x", []));

    [Fact]
    public async Task SearchFeedback_RequiresAQueryAndPassesTheKindFilter()
    {
        await Assert.ThrowsAsync<McpException>(() => Tools().SearchFeedbackAsync(" "));

        await Tools().SearchFeedbackAsync("debugging", "Strength", 3);

        Assert.Equal(("debugging", "strength", 3), _feedback.LastSearch);
    }

    [Fact]
    public async Task SaveUserProfile_ComputesInterviewCountAndLastSession()
    {
        _sessions.Completed = 6;
        _sessions.Recent = [new InterviewSummary("s6", "Quillstream", "AI Engineer", DateTime.UtcNow, true, null)];

        var profile = await Tools().SaveUserProfileAsync("Improving.", ["AI Engineer"],
            [new ProfileTopic { Topic = "retries", SessionIds = ["s1"] }],
            [new ProfileTopic { Topic = "quantify results", Status = "recurring", SessionIds = ["s1", "s3"] }]);

        Assert.Equal(6, profile.InterviewCount);
        Assert.Equal("s6", profile.LastSessionId);
        Assert.Same(profile, _profiles.Saved);
    }

    [Fact]
    public void VectorSearchPipeline_UsesTheIndexFilterAndScore()
    {
        var pipeline = MongoFeedbackRepository.BuildVectorSearchPipeline([0.1f, 0.2f], "strength", 5);
        var registry = MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry;
        var serializer = registry.GetSerializer<FeedbackItem>();
        var stages = pipeline.Render(new MongoDB.Driver.RenderArgs<FeedbackItem>(serializer, registry)).Documents.ToList();
        var search = stages[0]["$vectorSearch"].AsBsonDocument;

        Assert.Equal(MemoryConstants.VectorIndex, search["index"].AsString);
        Assert.Equal("embedding", search["path"].AsString);
        Assert.Equal(2, search["queryVector"].AsBsonArray.Count);
        Assert.Equal(5, search["limit"].AsInt32);
        Assert.Equal("strength", search["filter"]["kind"].AsString);
        Assert.Equal("vectorSearchScore", stages[1]["$addFields"]["score"]["$meta"].AsString);
    }

    private sealed class FakeSessions : ISessionRepository
    {
        public InterviewSession? Stored { get; set; }
        public long Completed { get; set; }
        public IReadOnlyList<InterviewSummary> Recent { get; set; } = [];

        public Task<InterviewSession> AddAsync(InterviewSession s, CancellationToken ct = default) => Task.FromResult(Stored = s);
        public Task<InterviewSession?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(Stored?.Id == id ? Stored : null);
        public Task<InterviewSession?> UpdateAsync(InterviewSession u, CancellationToken ct = default) => Task.FromResult<InterviewSession?>(Stored);
        public Task<InterviewSession?> CompleteAsync(string id, CancellationToken ct = default) => Task.FromResult<InterviewSession?>(Stored);

        public Task<bool> SetFeedbackAsync(string id, string feedback, CancellationToken ct = default)
        {
            Stored!.Feedback = feedback;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<InterviewSummary>> ListRecentAsync(int count, CancellationToken ct = default) => Task.FromResult(Recent);
        public Task<IReadOnlyList<InterviewSummary>> FindByCompanyAsync(string c, CancellationToken ct = default) => Task.FromResult(Recent);
        public Task<long> CountCompletedAsync(CancellationToken ct = default) => Task.FromResult(Completed);
    }

    private sealed class FakeFeedback : IFeedbackRepository
    {
        public List<FeedbackItemInput> Saved { get; } = [];
        public (string Query, string? Kind, int Limit)? LastSearch { get; private set; }

        public Task ReplaceForSessionAsync(InterviewSession s, IReadOnlyList<FeedbackItemInput> items, CancellationToken ct = default)
        {
            Saved.AddRange(items);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FeedbackItemView>> GetRecentAsync(int n, string? kind, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FeedbackItemView>>([]);

        public Task<IReadOnlyList<FeedbackItemView>> SearchAsync(string query, string? kind, int limit, CancellationToken ct = default)
        {
            LastSearch = (query, kind, limit);
            return Task.FromResult<IReadOnlyList<FeedbackItemView>>([]);
        }
    }

    private sealed class FakeProfiles : IProfileRepository
    {
        public UserProfile? Saved { get; private set; }
        public Task<UserProfile?> GetAsync(CancellationToken ct = default) => Task.FromResult(Saved);
        public Task SaveAsync(UserProfile p, CancellationToken ct = default) { Saved = p; return Task.CompletedTask; }
    }
}
