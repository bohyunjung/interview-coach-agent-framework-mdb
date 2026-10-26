using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace InterviewCoach.Memory;

public static class MemoryServiceCollectionExtensions
{
    private static readonly object Gate = new();
    private static bool _conventionsRegistered;

    /// <summary>Registers the MongoDB-backed memory repositories and the Voyage embedding client.</summary>
    public static IServiceCollection AddMongoMemory(this IServiceCollection services, IConfiguration configuration)
    {
        RegisterConventions();

        var connectionString = configuration.GetConnectionString(MemoryConstants.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{MemoryConstants.ConnectionStringName}' is not configured. See docs/PREREQUISITES.md.");
        }

        var databaseName = configuration["Mongo:Database"] ?? MemoryConstants.DefaultDatabase;

        services.Configure<VoyageOptions>(configuration.GetSection(VoyageOptions.Section));
        services.AddHttpClient<IEmbeddingService, VoyageEmbeddingService>();

        services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
        services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
        services.AddSingleton<ISessionRepository, MongoSessionRepository>();
        services.AddSingleton<IFeedbackRepository, MongoFeedbackRepository>();
        services.AddSingleton<IProfileRepository, MongoProfileRepository>();
        services.AddSingleton<IChatHistoryRepository, MongoChatHistoryRepository>();

        return services;
    }

    private static void RegisterConventions()
    {
        lock (Gate)
        {
            if (_conventionsRegistered)
            {
                return;
            }

            // The Python seed scripts write camelCase fields, so the C# models map to the same names.
            ConventionRegistry.Register("interview-coach-memory", new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new IgnoreExtraElementsConvention(true),
            }, t => t.Namespace == typeof(MemoryConstants).Namespace);

            _conventionsRegistered = true;
        }
    }
}

public class MongoSessionRepository(IMongoDatabase db) : ISessionRepository
{
    private readonly IMongoCollection<InterviewSession> _sessions = db.GetCollection<InterviewSession>(MemoryConstants.Sessions);

    private static readonly ProjectionDefinition<InterviewSession> SummaryProjection =
        Builders<InterviewSession>.Projection.Exclude(s => s.Transcript).Exclude(s => s.ResumeText).Exclude(s => s.JobDescriptionText);

    public async Task<InterviewSession> AddAsync(InterviewSession session, CancellationToken ct = default)
    {
        session.CreatedAt = session.UpdatedAt = DateTime.UtcNow;
        await _sessions.InsertOneAsync(session, cancellationToken: ct);

        return session;
    }

    public async Task<InterviewSession?> GetAsync(string id, CancellationToken ct = default)
        => await _sessions.Find(s => s.Id == id).FirstOrDefaultAsync(ct);

    public async Task<InterviewSession?> UpdateAsync(InterviewSession update, CancellationToken ct = default)
    {
        var current = await GetAsync(update.Id, ct);
        if (current is null)
        {
            return null;
        }

        var transcript = string.IsNullOrWhiteSpace(update.Transcript)
            ? current.Transcript
            : string.Join(Environment.NewLine + Environment.NewLine, new[] { current.Transcript, update.Transcript }.Where(t => !string.IsNullOrWhiteSpace(t)));

        var builder = Builders<InterviewSession>.Update;
        var updates = new List<UpdateDefinition<InterviewSession>>
        {
            builder.Set(s => s.ResumeLink, update.ResumeLink),
            builder.Set(s => s.ResumeText, update.ResumeText),
            builder.Set(s => s.ProceedWithoutResume, update.ProceedWithoutResume),
            builder.Set(s => s.JobDescriptionLink, update.JobDescriptionLink),
            builder.Set(s => s.JobDescriptionText, update.JobDescriptionText),
            builder.Set(s => s.ProceedWithoutJobDescription, update.ProceedWithoutJobDescription),
            builder.Set(s => s.Transcript, transcript),
            builder.Set(s => s.UpdatedAt, DateTime.UtcNow),
        };

        // Company and role are metadata for episodic recall; keep the stored value when the caller omits them.
        if (!string.IsNullOrWhiteSpace(update.CompanyName))
        {
            updates.Add(builder.Set(s => s.CompanyName, update.CompanyName));
        }

        if (!string.IsNullOrWhiteSpace(update.RoleTitle))
        {
            updates.Add(builder.Set(s => s.RoleTitle, update.RoleTitle));
        }

        return await _sessions.FindOneAndUpdateAsync(
            s => s.Id == update.Id,
            builder.Combine(updates),
            new FindOneAndUpdateOptions<InterviewSession> { ReturnDocument = ReturnDocument.After },
            ct);
    }

