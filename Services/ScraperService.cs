using System.Net.Http;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoshedTehran.Data;
using RoshedTehran.DTOs;
using RoshedTehran.Models;
using RoshedTehran.Statics;

namespace RoshedTehran.Services
{
    // Orchestrates: DuckDuckGo search -> fetch page -> extract contact info +
    // social links -> semantic chunking (site info / contact info) -> embed ->
    // upsert into Postgres + Qdrant (only if the domain doesn't already exist).
    public class ScraperService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ScraperService> _logger;
        private readonly SemanticSearchClient _semanticSearch;
        private readonly EmbederService _embeder;
        private readonly QdrantService _qdrant;
        private readonly ApplicationDbContext _db;

        public ScraperService(
            HttpClient httpClient,
            ILogger<ScraperService> logger,
            SemanticSearchClient semanticSearch,
            EmbederService embeder,
            QdrantService qdrant,
            ApplicationDbContext db)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromSeconds(15);

            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; RoshedTehranBot/1.0)");

            _logger = logger;
            _semanticSearch = semanticSearch;
            _embeder = embeder;
            _qdrant = qdrant;
            _db = db;
        }

        public string GetHost(string url)
        {
            var uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Host}";
        }

        private async Task<List<string>> DuckDuckGoSearchAsync(string query, int topN = 9)
        {
            const string url = "https://html.duckduckgo.com/html/";
            var data = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", query) });

            var response = await _httpClient.PostAsync(url, data);
            var html = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var results = new List<string>();
            var nodes = doc.DocumentNode.SelectNodes("//a[@class='result__a']");
            if (nodes == null) return results;

            foreach (var node in nodes.Take(topN))
            {
                var href = node.GetAttributeValue("href", "");
                if (!string.IsNullOrWhiteSpace(href))
                    results.Add(href);
            }
            return results;
        }

        private async Task<(HtmlDocument? Doc, string? RawHtml)> FetchPageAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return (null, null);

                var html = await response.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                return (doc, html);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch {Url}", url);
                return (null, null);
            }
        }

        public async Task<(string? Title, string? MetaDescription)> ExtractTitleAndDescriptionAsync(string host)
        {
            var (doc, _) = await FetchPageAsync(host);
            if (doc == null) return (null, null);

            string? title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();

            string? metaDesc =
                doc.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", null)
                ?? doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", null)
                ?? doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:description']")?.GetAttributeValue("content", null);

            return (title, metaDesc?.Trim());
        }

        // Scrapes one URL end to end and returns a fully populated DTO,
        // WITHOUT persisting anything (persistence happens in ScrapeAndPersistAsync).
        public async Task<EntityDto> ScrapeWebsiteAsync(string url)
        {
            var uri = new Uri(url);
            string baseUrl = uri.GetLeftPart(UriPartial.Authority);
            string domain = uri.Host;

            var (title, description) = await ExtractTitleAndDescriptionAsync(baseUrl);
            var (doc, rawHtml) = await FetchPageAsync(url);

            var dto = new EntityDto
            {
                Domain = domain,
                MainPageUrl = baseUrl,
                ContactPageUrl = url,
                TitleTag = title,
                MetaDescription = description,
                RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };

            if (doc == null || rawHtml == null)
                return dto;

            // Contact info (phones + emails), most important field per requirements.
            var (phones, emails) = ContactExtractor.ExtractContactInfo(doc);
            dto.PhoneNumber = phones;
            dto.Email = emails;

            // Social / messaging links.
            dto.SocialLinks = SocialMediaExtractor.Extract(doc);
            dto.Instagram = dto.SocialLinks.FirstOrDefault(s => s.Platform == "Instagram")?.Url;

            // Semantic chunking: exactly two chunks as requested -
            // one general site-info chunk, one contact-info chunk.
            try
            {
                var semantic = await _semanticSearch.AnalyzeAsync(rawHtml);
                dto.SiteInfoChunk = semantic.SiteInfoChunk;
                dto.ContactInfoChunk = semantic.ContactInfoChunk;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Semantic analysis failed for {Url}", url);
            }

            return dto;
        }

        // Full pipeline used by both the "online search" endpoint and the
        // CommonCrawl background worker: scrape, then persist to Postgres +
        // Qdrant only if the domain is not already known.
        public async Task<ResultObject> ScrapeAndPersistAsync(string url)
        {
            var dto = await ScrapeWebsiteAsync(url);

            var existing = await _db.Entities.FirstOrDefaultAsync(e => e.Domain == dto.Domain);
            if (existing != null)
            {
                return ToResultObject(existing);
            }

            var entity = new Entity
            {
                Id = Guid.NewGuid(),
                Domain = dto.Domain,
                MainPageUrl = dto.MainPageUrl,
                ContactPageUrl = dto.ContactPageUrl,
                TitleTag = dto.TitleTag,
                MetaDescription = dto.MetaDescription,
                Location = dto.Location,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                SiteInfoChunk = dto.SiteInfoChunk,
                ContactInfoChunk = dto.ContactInfoChunk,
                RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                LastCheckedUtc = DateTime.UtcNow,
                PhoneNumbers = dto.PhoneNumber.Select(p => new PhoneNumber { Phonenumber = p }).ToList(),
                Emails = dto.Email.Select(e => new Email { EmailAddress = e }).ToList(),
                SocialMediaLinks = dto.SocialLinks.Select(s => new SocialMediaLink
                {
                    Platform = Enum.TryParse<SocialPlatform>(s.Platform, true, out var plat) ? plat : SocialPlatform.Other,
                    Url = s.Url
                }).ToList()
            };

            await _db.Entities.AddAsync(entity);
            await _db.SaveChangesAsync();

            // Embed the site-info chunk (falls back to title+description if
            // semantic analysis produced nothing) and push to Qdrant, with a
            // payload pointing back at the Postgres id plus the searchable fields.
            var textToEmbed = !string.IsNullOrWhiteSpace(dto.SiteInfoChunk)
                ? dto.SiteInfoChunk!
                : $"{dto.TitleTag} {dto.MetaDescription}";

            if (!string.IsNullOrWhiteSpace(textToEmbed))
            {
                var vector = await _embeder.GetEmbeddingRawAsync(textToEmbed);
                await _qdrant.SaveEntityAsync(entity.Id, entity.TitleTag ?? "Unknown", vector, entity.ContactInfoChunk);
            }

            return ToResultObject(entity);
        }

        private static ResultObject ToResultObject(Entity e) => new()
        {
            Id = e.Id,
            Domain = e.Domain,
            MainPageUrl = e.MainPageUrl,
            ContactPageUrl = e.ContactPageUrl,
            TitleTag = e.TitleTag,
            Description = e.MetaDescription,
            Location = e.Location,
            PhoneNumber = e.PhoneNumbers.Select(p => p.Phonenumber).ToList(),
            Email = e.Emails.Select(em => em.EmailAddress).ToList(),
            SocialLinks = e.SocialMediaLinks.Select(s => new SocialLinkDto { Platform = s.Platform.ToString(), Url = s.Url }).ToList(),
            Instagram = e.SocialMediaLinks.FirstOrDefault(s => s.Platform == SocialPlatform.Instagram)?.Url,
            RegistrationDate = e.RegistrationDate
        };

        public async Task<IEnumerable<ResultObject>> ScrapeQueryAsync(string query)
        {
            string q = $"صفحه تماس با ما و ارتباط با ما {query}";
            var searchResults = await DuckDuckGoSearchAsync(q);

            _logger.LogInformation("Starting parallel scrape of {Count} results", searchResults.Count);

            var tasks = searchResults.Select(async url =>
            {
                try
                {
                    return await ScrapeAndPersistAsync(url);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to scrape/persist {Url}", url);
                    return null;
                }
            });

            var results = await Task.WhenAll(tasks);
            return results.Where(r => r != null)!;
        }
    }
}
