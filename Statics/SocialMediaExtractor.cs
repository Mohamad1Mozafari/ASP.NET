using System.Collections.Generic;
using System.Linq;
using HtmlAgilityPack;
using RoshedTehran.DTOs;

namespace RoshedTehran.Statics
{
    // Scans every <a href> on a page and classifies known social / contact platforms.
    public static class SocialMediaExtractor
    {
        private static readonly (string Match, string Platform)[] Patterns =
        {
            ("instagram.com", "Instagram"),
            ("linkedin.com", "LinkedIn"),
            ("t.me/", "Telegram"),
            ("telegram.me/", "Telegram"),
            ("wa.me/", "WhatsApp"),
            ("api.whatsapp.com", "WhatsApp"),
            ("whatsapp.com", "WhatsApp"),
            ("twitter.com", "Twitter"),
            ("x.com", "Twitter"),
            ("facebook.com", "Facebook"),
            ("youtube.com", "YouTube"),
            ("youtu.be", "YouTube"),
        };

        public static List<SocialLinkDto> Extract(HtmlDocument? doc)
        {
            var found = new Dictionary<string, SocialLinkDto>(); // dedupe by url

            if (doc == null) return new List<SocialLinkDto>();

            var links = doc.DocumentNode.SelectNodes("//a[@href]");
            if (links == null) return new List<SocialLinkDto>();

            foreach (var a in links)
            {
                var href = a.GetAttributeValue("href", "").Trim();
                if (string.IsNullOrWhiteSpace(href)) continue;

                var lower = href.ToLowerInvariant();

                foreach (var (match, platform) in Patterns)
                {
                    if (lower.Contains(match))
                    {
                        found[href] = new SocialLinkDto { Platform = platform, Url = href };
                        break;
                    }
                }
            }

            return found.Values.ToList();
        }
    }
}
