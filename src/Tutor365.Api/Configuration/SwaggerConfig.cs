using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Tutor365.Api.Configuration;

public class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;
    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider) => _provider = provider;

    public void Configure(SwaggerGenOptions options)
    {
        foreach (var description in _provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(description.GroupName, new OpenApiInfo
            {
                Title = "Tutor365 API",
                Version = description.ApiVersion.ToString(),
                Description = "GCSE tutoring platform API. All responses use the envelope { success, data, message, errorCode, errors, traceId }. " +
                              "Authenticate with a Bearer JWT obtained from /api/v1/auth/login. Roles: Student, Parent, Admin."
            });
        }

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter the access token only (no 'Bearer ' prefix)."
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
        });

        var xml = Path.Combine(AppContext.BaseDirectory, "Tutor365.Api.xml");
        if (File.Exists(xml)) options.IncludeXmlComments(xml, includeControllerXmlComments: true);
        options.CustomSchemaIds(t => t.FullName!.Replace("Tutor365.Application.DTOs.", "").Replace("Tutor365.Application.Common.", "").Replace("+", "."));
        options.SupportNonNullableReferenceTypes();
    }
}
