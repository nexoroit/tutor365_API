using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Tutor365.IntegrationTests;

/// <summary>Boots the real API against the shared SQL Server (same DB as dev) with content import disabled for speed.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:ImportContentOnStartup"] = "false",
            ["Smtp:Enabled"] = "false",
            ["Serilog:MinimumLevel:Default"] = "Warning"
        }));
    }
}

public static class Http
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<(int Status, JsonElement Body)> SendAsync(this HttpClient client, HttpMethod method, string url, object? body = null, string? token = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body != null) req.Content = JsonContent.Create(body);
        if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        var doc = string.IsNullOrWhiteSpace(text) ? JsonDocument.Parse("{}") : JsonDocument.Parse(text);
        return ((int)res.StatusCode, doc.RootElement.Clone());
    }

    public static JsonElement Data(this JsonElement e) => e.GetProperty("data");
    public static string Str(this JsonElement e, string prop) => e.GetProperty(prop).GetString()!;
}
