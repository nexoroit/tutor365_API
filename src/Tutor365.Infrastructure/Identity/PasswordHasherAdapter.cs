using Microsoft.AspNetCore.Identity;
using Tutor365.Domain.Entities;

namespace Tutor365.Infrastructure.Identity;

public class PasswordHasherAdapter : Application.Interfaces.IPasswordHasher
{
    private static readonly PasswordHasher<User> Hasher = new();
    private static readonly User Dummy = new();

    public string Hash(string password) => Hasher.HashPassword(Dummy, password);

    public bool Verify(string hash, string password)
    {
        var result = Hasher.VerifyHashedPassword(Dummy, hash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
