using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoshedTehran.Data;
using RoshedTehran.Models;
using RoshedTehran.Services;

namespace RoshedTehran.BackgroundServices
{
    // Every 10 days:
    //  1. asks CommonCrawl for the newest collection id
    //  2. if it differs from what we last fully ingested, downloads that
    //     collection's cdx-XXXXX.gz index files one by one
    //  3. for each entry, extracts the "url" JSON field, takes the host,
    //     and if that domain is not already in Postgres, runs it through
    //     the full scrape+persist pipeline (Postgres + Qdrant)
    //  4. tracks progress (which index file we're on) in CrawlIngestState so
    //     a restart resumes instead of starting over
    public class CommonCrawlBackgroundService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(10);
        private readonly IServiceProvider _services;
        private readonly ILogger<CommonCrawlBackgroundService> _logger;

        public CommonCrawlBackgroundService(IServiceProvider services, ILogger<CommonCrawlBackgroundService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CommonCrawl ingestion cycle failed");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var crawl = scope.ServiceProvider.GetRequiredService<CommonCrawlService>();
            var scraper = scope.ServiceProvider.GetRequiredService<ScraperService>();
            var httpClient = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();

            var latestId = await crawl.GetLatestCollectionIdAsync(ct);
            if (string.IsNullOrWhiteSpace(latestId))
            {
                _logger.LogWarning("Could not determine latest CommonCrawl collection id");
                return;
            }

            var state = await db.CrawlIngestStates.FirstOrDefaultAsync(ct)
                ?? new CrawlIngestState { LastIngestedCollectionId = "", LastIndexFileProcessed = -1 };

            bool isNewCollection = state.LastIngestedCollectionId != latestId;
            if (isNewCollection)
            {
                _logger.LogInformation("New CommonCrawl collection detected: {Old} -> {New}", state.LastIngestedCollectionId, latestId);
                state.LastIngestedCollectionId = latestId;
                state.LastIndexFileProcessed = -1; // start this new collection from file 0
            }

            var lastFileAvailable = await crawl.FindLastIndexFileNumberAsync(latestId, ct);
            if (lastFileAvailable < 0)
            {
                _logger.LogWarning("No index files found for collection {Id}", latestId);
                return;
            }

            for (int fileIndex = state.LastIndexFileProcessed + 1; fileIndex <= lastFileAvailable; fileIndex++)
            {
                ct.ThrowIfCancellationRequested();

                var url = CommonCrawlService.BuildIndexFileUrl(latestId, fileIndex);
                _logger.LogInformation("Processing CommonCrawl index file {Url}", url);

                try
                {
                    var hosts = await DownloadAndExtractHostsAsync(httpClient, url, ct);

                    foreach (var host in hosts)
                    {
                        ct.ThrowIfCancellationRequested();

                        var domainExists = await db.Entities.AnyAsync(e => e.Domain == host, ct);
                        if (domainExists) continue;

                        try
                        {
                            await scraper.ScrapeAndPersistAsync($"https://{host}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to scrape discovered host {Host}", host);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process index file {Url}, will retry next cycle", url);
                    break; // stop here, resume from this same file next run
                }

                state.LastIndexFileProcessed = fileIndex;
                state.UpdatedAtUtc = DateTime.UtcNow;

                if (state.Id == 0) await db.CrawlIngestStates.AddAsync(state, ct);
                await db.SaveChangesAsync(ct);
            }
        }

        // Streams the .gz, decompresses, and pulls out unique hosts from each
        // CDX line's JSON payload (mirrors the logic in the original
        // backGroundWorker.cs console app, but async/streaming and de-duped).
        private static async Task<HashSet<string>> DownloadAndExtractHostsAsync(HttpClient httpClient, string url, CancellationToken ct)
        {
            var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var rawStream = await response.Content.ReadAsStreamAsync(ct);
            await using var gzip = new GZipStream(rawStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var parts = line.Split(' ', 3);
                if (parts.Length < 3) continue;

                string? urlValue;
                try
                {
                    using var jsonDoc = JsonDocument.Parse(parts[2]);
                    if (!jsonDoc.RootElement.TryGetProperty("url", out var urlProp)) continue;
                    urlValue = urlProp.GetString();
                }
                catch (JsonException)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(urlValue)) continue;
                if (!Uri.TryCreate(urlValue, UriKind.Absolute, out var uri)) continue;

                var host = uri.Host;
                if (System.Net.IPAddress.TryParse(host, out _)) continue;

                hosts.Add(host);
            }

            return hosts;
        }
    }
}
