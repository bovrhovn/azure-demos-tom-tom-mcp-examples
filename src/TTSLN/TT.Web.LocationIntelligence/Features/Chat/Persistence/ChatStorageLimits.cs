using System.Text.Json;
using TT.Web.LocationIntelligence.Features.Chat.Models;

namespace TT.Web.LocationIntelligence.Features.Chat.Persistence;

/// <summary>Leaves headroom below Cosmos DB's 2 MB item limit, including JSON escaping.</summary>
public static class ChatStorageLimits
{
    public const int MaxReplyCharacters = 16000;
    private const int MaxDocumentBytes = 1_800_000;
    private const int MaxEscapedCharacterBytes = 6;

    public static void Validate(ChatSession session, bool reserveReply = false)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length;
        if (bytes + (reserveReply ? MaxReplyCharacters * MaxEscapedCharacterBytes : 0) > MaxDocumentBytes)
            throw new ChatException(400, "This conversation is full. Start a new conversation.", "session_full");
    }
}
