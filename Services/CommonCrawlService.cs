using System.Text.Json;

namespace RoshedTehran.Services
{
    // Talks to the CommonCrawl index to discover the newest collection id and
    // to build the concrete cdx-XXXXX.gz URLs for that collection.
    public class CommonCrawlService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CommonCrawlService> _logger;

        // The real collections go up to roughly cdx-00299 as you noted; we
        // still probe with HEAD requests instead of hard-coding 300 in case
        // a given collection has fewer/more shards.
        private const int MaxProbableIndexFiles = 320;

        public CommonCrawlService(HttpClient httpClient, ILogger<CommonCrawlService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string?> GetLatestCollectionIdAsync(CancellationToken ct = default)
        {
            var response = await _httpClient.GetAsync("https://index.commoncrawl.org/collinfo.json", ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var first = doc.RootElement.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Undefined) return null;

            return first.GetProperty("id").GetString();
        }

        // e.g. "CC-MAIN-2026-39", 0 -> https://data.commoncrawl.org/cc-index/collections/CC-MAIN-2026-39/indexes/cdx-00000.gz
        public static string BuildIndexFileUrl(string collectionId, int index) =>
            $"https://data.commoncrawl.org/cc-index/collections/{collectionId}/indexes/cdx-{index:D5}.gz";

        public static string BuildClusterIdxUrl(string collectionId) =>
            $"https://data.commoncrawl.org/cc-index/collections/{collectionId}/indexes/cluster.idx";

        // Probes sequential cdx files with HEAD requests until a 404 is hit,
        // so we know exactly where the collection ends without guessing.
        public async Task<int> FindLastIndexFileNumberAsync(string collectionId, CancellationToken ct = default)
        {
            int last = -1;
            for (int i = 0; i < MaxProbableIndexFiles; i++)
            {
                var url = BuildIndexFileUrl(collectionId, i);
                using var req = new HttpRequestMessage(HttpMethod.Head, url);
                try
                {
                    var resp = await _httpClient.SendAsync(req, ct);
                    if (!resp.IsSuccessStatusCode) break;
                    last = i;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "HEAD probe failed for {Url}", url);
                    break;
                }
            }
            return last;
        }
    }
}
