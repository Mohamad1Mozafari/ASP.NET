using System.Net.Http.Json;

namespace RoshedTehran.Services
{
    public class EmbederService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public EmbederService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _baseUrl = config["EmbederService:BaseUrl"] ?? "http://127.0.0.1:8000";
        }

        public async Task<float[]> GetEmbeddingAsyncEntity(string? uri, string? titleTag, string? metaDescription)
        {
            string? domain = null;

            if (!string.IsNullOrWhiteSpace(uri) && Uri.TryCreate(uri, UriKind.Absolute, out var url))
            {
                var parts = url.Host.ToLowerInvariant().Split('.');
                if (parts.Length > 2 && parts[0] == "www")
                    parts = parts.Skip(1).ToArray();
                domain = parts.Length >= 2 ? parts[^2] : parts[0];
            }

            string text = string.Join(" ", new[] { domain, titleTag, metaDescription }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            return await GetEmbeddingRawAsync(text);
        }

        public async Task<float[]> GetEmbeddingRawAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text cannot be empty.", nameof(text));

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/embedEntity", new { entity = text });
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>();
            return result?.Embedding ?? throw new Exception("Failed to parse embedding response.");
        }

        public async Task<float[]> GetEmbeddingAsyncQuery(string searchquery)
        {
            if (string.IsNullOrWhiteSpace(searchquery))
                throw new ArgumentException("Text cannot be empty.", nameof(searchquery));

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/embedQuery", new { searchquery });
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>();
            return result?.Embedding ?? throw new Exception("Failed to parse embedding response.");
        }

        private class EmbeddingResponse
        {
            public float[] Embedding { get; set; } = Array.Empty<float>();
        }
    }
}
