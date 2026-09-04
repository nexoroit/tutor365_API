# Tutor365 API

ASP.NET Core 8 Web API for the Tutor365 GCSE tutoring platform (Years 9–11; Maths, English Language, English Literature, Biology, Chemistry, Physics).

## Solution layout

```
src/
  Tutor365.Domain          entities, enums, domain exceptions
  Tutor365.Application     DTOs, validators, service interfaces + implementations
  Tutor365.Infrastructure  EF Core (SQL Server), migrations, seed data, JWT, SMTP, AI provider
  Tutor365.Api             controllers, middleware, Swagger, Program.cs
tests/
  Tutor365.UnitTests
  Tutor365.IntegrationTests
```

## Run locally

Requires the .NET 8 SDK.

```bash
dotnet build
cd src/Tutor365.Api
dotnet run
```

Swagger UI: http://localhost:5293/swagger  ·  Health: http://localhost:5293/health

On start-up the API applies pending EF migrations and seeds reference data (exam boards, year groups, subjects, AQA curriculum, system settings, default admin). Seeding is idempotent.

## Configuration (`appsettings.json`)

| Section | Purpose |
|---|---|
| `ConnectionStrings:Default` | SQL Server (shared dev/prod DB, port 11433) |
| `Auth` | JWT issuer/audience/signing key, token lifetimes, OTP and lockout settings |
| `Smtp` | OTP / notification email. `Enabled:false` logs emails instead of sending |
| `AI` | `Provider: Stub` until a vendor is chosen. Keys never reach the frontend |
| `App` | Frontend URL, support email, seeded admin credentials |
| `Cors:AllowedOrigins` | Angular dev origins |

Override secrets on the server with environment variables, e.g. `Auth__SigningKey`, `ConnectionStrings__Default`, `Smtp__Password`.

## Roles

`Student`, `Parent`, `Admin`. There is no tutor role: the platform is the tutor.

- Parents self-register (`POST /api/v1/auth/register`) and verify with a 6-digit emailed OTP.
- Parents create child accounts (`POST /api/v1/parents/me/children`); children log in with their own email/password and can reset via OTP.
- Admin is seeded from `App:AdminEmail` / `App:AdminPassword` with `mustChangePassword = true`.

## Response envelope

Every endpoint returns:

```json
{ "success": true, "data": { ... }, "message": null, "errorCode": null, "errors": null, "traceId": "..." }
```

Errors use the same shape with `success:false`, an `errorCode` such as `VALIDATION_FAILED`, `INVALID_CREDENTIALS`, `EMAIL_NOT_VERIFIED`, `INVALID_OTP`, `FORBIDDEN`, `*_NOT_FOUND`, and optional field-level `errors`.

## Migrations

```bash
dotnet tool install --global dotnet-ef --version 8.0.11
dotnet ef migrations add <Name> -p src/Tutor365.Infrastructure -s src/Tutor365.Api -o Data/Migrations
```

## Content policy

Lessons and questions are original platform content. `CurriculumMappings` only stores the exam-board spec reference and the CGP book/section reference for alignment; no book text is stored.
