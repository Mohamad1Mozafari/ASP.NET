using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoshedTehran.Data;
using RoshedTehran.DTOs;
using RoshedTehran.Models;
using RoshedTehran.Services;

namespace RoshedTehran.Controllers
{
    [Route("api/v1/[controller]")]
    [ApiController]
    public class EntityController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly EmbederService _embeder;
        private readonly QdrantService _qdrant;

        public EntityController(ApplicationDbContext db, EmbederService embeder, QdrantService qdrant)
        {
            _db = db;
            _embeder = embeder;
            _qdrant = qdrant;
        }

        [HttpPost]
        public async Task<IActionResult> AddEntity([FromBody] EntityDto dto)
        {
            var existing = await _db.Entities.FirstOrDefaultAsync(e => e.Domain == dto.Domain);
            if (existing != null)
                return Conflict(new { message = "Domain already exists", id = existing.Id });

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

            var textToEmbed = !string.IsNullOrWhiteSpace(dto.SiteInfoChunk) ? dto.SiteInfoChunk! : $"{dto.TitleTag} {dto.MetaDescription}";
            if (!string.IsNullOrWhiteSpace(textToEmbed))
            {
                var embedding = await _embeder.GetEmbeddingRawAsync(textToEmbed);
                await _qdrant.SaveEntityAsync(entity.Id, entity.TitleTag ?? "Unknown", embedding, entity.ContactInfoChunk);
            }

            return CreatedAtAction(nameof(AddEntity), new { id = entity.Id }, entity);
        }
    }
}
