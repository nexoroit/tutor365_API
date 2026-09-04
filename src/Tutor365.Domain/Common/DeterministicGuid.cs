using System.Security.Cryptography;
using System.Text;

namespace Tutor365.Domain.Common;

/// <summary>Creates stable GUIDs from a string key so seed data is idempotent across environments.</summary>
public static class DeterministicGuid
{
    public static Guid Create(string key)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
