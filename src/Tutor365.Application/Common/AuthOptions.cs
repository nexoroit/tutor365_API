namespace Tutor365.Application.Common;

public class AuthOptions
{
    public const string SectionName = "Auth";
    public string Issuer { get; set; } = "Tutor365";
    public string Audience { get; set; } = "Tutor365.Client";
    public string SigningKey { get; set; } = default!;
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
    public int RememberMeRefreshTokenDays { get; set; } = 90;
    public int OtpExpiryMinutes { get; set; } = 10;
    public int OtpMaxAttempts { get; set; } = 5;
    public int LockoutThreshold { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public class AppOptions
{
    public const string SectionName = "App";
    public string Name { get; set; } = "Tutor365";
    public string FrontendUrl { get; set; } = "http://localhost:4200";
    public string SupportEmail { get; set; } = "support@tutor365.local";
    public string AdminEmail { get; set; } = "admin@tutor365.local";
    public string AdminPassword { get; set; } = "ChangeMe!2026";
    public bool SeedDemoData { get; set; } = false;
}
