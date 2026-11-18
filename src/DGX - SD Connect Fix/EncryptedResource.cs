using System.Security.Cryptography;

namespace Dgx.SdConnectFix;

internal static class EncryptedResource
{
    private const int MagicSize = 8;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = MagicSize + SaltSize + NonceSize + TagSize;
    private const int Iterations = 600_000;

    internal static TResult Read<TResult>(string resourceName, ReadOnlySpan<byte> expectedMagic, ReadOnlySpan<char> password, Func<byte[], TResult> readContent)
    {
        using Stream resource = typeof(EncryptedResource).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException("Recurso criptografado ausente.");
        if (resource.Length <= HeaderSize)
            throw new InvalidDataException("Pacote offline incompleto.");

        Span<byte> header = stackalloc byte[HeaderSize];
        resource.ReadExactly(header);
        ReadOnlySpan<byte> magic = header[..MagicSize];
        if (!magic.SequenceEqual(expectedMagic))
            throw new InvalidDataException("Formato do pacote offline desconhecido.");

        ReadOnlySpan<byte> salt = header.Slice(MagicSize, SaltSize);
        ReadOnlySpan<byte> nonce = header.Slice(MagicSize + SaltSize, NonceSize);
        ReadOnlySpan<byte> tag = header[^TagSize..];
        byte[] ciphertext = new byte[checked((int)resource.Length - HeaderSize)];
        resource.ReadExactly(ciphertext);

        byte[] plaintext = new byte[ciphertext.Length];
        Span<byte> key = stackalloc byte[32];
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(password, salt, key, Iterations, HashAlgorithmName.SHA256);
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, magic);

            return readContent(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

