using System.Security.Cryptography;

namespace Dgx.SdConnectFix;

internal static class PasswordVerifier
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 600_000;
    private static readonly byte[] Verifier = Convert.FromBase64String("6rQpHJ2jDBQf4pcdxkUS9DrfdSf/XN9CiO20E4QB/QrxYUSXpdRqOoP93jaDZVku");

    internal static bool Matches(string password)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        Span<byte> computed = stackalloc byte[HashSize];
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(password.AsSpan(), Verifier.AsSpan(0, SaltSize), computed,
                Iterations, HashAlgorithmName.SHA256);
            return CryptographicOperations.FixedTimeEquals(computed, Verifier.AsSpan(SaltSize));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(computed);
        }
    }
}
