using System.Text;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;
using Tutor365.Api.Configuration;
using Tutor365.Api.Filters;
using Tutor365.Api.Middleware;
using Tutor365.Application.Common;
using Tutor365.Application.Validators;
using Tutor365.Infrastructure;
using Tutor365.Infrastructure.Data;
using Tutor365.Infrastructure.Data.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext());

// ---- services ----
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

builder.Services.AddControllers(o => o.Filters.Add<ValidationFilter>())
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Return model-binding errors in the same envelope as everything else.
builder.Services.Configure<ApiBehaviorOptions>(o =>
{
    o.InvalidModelStateResponseFactory = ctx =>
    {
        var errors = ctx.ModelState.Where(kv => kv.Value?.Errors.Count > 0)
            .ToDictionary(kv => kv.Key.Length > 0 ? char.ToLowerInvariant(kv.Key[0]) + kv.Key[1..] : kv.Key,
                          kv => kv.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage).ToArray());
        return new BadRequestObjectResult(ApiResponse.Fail("VALIDATION_FAILED", "The request is invalid.", errors));
    };
});

builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions = true;
    o.ApiVersionReader = new UrlSegmentApiVersionReader();
}).AddApiExplorer(o =>
{
    o.GroupNameFormat = "'v'VVV";
    o.SubstituteApiVersionInUrl = true;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
builder.Services.AddSwaggerGen();

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = authOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = authOptions.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey))
    };
    o.Events = new JwtBearerEvents
    {
        OnChallenge = async ctx =>
        {
            ctx.HandleResponse();
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.ContentType = "application/json";
            var body = ApiResponse.Fail(ctx.AuthenticateFailure is SecurityTokenExpiredException ? "TOKEN_EXPIRED" : "UNAUTHORIZED",
                ctx.AuthenticateFailure is SecurityTokenExpiredException ? "Your session has expired. Please refresh the token." : "Authentication is required.");
            await ctx.Response.WriteAsJsonAsync(body);
        },
        OnForbidden = async ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(ApiResponse.Fail("FORBIDDEN", "You do not have permission to access this resource."));
        }
    };
});
builder.Services.AddAuthorization();

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (origins.Length == 0) p.AllowAnyOrigin();
    else p.WithOrigins(origins).AllowCredentials();
    p.AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("api-supported-versions");
}));

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

// ---- database migrate + seed ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    var appOptions = scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>().Value;
    if (appOptions.ImportContentOnStartup)
    {
        // Dev: ../../content/lessons relative to the project; IIS publish: content/lessons next to the DLL.
        var candidates = new[]
        {
            Path.IsPathRooted(appOptions.ContentPath) ? appOptions.ContentPath : Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, appOptions.ContentPath)),
            Path.Combine(app.Environment.ContentRootPath, "content", "lessons"),
            Path.Combine(AppContext.BaseDirectory, "content", "lessons"),
        };
        var contentPath = candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
        await scope.ServiceProvider.GetRequiredService<ContentSeeder>().ImportDirectoryAsync(contentPath);
    }
}

// ---- pipeline ----
// Request logging first so handled errors (e.g. 422 business rules) are logged with their real status, not as 500s.
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(o =>
{
    var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
    foreach (var d in provider.ApiVersionDescriptions)
        o.SwaggerEndpoint($"/swagger/{d.GroupName}/swagger.json", $"Tutor365 API {d.GroupName}");
    o.RoutePrefix = "swagger";
    o.DocumentTitle = "Tutor365 API";
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

public partial class Program { }
