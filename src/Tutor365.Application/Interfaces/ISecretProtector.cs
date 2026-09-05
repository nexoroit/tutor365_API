namespace Tutor365.Application.Interfaces;

/// <summary>Reversible encryption for secrets stored in SystemSettings (SMTP password, AI keys). Key derived from Auth:SigningKey so all environments sharing config can decrypt.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string stored);
    bool IsProtected(string value);
}