    public async Task<InterviewSession?> CompleteAsync(string id, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _sessions.FindOneAndUpdateAsync(
            s => s.Id == id,
            Builders<InterviewSession>.Update.Set(s => s.IsCompleted, true).Set(s => s.CompletedAt, now).Set(s => s.UpdatedAt, now),
            new FindOneAndUpdateOptions<InterviewSession> { ReturnDocument = ReturnDocument.After },
            ct);
    }

    public async Task<bool> SetFeedbackAsync(string id, string feedback, CancellationToken ct = default)
    {
        var result = await _sessions.UpdateOneAsync(
            s => s.Id == id,
            Builders<InterviewSession>.Update.Set(s => s.Feedback, feedback).Set(s => s.UpdatedAt, DateTime.UtcNow),
            cancellationToken: ct);

        return result.MatchedCount > 0;
    }

    public async Task<IReadOnlyList<InterviewSummary>> ListRecentAsync(int count, CancellationToken ct = default)
    {
        var sessions = await _sessions.Find(s => s.IsCompleted)
                                      .Project<InterviewSession>(SummaryProjection)
                                      .SortByDescending(s => s.CreatedAt)
                                      .Limit(Math.Clamp(count, 1, 50))
                                      .ToListAsync(ct);

        return [.. sessions.Select(ToSummary)];
    }

    public async Task<IReadOnlyList<InterviewSummary>> FindByCompanyAsync(string companyName, CancellationToken ct = default)
    {
        var pattern = new BsonRegularExpression(Regex.Escape(companyName.Trim()), "i");
        var sessions = await _sessions.Find(Builders<InterviewSession>.Filter.Regex(s => s.CompanyName, pattern))
                                      .Project<InterviewSession>(SummaryProjection)
                                      .SortByDescending(s => s.CreatedAt)
                                      .Limit(20)
                                      .ToListAsync(ct);

        return [.. sessions.Select(ToSummary)];
    }

    public Task<long> CountCompletedAsync(CancellationToken ct = default)
        => _sessions.CountDocumentsAsync(s => s.IsCompleted, cancellationToken: ct);

    private static InterviewSummary ToSummary(InterviewSession s)
        => new(s.Id, s.CompanyName, s.RoleTitle, s.CreatedAt, s.IsCompleted, s.Feedback);
}

public class MongoFeedbackRepository(IMongoDatabase db, IEmbeddingService embeddings) : IFeedbackRepository
{
    private const int NumCandidates = 100;

    private readonly IMongoCollection<FeedbackItem> _items = db.GetCollection<FeedbackItem>(MemoryConstants.FeedbackItems);
    private readonly IMongoCollection<InterviewSession> _sessions = db.GetCollection<InterviewSession>(MemoryConstants.Sessions);

