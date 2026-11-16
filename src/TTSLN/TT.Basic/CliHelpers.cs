namespace TT.Basic;

public static class CliHelpers
{
    public static string MaskSecret(string secret, int visibleStart = 4, int visibleEnd = 4)
    {
        if (string.IsNullOrEmpty(secret))
            return string.Empty;

        if (secret.Length <= visibleStart + visibleEnd)
            return new string('*', secret.Length);

        return string.Concat(
            secret.AsSpan(0, visibleStart),
            new string('*', secret.Length - visibleStart - visibleEnd),
            secret.AsSpan(secret.Length - visibleEnd)
        );
    }
}