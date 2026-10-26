using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

namespace InterviewCoach.Memory;

public class VoyageOptions
{
    public const string Section = "Voyage";

    public string? ApiKey { get; set; }
    public string Model { get; set; } = "voyage-4-lite";
    public int Dimensions { get; set; } = 1024;

    /// <summary>Overrides the endpoint. When empty it is derived from the key (see <see cref="ResolveBaseUrl"/>).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Keys created in MongoDB Atlas ("al-" prefix) are served from ai.mongodb.com, Voyage dashboard keys from api.voyageai.com.</summary>
    public string ResolveBaseUrl() =>
        !string.IsNullOrWhiteSpace(BaseUrl) ? BaseUrl
        : ApiKey?.StartsWith("al-", StringComparison.Ordinal) == true ? "https://ai.mongodb.com/v1/"
        : "https://api.voyageai.com/v1/";
}

/// <summary>Calls the Voyage AI embeddings API directly (no auto-embedding index).</summary>
public class VoyageEmbeddingService(HttpClient http, IOptions<VoyageOptions> options) : IEmbeddingService
{
    private readonly VoyageOptions _options = options.Value;

    public string Model => _options.Model;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, EmbeddingInputType inputType, CancellationToken ct = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Voyage API key is not configured. Set 'Voyage:ApiKey' (see docs/PREREQUISITES.md).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.ResolveBaseUrl()), "embeddings"))
        {
            Content = JsonContent.Create(new EmbeddingRequest(
                texts,
                _options.Model,
                inputType == EmbeddingInputType.Query ? "query" : "document",
                _options.Dimensions)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Voyage embeddings request failed ({(int)response.StatusCode}): {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ct)
                     ?? throw new InvalidOperationException("Empty response from Voyage.");

        return [.. result.Data.OrderBy(d => d.Index).Select(d => d.Embedding)];
    }

    private record EmbeddingRequest(
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input_type")] string InputType,
        [property: JsonPropertyName("output_dimension")] int OutputDimension);

    private record EmbeddingResponse([property: JsonPropertyName("data")] List<EmbeddingData> Data);

    private record EmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
