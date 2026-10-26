using System.Collections.Concurrent;

using GitHub.Copilot;

using InterviewCoach.Agent;
using InterviewCoach.Memory;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;


var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.AddServiceDefaults();

builder.Services.AddMongoMemory(config);

builder.Services.AddHttpClient("mcp-markitdown", client =>
{
    client.BaseAddress = new Uri("http://mcp-markitdown");
});

builder.Services.AddKeyedSingleton<McpClient>("mcp-markitdown", (sp, obj) =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var httpClient = sp.GetRequiredService<IHttpClientFactory>()
                       .CreateClient("mcp-markitdown");
    var endpoint = $"{httpClient.BaseAddress!.ToString().TrimEnd('/')}";

    var clientTransportOptions = new HttpClientTransportOptions()
    {
        Endpoint = new Uri($"{endpoint}/mcp")
    };
    var clientTransport = new HttpClientTransport(clientTransportOptions, httpClient, loggerFactory);

    var clientOptions = new McpClientOptions()
    {
        ClientInfo = new Implementation()
        {
            Name = "MCP MarkItDown Client",
            Version = "1.0.0",
        }
    };

    return McpClient.CreateAsync(clientTransport, clientOptions, loggerFactory).GetAwaiter().GetResult();
});


builder.Services.AddHttpClient("mcp-memory", client =>
{
    client.BaseAddress = new Uri("https+http://mcp-memory");
});

builder.Services.AddKeyedSingleton<McpClient>("mcp-memory", (sp, obj) =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var httpClient = sp.GetRequiredService<IHttpClientFactory>()
                       .CreateClient("mcp-memory");
    var endpoint = builder.Environment.IsDevelopment() == true
                 ? $"{httpClient.BaseAddress!.ToString().Replace("https+", string.Empty).TrimEnd('/')}"
                 : $"{httpClient.BaseAddress!.ToString().Replace("+http", string.Empty).TrimEnd('/')}";

    var clientTransportOptions = new HttpClientTransportOptions()
    {
        Endpoint = new Uri($"{endpoint}/mcp")
    };
    var clientTransport = new HttpClientTransport(clientTransportOptions, httpClient, loggerFactory);

    var clientOptions = new McpClientOptions()
    {
        ClientInfo = new Implementation()
        {
            Name = "MCP Memory Client",
            Version = "1.0.0",
        }
    };

    return McpClient.CreateAsync(clientTransport, clientOptions, loggerFactory).GetAwaiter().GetResult();
});

var llmProvider = Enum.TryParse<LlmProvider>(config[Constants.LlmProvider], ignoreCase: true, out var parsedProvider)
    ? parsedProvider
    : throw new InvalidOperationException($"LLM provider not specified or invalid. Please set the '{Constants.LlmProvider}' configuration value.");

if (llmProvider == LlmProvider.GitHubCopilot)
{
    var githubToken = config[Constants.GitHubToken];

    builder.Services.AddSingleton(_ => new CopilotClient(new CopilotClientOptions
    {
        BaseDirectory = Path.Combine(Path.GetTempPath(), "interview-coach-copilot"),
        GitHubToken = githubToken,
        Mode = CopilotClientMode.Empty,
        UseLoggedInUser = string.IsNullOrWhiteSpace(githubToken),
    }));
}
else
{
    throw new NotSupportedException($"The specified LLM provider '{llmProvider}' is not supported.");
}

var agentBuilder = builder.AddAIAgent("coach");

builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

// DevUI is intentionally configured for both development and production environments,
// so that the DevUI can be used to inspect the agent's state and behavior in production scenarios.
builder.Services.AddDevUI(options => options.AllowRemoteAccess = true);

builder.Services.AddAGUIServer();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapOpenAIResponses();
app.MapOpenAIConversations();

app.MapAGUIServer(agentBuilder, "ag-ui");

// The UI restores a conversation (page refresh, shared link) from the turns the middleware stored.
app.MapGet("/chat/{sessionId:guid}/messages", async (Guid sessionId, IChatHistoryRepository history, CancellationToken ct) =>
    ChatHistoryView.Collapse(await history.GetAsync(sessionId.ToString("D"), 500, ct)));

// DevUI is intentionally mapped for both development and production environments,
// so that the DevUI can be used to inspect the agent's state and behavior in production scenarios.
app.MapDevUI();

if (builder.Environment.IsDevelopment() == false)
{
    app.UseHttpsRedirection();
}

// --- File Upload Endpoints ---
// In-memory store for uploaded files (ephemeral, session-scoped).
var uploadedFiles = new ConcurrentDictionary<string, (byte[] Content, string ContentType, string FileName)>();

var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".pdf", ".docx", ".doc", ".txt", ".md", ".html"
};

app.MapPost("/upload", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data.");

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");

    if (file is null || file.Length == 0)
        return Results.BadRequest("No file provided.");

    if (file.Length > 10 * 1024 * 1024)
        return Results.Problem("File size exceeds 10 MB limit.", statusCode: 413);

    var ext = Path.GetExtension(file.FileName);
    if (!allowedExtensions.Contains(ext))
        return Results.Problem($"File type '{ext}' is not supported.", statusCode: 415);

    var fileId = Guid.NewGuid().ToString("N");
    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);

    uploadedFiles[fileId] = (ms.ToArray(), file.ContentType, file.FileName);

    // The URL is fetched by the MarkItDown container, where "localhost" is the container itself.
    var host = request.Host.Host is "localhost" or "127.0.0.1" or "[::1]"
        ? new HostString("host.docker.internal", request.Host.Port ?? (request.IsHttps ? 443 : 80))
        : request.Host;
    var url = $"{request.Scheme}://{host}/uploads/{fileId}/{Uri.EscapeDataString(file.FileName)}";
    return Results.Ok(new { url });
});

app.MapGet("/uploads/{fileId}/{fileName}", (string fileId, string fileName) =>
{
    if (!uploadedFiles.TryGetValue(fileId, out var entry))
        return Results.NotFound();

    return Results.File(entry.Content, entry.ContentType, entry.FileName);
});

await app.RunAsync();
