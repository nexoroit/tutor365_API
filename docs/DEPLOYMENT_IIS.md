# Deploying Tutor365 API to IIS

## Server prerequisites
1. Windows Server with IIS and the **.NET 8 Hosting Bundle** (https://dotnet.microsoft.com/download/dotnet/8.0 → "Hosting Bundle"). Restart IIS after installing (`iisreset`).
2. Outbound access from the server to SQL Server `69.197.174.29,11433` and to your SMTP host.

## Publish
```bash
dotnet publish src/Tutor365.Api -c Release -o publish
```
The publish folder contains `Tutor365.Api.dll`, `web.config`, and `content/lessons/**` (lesson JSON is copied so the import runs on the server).

## Configure
- Copy `appsettings.Production.template.json` to `appsettings.Production.json` in the publish folder and fill in secrets, **or** set environment variables on the IIS app pool / site (preferred):
  `ConnectionStrings__Default`, `Auth__SigningKey`, `App__AdminPassword`, `Smtp__Password`, `Cors__AllowedOrigins__0`.
- `web.config` sets `ASPNETCORE_ENVIRONMENT=Production` and in-process hosting.

## IIS site
1. Create an Application Pool: .NET CLR version **No Managed Code**, Integrated pipeline. Give its identity write access to the site's `Logs` folder.
2. Create a Website (or application) pointing at the publish folder, bind HTTPS (443) with your certificate.
3. Browse `https://api.your-domain.com/health` → `Healthy`; `https://api.your-domain.com/swagger` → API docs.

On first start the app applies EF migrations, seeds reference data and imports lesson content. Re-import content later with `POST /api/v1/admin/content/import`.

## Updating
Stop the app pool, copy the new publish output over the folder (keep `appsettings.Production.json`), start the pool. Migrations run automatically.

## Frontend
Add the Angular origin to `Cors:AllowedOrigins`. The frontend only needs the API base URL; no secrets.
