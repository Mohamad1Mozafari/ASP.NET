using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoshedTehran.Data;
using RoshedTehran.DTOs;
using RoshedTehran.Models;
using RoshedTehran.Services;

namespace RoshedTehran.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class SearchController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly EmbederService _embeder;
        private readonly QdrantService _qdrant;
        private readonly ScraperService _scraper;
        private readonly ILogger<SearchController> _logger;

        public SearchController(
            ApplicationDbContext db,
            EmbederService embeder,
            QdrantService qdrant,
            ScraperService scraper,
            ILogger<SearchController> logger)
        {
            _db = db;
            _embeder = embeder;
            _qdrant = qdrant;
            _scraper = scraper;
            _logger = logger;
        }

        /// <summary>
        /// Local/offline search: embeds the query, searches Qdrant, then joins the
        /// hits back to Postgres by id and returns them ordered by similarity.
        /// </summary>
        [HttpGet("offline")]
        public async Task<ActionResult<IEnumerable<ResultObject>>> Offline([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("query is required");

            await LogQueryAsync(query);

            var vector = await _embeder.GetEmbeddingAsyncQuery(query);
            var hits = (await _qdrant.SearchAsync(vector)).ToList();

            var ids = hits.Select(h => Guid.Parse(h.Id.Uuid)).ToList();
            var scoreById = hits.ToDictionary(h => Guid.Parse(h.Id.Uuid), h => h.Score);

            var entities = await _db.Entities
                .Include(e => e.PhoneNumbers)
                .Include(e => e.Emails)
                .Include(e => e.SocialMediaLinks)
                .Where(e => ids.Contains(e.Id))
                .ToListAsync();

            var results = entities
                .Select(e => ToResultObject(e, scoreById.GetValueOrDefault(e.Id)))
                .OrderByDescending(r => r.SimilarityScore)
                .ToList();

            return Ok(results);
        }

        /// <summary>
        /// Online search: DuckDuckGo -> scrape each result -> extract contact/social
        /// info -> semantic chunk -> persist new domains to Postgres + Qdrant.
        /// </summary>
        [HttpGet("online")]
        public async Task<ActionResult<IEnumerable<ResultObject>>> Online([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("query is required");

            await LogQueryAsync(query);

            var results = await _scraper.ScrapeQueryAsync(query);
            return Ok(results);
        }

        private async Task LogQueryAsync(string query)
        {
            await _db.Queries.AddAsync(new QueryEntity
            {
                Id = Guid.NewGuid(),
                Query = query,
                DateTime = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        private static ResultObject ToResultObject(Entity e, float score) => new()
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
            RegistrationDate = e.RegistrationDate,
            SimilarityScore = score
        };
    }
}
