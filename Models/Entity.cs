using System;
using System.Collections.Generic;

namespace RoshedTehran.Models
{
    public class Entity
    {
        public Guid Id { get; set; }

        public string Domain { get; set; } = string.Empty;
        public string MainPageUrl { get; set; } = string.Empty;
        public string? ContactPageUrl { get; set; }

        public string? TitleTag { get; set; }
        public string? MetaDescription { get; set; }

        // Free-text address/location extracted from the page.
        public string? Location { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? GoogleMapIdentifierUrl { get; set; }

        // The cleaned text chunk that was actually embedded into Qdrant
        // (site-info chunk). Kept here too so Postgres has a copy.
        public string? SiteInfoChunk { get; set; }

        // The chunk of text that specifically contains phone/email/address
        // context (kept separate per your "two chunks" requirement).
        public string? ContactInfoChunk { get; set; }

        public DateOnly RegistrationDate { get; set; }
        public DateTime LastCheckedUtc { get; set; }

        public ICollection<PhoneNumber> PhoneNumbers { get; set; } = new List<PhoneNumber>();
        public ICollection<Email> Emails { get; set; } = new List<Email>();
        public ICollection<SocialMediaLink> SocialMediaLinks { get; set; } = new List<SocialMediaLink>();
    }

    public class PhoneNumber
    {
        public int Id { get; set; }
        public Guid RecordId { get; set; }
        public string Phonenumber { get; set; } = string.Empty;
        public Entity? Entity { get; set; }
    }

    public class Email
    {
        public int Id { get; set; }
        public Guid RecordId { get; set; }
        public string EmailAddress { get; set; } = string.Empty;
        public Entity? Entity { get; set; }
    }

    public enum SocialPlatform
    {
        Instagram,
        LinkedIn,
        Telegram,
        WhatsApp,
        Twitter,
        Facebook,
        YouTube,
        Other
    }

    public class SocialMediaLink
    {
        public int Id { get; set; }
        public Guid RecordId { get; set; }
        public SocialPlatform Platform { get; set; }
        public string Url { get; set; } = string.Empty;
        public Entity? Entity { get; set; }
    }

    public class QueryEntity
    {
        public Guid Id { get; set; }
        public string Query { get; set; } = string.Empty;
        public DateTime DateTime { get; set; }
    }

    // Tracks the last CommonCrawl collection id we fully ingested,
    // so the background worker knows whether a new one is available.
    public class CrawlIngestState
    {
        public int Id { get; set; }
        public string LastIngestedCollectionId { get; set; } = string.Empty;
        public int LastIndexFileProcessed { get; set; } // cdx-XXXXX, resume point
        public DateTime UpdatedAtUtc { get; set; }
    }
}
