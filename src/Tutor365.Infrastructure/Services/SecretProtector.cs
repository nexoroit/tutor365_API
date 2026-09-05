using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Tutor365.Application.Common;
using Tutor365.Application.Interfaces;

namespace Tutor365.Infrastructure.Services;

public class SecretProtector : ISecretProtector
{
    private const string Prefix = "enc:v1:";
    private readonly byte[] _key;

    public SecretProtector(IOptions<AuthOptions> auth)
    {
        _key = SHA256.HashData(Encoding.UTF8.GetBytes("tutor365-secrets:" + auth.Value.SigningKey));
    }

    public bool IsProtected(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Prefix + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public string Unprotect(string stored)
    {
        if (!IsProtected(stored)) return stored;
        var bytes = Convert.FromBase64String(stored[Prefix.Length..]);
        var nonce = bytes[..12]; var tag = bytes[12..28]; var cipher = bytes[28..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
