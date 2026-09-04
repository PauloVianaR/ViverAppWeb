using System.Security.Cryptography;
using System.Text;

namespace ViverApp.Api.Features.Payments;

internal static class PagBankWebhookAuthenticator
{
    public const string HeaderName = "x-authenticity-token";

    public static bool Verify(string token, ReadOnlySpan<byte> payload, string? receivedSignature, out byte[] expectedHash)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var signed = new byte[tokenBytes.Length + 1 + payload.Length];
        tokenBytes.CopyTo(signed, 0);
        signed[tokenBytes.Length] = (byte)'-';
        payload.CopyTo(signed.AsSpan(tokenBytes.Length + 1));
        expectedHash = SHA256.HashData(signed);
        CryptographicOperations.ZeroMemory(signed);
        CryptographicOperations.ZeroMemory(tokenBytes);

        if (receivedSignature?.Length != 64)
        {
            return false;
        }

        try
        {
            var received = Convert.FromHexString(receivedSignature);
            return received.Length == expectedHash.Length
                && CryptographicOperations.FixedTimeEquals(expectedHash, received);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
