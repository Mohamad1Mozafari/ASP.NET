using System.Collections.Generic;

namespace RoshedTehran.DTOs
{
    public class EntityDto
    {
        public string Domain { get; set; } = string.Empty;
        public string MainPageUrl { get; set; } = string.Empty;
        public string? ContactPageUrl { get; set; }

        public string? TitleTag { get; set; }
        public string? MetaDescription { get; set; }

        public string? Location { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? GoogleMapIdentifierUrl { get; set; }

        public string? SiteInfoChunk { get; set; }
        public string? ContactInfoChunk { get; set; }

        public List<string> PhoneNumber { get; set; } = new();
        public List<string> Email { get; set; } = new();
        public List<SocialLinkDto> SocialLinks { get; set; } = new();

        // legacy single-field, kept for backward compatibility with old callers
        public string? Instagram { get; set; }
        public string? DOM { get; set; }
        public System.DateOnly RegistrationDate { get; set; }
    }

    public class SocialLinkDto
    {
        public string Platform { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    public class IdsScores
    {
        public System.Guid Id { get; set; }
        public float Similarity_Score { get; set; }
    }

    public class ResultObject
    {
        public System.Guid Id { get; set; }
        public string? Domain { get; set; }
        public string? MainPageUrl { get; set; }
        public string? ContactPageUrl { get; set; }
        public string? TitleTag { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public List<string> PhoneNumber { get; set; } = new();
        public List<string> Email { get; set; } = new();
        public List<SocialLinkDto> SocialLinks { get; set; } = new();
        public string? Instagram { get; set; }
        public System.DateOnly RegistrationDate { get; set; }
        public float SimilarityScore { get; set; }
    }
}