    public async Task ReplaceForSessionAsync(InterviewSession session, IReadOnlyList<FeedbackItemInput> items, CancellationToken ct = default)
    {
        var vectors = await embeddings.EmbedAsync(
            [.. items.Select(i => $"{i.Topic}: {i.Text}")], EmbeddingInputType.Document, ct);

        var docs = items.Select((item, index) => new FeedbackItem
        {
            Id = $"fi-{session.Id}-{index + 1}",
            SessionId = session.Id,
            CompanyName = session.CompanyName,
            RoleTitle = session.RoleTitle,
            Kind = item.Kind,
            Topic = item.Topic,
            Text = item.Text,
            TextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Text))).ToLowerInvariant(),
            EmbeddingModel = embeddings.Model,
            Embedding = vectors[index],
            CreatedAt = session.CompletedAt ?? DateTime.UtcNow,
        }).ToList();

        await _items.DeleteManyAsync(i => i.SessionId == session.Id, ct);
        if (docs.Count > 0)
        {
            await _items.InsertManyAsync(docs, cancellationToken: ct);
        }
    }

    public async Task<IReadOnlyList<FeedbackItemView>> GetRecentAsync(int sessionCount, string? kind, CancellationToken ct = default)
    {
        var recent = await _sessions.Find(s => s.IsCompleted)
                                    .Project(s => new { s.Id, s.CreatedAt })
                                    .SortByDescending(s => s.CreatedAt)
                                    .Limit(Math.Clamp(sessionCount, 1, 50))
                                    .ToListAsync(ct);
        var ids = recent.Select(s => s.Id).ToList();

        var filter = Builders<FeedbackItem>.Filter.In(i => i.SessionId, ids);
        if (!string.IsNullOrWhiteSpace(kind))
        {
            filter &= Builders<FeedbackItem>.Filter.Eq(i => i.Kind, kind);
        }

        var items = await _items.Find(filter)
                                .Project<FeedbackItem>(Builders<FeedbackItem>.Projection.Exclude(i => i.Embedding))
                                .ToListAsync(ct);

        return [.. items.OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id).Select(i => ToView(i))];
    }

    public async Task<IReadOnlyList<FeedbackItemView>> SearchAsync(string query, string? kind, int limit, CancellationToken ct = default)
    {
        var vector = (await embeddings.EmbedAsync([query], EmbeddingInputType.Query, ct))[0];
        var pipeline = BuildVectorSearchPipeline(vector, kind, Math.Clamp(limit, 1, 20));

        var docs = await _items.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);

        return [.. docs.Select(d => new FeedbackItemView(
            d["_id"].AsString,
            d["sessionId"].AsString,
            d.GetValue("companyName", BsonNull.Value).IsBsonNull ? null : d["companyName"].AsString,
            d.GetValue("roleTitle", BsonNull.Value).IsBsonNull ? null : d["roleTitle"].AsString,
            d["kind"].AsString,
            d["topic"].AsString,
            d["text"].AsString,
            d["createdAt"].ToUniversalTime(),
            Math.Round(d["score"].ToDouble(), 4)))];
    }

    internal static PipelineDefinition<FeedbackItem, BsonDocument> BuildVectorSearchPipeline(float[] vector, string? kind, int limit)
    {
        var search = new BsonDocument
        {
            { "index", MemoryConstants.VectorIndex },
            { "path", MemoryConstants.VectorPath },
            { "queryVector", new BsonArray(vector.Select(v => (double)v)) },
            { "numCandidates", NumCandidates },
            { "limit", limit },
        };
        if (!string.IsNullOrWhiteSpace(kind))
        {
            search.Add("filter", new BsonDocument("kind", kind));
        }

        return PipelineDefinition<FeedbackItem, BsonDocument>.Create(
        [
            new BsonDocument("$vectorSearch", search),
            new BsonDocument("$addFields", new BsonDocument("score", new BsonDocument("$meta", "vectorSearchScore"))),
            new BsonDocument("$project", new BsonDocument(MemoryConstants.VectorPath, 0).Add("textHash", 0)),
        ]);
    }

    private static FeedbackItemView ToView(FeedbackItem i)
        => new(i.Id, i.SessionId, i.CompanyName, i.RoleTitle, i.Kind, i.Topic, i.Text, i.CreatedAt);
}

public class MongoProfileRepository(IMongoDatabase db) : IProfileRepository
{
    private readonly IMongoCollection<UserProfile> _profiles = db.GetCollection<UserProfile>(MemoryConstants.UserProfile);

    public async Task<UserProfile?> GetAsync(CancellationToken ct = default)
        => await _profiles.Find(p => p.Id == MemoryConstants.ProfileId).FirstOrDefaultAsync(ct);

    public async Task SaveAsync(UserProfile profile, CancellationToken ct = default)
    {
        profile.Id = MemoryConstants.ProfileId;
        profile.UpdatedAt = DateTime.UtcNow;
        await _profiles.ReplaceOneAsync(p => p.Id == profile.Id, profile, new ReplaceOptions { IsUpsert = true }, ct);
    }
}

public class MongoChatHistoryRepository(IMongoDatabase db) : IChatHistoryRepository
{
    private readonly IMongoCollection<ChatTurn> _turns = db.GetCollection<ChatTurn>(MemoryConstants.ChatMessages);

    public Task AppendAsync(ChatTurn turn, CancellationToken ct = default)
        => _turns.InsertOneAsync(turn, cancellationToken: ct);

    public async Task<IReadOnlyList<ChatTurn>> GetAsync(string conversationId, int limit, CancellationToken ct = default)
    {
        var turns = await _turns.Find(t => t.ConversationId == conversationId)
                                .SortByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                                .Limit(Math.Clamp(limit, 1, 500))
                                .ToListAsync(ct);
        turns.Reverse();

        return turns;
    }
}
