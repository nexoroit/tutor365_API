using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Common;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment env)
    {
        _next = next; _logger = logger; _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try { await _next(context); }
        catch (Exception ex) { await HandleAsync(context, ex); }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var traceId = context.TraceIdentifier;
        HttpStatusCode status;
        ApiResponse body;

        switch (ex)
        {
            case AppValidationException v:
                status = HttpStatusCode.BadRequest;
                body = ApiResponse.Fail(v.ErrorCode, v.Message, v.Errors);
                break;
            case FluentValidation.ValidationException fv:
                status = HttpStatusCode.BadRequest;
                var errors = fv.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => ToCamel(g.Key), g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
                body = ApiResponse.Fail("VALIDATION_FAILED", "One or more validation errors occurred.", errors);
                break;
            case NotFoundException nf:
                status = HttpStatusCode.NotFound; body = ApiResponse.Fail(nf.ErrorCode, nf.Message); break;
            case UnauthorizedException ua:
                status = HttpStatusCode.Unauthorized; body = ApiResponse.Fail(ua.ErrorCode, ua.Message); break;
            case ForbiddenException fb:
                status = HttpStatusCode.Forbidden; body = ApiResponse.Fail(fb.ErrorCode, fb.Message); break;
            case ConflictException cf:
                status = HttpStatusCode.Conflict; body = ApiResponse.Fail(cf.ErrorCode, cf.Message); break;
            case BusinessRuleException br:
                status = HttpStatusCode.UnprocessableEntity; body = ApiResponse.Fail(br.ErrorCode, br.Message); break;
            case OperationCanceledException:
                status = (HttpStatusCode)499; body = ApiResponse.Fail("REQUEST_CANCELLED", "The request was cancelled."); break;
            case DbUpdateConcurrencyException:
                status = HttpStatusCode.Conflict; body = ApiResponse.Fail("CONCURRENCY_CONFLICT", "The record was modified by someone else. Please refresh and try again."); break;
            default:
                status = HttpStatusCode.InternalServerError;
                _logger.LogError(ex, "Unhandled exception {TraceId} {Method} {Path}", traceId, context.Request.Method, context.Request.Path);
                body = ApiResponse.Fail("INTERNAL_ERROR", _env.IsDevelopment() ? ex.Message : "Something went wrong. Please try again.");
                break;
        }

        if (status != HttpStatusCode.InternalServerError)
            _logger.LogWarning("Handled {ErrorCode} ({Status}) {TraceId} {Method} {Path}: {Message}", body.ErrorCode, (int)status, traceId, context.Request.Method, context.Request.Path, ex.Message);

        body.TraceId = traceId;
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }

    private static string ToCamel(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
