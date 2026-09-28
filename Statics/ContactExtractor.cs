using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace RoshedTehran.Statics
{
    public static class ContactExtractor
    {
        private const int MinPhoneDigits = 7;
        private const int MaxPhoneDigits = 15;

        public static (List<string> Phones, List<string> Emails) ExtractContactInfo(HtmlDocument doc)
        {
            if (doc == null) return (new List<string>(), new List<string>());

            string rawText = doc.DocumentNode.InnerText ?? string.Empty;
            string normalizedText = ConvertUnicodeDigitsToAscii(rawText);

            var emails = ExtractEmails(normalizedText);
            var phones = ExtractPhones(normalizedText);

            return (phones, emails);
        }

        private static List<string> ExtractEmails(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();

            var emailPattern = @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}";
            var matches = Regex.Matches(text, emailPattern);

            return matches
                .Select(m => m.Value.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();
        }

        private static List<string> ExtractPhones(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();

            var phoneCandidatePattern = @"(?:(?:\+|00)?[0-9\-\s().\/]{7,50}[0-9])";
            var matches = Regex.Matches(text, phoneCandidatePattern);

            var results = new HashSet<string>();

            foreach (Match m in matches)
            {
                string candidate = m.Value.Trim();
                string normalized = NormalizePhoneCandidate(candidate);
                if (string.IsNullOrEmpty(normalized)) continue;

                string digitsOnlyForSplit = Regex.Replace(normalized, @"\D", "");
                bool hadLeadingPlus = normalized.StartsWith("+");

                if (digitsOnlyForSplit.Length > MaxPhoneDigits)
                {
                    foreach (var chunk in SplitLongDigits(digitsOnlyForSplit))
                    {
                        if (chunk.Length >= MinPhoneDigits && chunk.Length <= MaxPhoneDigits)
                            results.Add(hadLeadingPlus ? "+" + chunk : chunk);
                    }
                }
                else
                {
                    string digitsOnly = Regex.Replace(normalized, @"\D", "");
                    if (digitsOnly.Length >= MinPhoneDigits && digitsOnly.Length <= MaxPhoneDigits)
                        results.Add(normalized.StartsWith("+") ? "+" + digitsOnly : digitsOnly);
                }
            }

            var plainDigitSeqPattern = @"(?<!\d)(?:\+|00)?\d{7,20}(?!\d)";
            foreach (Match m in Regex.Matches(text, plainDigitSeqPattern))
            {
                string candidate = NormalizePhoneCandidate(m.Value);
                string digitsOnly = Regex.Replace(candidate, @"\D", "");
                bool hasPlus = candidate.StartsWith("+");

                if (digitsOnly.Length > MaxPhoneDigits)
                {
                    foreach (var c in SplitLongDigits(digitsOnly))
                        if (c.Length >= MinPhoneDigits && c.Length <= MaxPhoneDigits)
                            results.Add(hasPlus ? "+" + c : c);
                }
                else if (digitsOnly.Length >= MinPhoneDigits)
                {
                    results.Add(hasPlus ? "+" + digitsOnly : digitsOnly);
                }
            }

            return results.OrderBy(p => p).ToList();
        }

        private static string NormalizePhoneCandidate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            s = ConvertUnicodeDigitsToAscii(s.Trim());

            if (s.StartsWith("00"))
                s = "+" + s.Substring(2);

            bool hasPlus = s.StartsWith("+");
            string cleaned = Regex.Replace(s, @"[^\d\+]", "");

            if (cleaned.Count(c => c == '+') > 1)
                cleaned = cleaned.Replace("+", "");

            if (hasPlus && !cleaned.StartsWith("+"))
                cleaned = "+" + Regex.Replace(cleaned, @"\+", "");

            return cleaned;
        }

        private static List<string> SplitLongDigits(string digits)
        {
            var outList = new List<string>();
            if (string.IsNullOrEmpty(digits)) return outList;

            string remaining = digits;
            while (remaining.Length > 0)
            {
                if (remaining.Length <= MaxPhoneDigits && remaining.Length >= MinPhoneDigits)
                {
                    outList.Add(remaining);
                    break;
                }

                int take = 11;
                while (true)
                {
                    int remainder = remaining.Length - take;
                    if (remainder == 0 || remainder >= MinPhoneDigits) break;

                    take--;
                    if (take < MinPhoneDigits)
                    {
                        take = Math.Min(MaxPhoneDigits, remaining.Length);
                        break;
                    }
                }

                if (take <= 0) break;

                outList.Add(remaining.Substring(0, take));
                remaining = remaining.Substring(take);
            }

            return outList;
        }

        private static string ConvertUnicodeDigitsToAscii(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var sb = new System.Text.StringBuilder(input.Length);
            foreach (var ch in input)
            {
                switch (ch)
                {
                    case '۰': sb.Append('0'); break;
                    case '۱': sb.Append('1'); break;
                    case '۲': sb.Append('2'); break;
                    case '۳': sb.Append('3'); break;
                    case '۴': sb.Append('4'); break;
                    case '۵': sb.Append('5'); break;
                    case '۶': sb.Append('6'); break;
                    case '۷': sb.Append('7'); break;
                    case '۸': sb.Append('8'); break;
                    case '۹': sb.Append('9'); break;
                    case '٠': sb.Append('0'); break;
                    case '١': sb.Append('1'); break;
                    case '٢': sb.Append('2'); break;
                    case '٣': sb.Append('3'); break;
                    case '٤': sb.Append('4'); break;
                    case '٥': sb.Append('5'); break;
                    case '٦': sb.Append('6'); break;
                    case '٧': sb.Append('7'); break;
                    case '٨': sb.Append('8'); break;
                    case '٩': sb.Append('9'); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }
    }
}
