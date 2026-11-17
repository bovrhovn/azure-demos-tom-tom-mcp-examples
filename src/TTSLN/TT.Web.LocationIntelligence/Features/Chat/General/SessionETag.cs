using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;
using TT.Web.LocationIntelligence.Features.Chat.Models;

namespace TT.Web.LocationIntelligence.Features.Chat;

/// <summary>Computes opaque RFC 9110 entity tags without exposing raw storage metadata.</summary>
public static class SessionETag
{
    public static string Compute(ChatSession session)
    {
        var material = session.ETag is { Length: > 0 } etag
            ? $"{session.Id}:{etag}"
            : $"{session.Id}:{session.UpdatedAt.UtcTicks}:{session.Messages.Count}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"\"{Convert.ToHexStringLower(hash.AsSpan(0, 8))}\"";
    }

    public static bool Matches(StringValues ifNoneMatch, string etag)
    {
        foreach (var header in ifNoneMatch)
        {
            if (string.IsNullOrWhiteSpace(header)) continue;
            foreach (var candidate in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (candidate == "*" || NormalizeTag(candidate).SequenceEqual(NormalizeTag(etag)))
                    return true;
            }
        }
        return false;
    }

    private static ReadOnlySpan<char> NormalizeTag(ReadOnlySpan<char> value) =>
        value.StartsWith("W/", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
}
