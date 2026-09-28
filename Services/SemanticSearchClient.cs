using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RoshedTehran.Services
{
    // Talks to the Python "PSemanticSearcher" microservice (FastAPI, see Services/Semanticsercher.py)
    // which cleans the raw HTML, chunks it, and returns the two chunks we care about:
    //  - a general "site info" chunk (about/description of the business)
    //  - a "contact info" chunk (the part of the page most likely to contain phone/email/address)
    public class SemanticSearchClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public SemanticSearchClient(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _baseUrl = config["SemanticSearchService:BaseUrl"] ?? "http://127.0.0.1:8100";
        }

        public async Task<SemanticSearchResult> AnalyzeAsync(string rawHtml, CancellationToken ct = default)
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/analyze",
                new { html = rawHtml },
                ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SemanticSearchResult>(cancellationToken: ct);
            return result ?? new SemanticSearchResult();
        }
    }

    public class SemanticSearchResult
    {
        [JsonPropertyName("site_info_chunk")]
        public string? SiteInfoChunk { get; set; }

        [JsonPropertyName("contact_info_chunk")]
        public string? ContactInfoChunk { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }
    }
}
