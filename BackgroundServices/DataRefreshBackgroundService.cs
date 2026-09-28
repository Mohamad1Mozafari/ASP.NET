using Microsoft.EntityFrameworkCore;
using RoshedTehran.Data;
using RoshedTehran.Services;

namespace RoshedTehran.BackgroundServices
{
    // Once a month: finds every entity whose data is older than a year and
    // re-scrapes it, updating Postgres (contact info, social links, chunks)
    // and re-embedding into Qdrant, then bumps RegistrationDate/LastCheckedUtc.
    public class DataRefreshBackgroundService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(30);
        private readonly IServiceProvider _services;
        private readonly ILogger<DataRefreshBackgroundService> _logger;

        public DataRefreshBackgroundService(IServiceProvider services, ILogger<DataRefreshBackgroundService> logger)
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
                    await RefreshStaleEntitiesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Monthly data refresh cycle failed");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
        }

        private async Task RefreshStaleEntitiesAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var scraper = scope.ServiceProvider.GetRequiredService<ScraperService>();
            var embeder = scope.ServiceProvider.GetRequiredService<EmbederService>();
            var qdrant = scope.ServiceProvider.GetRequiredService<QdrantService>();

            var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));

            var staleEntities = await db.Entities
                .Include(e => e.PhoneNumbers)
                .Include(e => e.Emails)
                .Include(e => e.SocialMediaLinks)
                .Where(e => e.RegistrationDate < cutoff)
                .ToListAsync(ct);

            _logger.LogInformation("Found {Count} entities older than a year, refreshing", staleEntities.Count);

            foreach (var entity in staleEntities)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var fresh = await scraper.ScrapeWebsiteAsync(entity.ContactPageUrl ?? entity.MainPageUrl);

                    entity.TitleTag = fresh.TitleTag ?? entity.TitleTag;
                    entity.MetaDescription = fresh.MetaDescription ?? entity.MetaDescription;
                    entity.SiteInfoChunk = fresh.SiteInfoChunk ?? entity.SiteInfoChunk;
                    entity.ContactInfoChunk = fresh.ContactInfoChunk ?? entity.ContactInfoChunk;

                    db.PhoneNumbers.RemoveRange(entity.PhoneNumbers);
                    db.Emails.RemoveRange(entity.Emails);
                    db.SocialMediaLinks.RemoveRange(entity.SocialMediaLinks);

                    entity.PhoneNumbers = fresh.PhoneNumber.Select(p => new Models.PhoneNumber { RecordId = entity.Id, Phonenumber = p }).ToList();
                    entity.Emails = fresh.Email.Select(e => new Models.Email { RecordId = entity.Id, EmailAddress = e }).ToList();
                    entity.SocialMediaLinks = fresh.SocialLinks.Select(s => new Models.SocialMediaLink
                    {
                        RecordId = entity.Id,
                        Platform = Enum.TryParse<Models.SocialPlatform>(s.Platform, true, out var plat) ? plat : Models.SocialPlatform.Other,
                        Url = s.Url
                    }).ToList();

                    // Reset the "age" clock now that the data is current.
                    entity.RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow);
                    entity.LastCheckedUtc = DateTime.UtcNow;

                    await db.SaveChangesAsync(ct);

                    var textToEmbed = !string.IsNullOrWhiteSpace(entity.SiteInfoChunk)
                        ? entity.SiteInfoChunk!
                        : $"{entity.TitleTag} {entity.MetaDescription}";

                    if (!string.IsNullOrWhiteSpace(textToEmbed))
                    {
                        var vector = await embeder.GetEmbeddingRawAsync(textToEmbed);
                        await qdrant.SaveEntityAsync(entity.Id, entity.TitleTag ?? "Unknown", vector, entity.ContactInfoChunk);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh entity {Id} ({Domain})", entity.Id, entity.Domain);
                }
            }
        }
    }
}
