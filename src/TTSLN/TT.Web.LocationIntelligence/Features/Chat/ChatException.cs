namespace TT.Web.LocationIntelligence.Features.Chat;

/// <summary>A safe, explicit application failure mapped to Problem Details.</summary>
public sealed class ChatException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
