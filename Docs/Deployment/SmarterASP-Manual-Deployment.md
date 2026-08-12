# SmarterASP.NET Manual Deployment Guide

## Purpose

This guide documents a manual deployment process for the VocabularyApp repository to SmarterASP.NET from a clean checkout. It is based on the repository structure and configuration present in source control as of this revision.

## 1. Prerequisites

### Required SDKs and Tools

- .NET SDK: .NET 8 SDK is required to build and publish the backend. The backend projects target `net8.0`.
- Node.js: Node.js 20.x is recommended. This workspace successfully built with Node `v20.19.0`.
- npm: npm 10.x is recommended. This workspace successfully built with npm `10.8.2`.
- Angular CLI: The repo includes Angular CLI as a local dev dependency, so a global Angular CLI install is optional. Use `npx ng` or the package scripts if Angular CLI is not installed globally.
- Git: required to clone the repository.
- Optional EF Core CLI: recommended if you want to apply migrations with `dotnet ef`. If not already installed, use `dotnet tool install --global dotnet-ef`.

### Required SmarterASP.NET Hosting Features

- Windows hosting with IIS.
- ASP.NET Core hosting support for .NET 8 applications.
- Ability to run an ASP.NET Core application through `AspNetCoreModuleV2`.
- SQL Server database hosting, or access to an external SQL Server instance reachable from SmarterASP.NET.
- FTP access or File Manager access.
- Access to the SmarterASP.NET control panel for connection strings, app pool/runtime settings, and site path configuration.

### Required Database Setup

- A SQL Server database is required.
- The development connection strings in source use LocalDB and are not valid for SmarterASP.NET.
- The database can be empty before deployment if EF Core migrations are applied successfully.
- The application relies on EF Core migrations and includes committed migrations in `VocabularyApp.Data/Migrations`.

### Required Access

- Repository checkout access.
- SmarterASP.NET FTP credentials or File Manager.
- SmarterASP.NET database connection details.
- Permission to create or update application settings and connection strings in hosting.

## 2. Repository Overview

### Solution and Projects

- Solution file: `VocabularyApp.sln`
- Backend/API project: `VocabularyApp.WebApi/VocabularyApp.WebApi.csproj`
- Data project: `VocabularyApp.Data/VocabularyApp.Data.csproj`
- Frontend project: `VocabularyApp.UI/package.json`

### Startup Project

- Startup project: `VocabularyApp.WebApi`
- Entry point: `VocabularyApp.WebApi/Program.cs`

### Dependency Layout

- `VocabularyApp.WebApi` references `VocabularyApp.Data`.
- `VocabularyApp.Data` contains `ApplicationDbContext`, EF Core models, and migrations.
- `VocabularyApp.UI` is a separate Angular application that must be built and then deployed as static files.

### Hosting Shape Used by This Repository

This repository is configured so the ASP.NET Core backend serves the Angular frontend as static files:

- API routes are under `/api/...`
- Angular production environment points to `/api`
- The backend enables static files and a fallback route to `index.html`

This means the intended production deployment is a single IIS site containing:

- The published ASP.NET Core backend
- The built Angular frontend files copied into the backend site's static content folder

## 3. Configuration Requirements

### Backend Configuration Files

The backend includes:

- `VocabularyApp.WebApi/appsettings.json`
- `VocabularyApp.WebApi/appsettings.Development.json`

The publish output also includes these files unless you remove them during packaging.

### Connection String Requirements

Source-controlled connection strings currently point to LocalDB:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=VocabularyAppDb;Trusted_Connection=true;MultipleActiveResultSets=true"
}
```

This must be replaced for SmarterASP.NET with a real SQL Server connection string, typically using SQL authentication.

Example shape:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=YOUR_SQL_SERVER;Database=YOUR_DATABASE;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

### JWT / Security Settings

The backend reads JWT settings from the `JwtSettings` section:

```json
"JwtSettings": {
  "SecretKey": "...",
  "Issuer": "VocabularyApp",
  "Audience": "VocabularyApp",
  "ExpirationMinutes": "60"
}
```

Requirements:

- `SecretKey` must be replaced with a strong production secret.
- `Issuer` and `Audience` must be set consistently for token generation and validation.
- Do not keep development JWT secrets in production.

### CORS Settings

The backend reads CORS origins from:

```json
"Cors": {
  "AllowedOrigins": [ ... ]
}
```

Important detail:

- If the Angular frontend and API are deployed under the same domain and site, CORS may not be needed for browser calls because the frontend uses a relative `/api` URL.
- If you serve the frontend from a different domain, subdomain, or separate site, you must add that exact origin to `Cors:AllowedOrigins`.

### Angular API Base URL

Angular environment files:

- Development: `VocabularyApp.UI/src/environments/environment.ts`
- Production: `VocabularyApp.UI/src/environments/environment.prod.ts`

Configured values:

- Development API URL: `http://localhost:5190/api`
- Production API URL: `/api`

This production setting assumes the frontend is served by the same site as the backend.

### Values That Should Not Be Committed

These should not be committed with production values:

- Production SQL connection strings
- Production database passwords
- Production JWT secrets
- Any SmarterASP.NET FTP credentials
- Any control-panel-generated publish credentials

Note: the repository currently contains development-style JWT secrets and LocalDB connection strings in appsettings files. Those should not be reused for production.

## 4. Build Steps

Run all commands from a clean checkout at the repository root unless noted otherwise.

### Restore NuGet Packages

```powershell
dotnet restore .\VocabularyApp.sln
```

### Restore npm Packages

```powershell
Set-Location .\VocabularyApp.UI
npm install
Set-Location ..
```

### Build the Angular Frontend

```powershell
Set-Location .\VocabularyApp.UI
npm run build
Set-Location ..
```

What this does:

- Runs the Angular production build.
- Uses `environment.prod.ts`.
- Produces static assets in `VocabularyApp.UI/dist/vocabulary-app.ui/browser`.

### Prepare the Backend Static Files

This repository does not automatically copy Angular build output into the backend publish output.

Before publishing the backend, copy the Angular browser files into the backend `wwwroot` folder.

Example PowerShell commands:

```powershell
Remove-Item .\VocabularyApp.WebApi\wwwroot\* -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item .\VocabularyApp.UI\dist\vocabulary-app.ui\browser\* .\VocabularyApp.WebApi\wwwroot\ -Recurse -Force
```

If the `wwwroot` folder does not exist, create it first:

```powershell
New-Item -ItemType Directory -Force -Path .\VocabularyApp.WebApi\wwwroot
```

### Build the .NET Backend

```powershell
Set-Location .\VocabularyApp.WebApi
dotnet build -c Release
Set-Location ..
```

### Publish the .NET Backend

```powershell
Set-Location .\VocabularyApp.WebApi
dotnet publish -c Release -o .\publish
Set-Location ..
```

Important:

- Publish only after copying the Angular built files into `VocabularyApp.WebApi/wwwroot`.
- Otherwise the published site will contain the API only and the frontend will not load.

### Optional Clean Rebuild Sequence

If you want a strict clean deployment package build:

```powershell
dotnet restore .\VocabularyApp.sln
Set-Location .\VocabularyApp.UI
npm install
npm run build
Set-Location ..
New-Item -ItemType Directory -Force -Path .\VocabularyApp.WebApi\wwwroot | Out-Null
Remove-Item .\VocabularyApp.WebApi\wwwroot\* -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item .\VocabularyApp.UI\dist\vocabulary-app.ui\browser\* .\VocabularyApp.WebApi\wwwroot\ -Recurse -Force
Set-Location .\VocabularyApp.WebApi
dotnet publish -c Release -o .\publish
Set-Location ..
```

## 5. Publish Output

### Angular Build Output

Angular build output is created in:

- `VocabularyApp.UI/dist/vocabulary-app.ui/browser`

Observed contents include:

- `index.html`
- bundled JavaScript files
- bundled CSS files
- `favicon.ico`
- Angular IIS rewrite `web.config`

### .NET Publish Output

.NET publish output is created in:

- `VocabularyApp.WebApi/publish`

Expected contents include:

- `VocabularyApp.WebApi.exe`
- `VocabularyApp.WebApi.dll`
- `VocabularyApp.WebApi.runtimeconfig.json`
- `web.config`
- `appsettings.json`
- dependency DLLs
- any files present under `VocabularyApp.WebApi/wwwroot` at publish time

### What Needs To Be Uploaded

Upload the contents of the backend publish folder after Angular files have been copied into backend `wwwroot` and the backend has been published.

Recommended upload source:

- Everything inside `VocabularyApp.WebApi/publish`

### What Should Not Be Uploaded

Do not upload:

- `.git/`
- source code folders such as `VocabularyApp.UI/src` or `VocabularyApp.WebApi/Controllers`
- `node_modules`
- `bin` and `obj` folders from source projects
- `VocabularyApp.UI/dist` by itself unless you are intentionally doing a split deployment
- local development logs
- local database files
- `.vs` or `.vscode`

## 6. SmarterASP.NET Deployment Steps

### Recommended Deployment Model

Deploy as a single ASP.NET Core site with Angular static files included in the backend publish output.

This is the best match for the repository because:

- Angular production uses `/api`
- The backend serves static files
- The backend uses `MapFallbackToFile("{*path:nonfile}", "index.html")`

### Step-by-Step Deployment

1. Build the Angular frontend.
2. Copy Angular browser files into `VocabularyApp.WebApi/wwwroot`.
3. Publish the backend.
4. In SmarterASP.NET, open the site root for the ASP.NET Core app.
5. Upload everything from `VocabularyApp.WebApi/publish` into that site root.
6. Confirm that the uploaded root contains both:
   - `VocabularyApp.WebApi.exe` and related backend files
   - a `wwwroot` folder with Angular assets including `index.html`

### Where To Upload the Backend/API Files

Upload to the IIS application root for the site or virtual application that SmarterASP.NET runs as your ASP.NET Core app.

Typical expectation:

- Site root contains `web.config`, the `.exe`, `.dll`, `appsettings*.json`, and `wwwroot`

### Where To Upload the Frontend Files

Do not upload the Angular app as a separate site unless you intentionally redesign the deployment.

Instead:

- Copy Angular output into `VocabularyApp.WebApi/wwwroot`
- Publish the backend
- Upload the single backend publish folder

### How To Handle `web.config`

There are two different `web.config` files in this repository:

- `VocabularyApp.UI/public/web.config`: Angular rewrite rules for static IIS hosting
- `VocabularyApp.WebApi/publish/web.config`: ASP.NET Core hosting configuration for IIS

For this deployment model:

- The root `web.config` that matters is the ASP.NET Core backend `web.config` from the publish output.
- The Angular `web.config` may be copied into `wwwroot` as a static file, but IIS will use the site root `web.config` for the application.

Do not replace the root ASP.NET Core `web.config` with the Angular-only `web.config`.

### Application Path / Site Folder

Preferred deployment target:

- Deploy the app at the site root.

Reasons:

- Angular production base URL is written for root deployment.
- Angular production API URL is `/api`.
- Backend Swagger endpoint pathing is also easiest at root deployment.

If you must deploy to a subfolder:

- Rebuild Angular with `--base-href /your-subfolder/`
- Verify the backend application is also mounted at that same subfolder
- Re-test all routes and static assets carefully

### Database Connection String Configuration

Preferred approach:

- Update `appsettings.json` in the publish output before upload, or
- Configure the connection string through SmarterASP.NET if the hosting panel supports ASP.NET Core app settings / connection strings injection

If using hosting panel settings, verify that the configuration is exposed to ASP.NET Core the same way the app expects.

The application expects:

- `ConnectionStrings:DefaultConnection`

### IIS / ASP.NET Core Hosting Requirements

Required:

- ASP.NET Core Hosting Bundle support on the server side
- `AspNetCoreModuleV2`
- Support for in-process hosting, because the published `web.config` uses `hostingModel="inprocess"`

The published root `web.config` currently points to:

```xml
<aspNetCore processPath=".\VocabularyApp.WebApi.exe" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess" />
```

## 7. Database Steps

### EF Core Migrations

The repository includes committed migrations under `VocabularyApp.Data/Migrations`.

This indicates the intended schema deployment path is EF Core migrations.

### Does the Database Need To Already Exist?

- The SQL Server instance and credentials must exist.
- The actual database may be created by EF Core during migration, depending on permissions.
- On shared hosting, it is often safer to create the database in SmarterASP.NET first, then run migrations against it.

### How To Apply Migrations

Preferred approach from a developer workstation before or during deployment:

1. Update the backend connection string to point to the SmarterASP.NET SQL Server database.
2. Run EF Core database update from the repository root or startup project.

Example command:

```powershell
dotnet ef database update --project .\VocabularyApp.Data\VocabularyApp.Data.csproj --startup-project .\VocabularyApp.WebApi\VocabularyApp.WebApi.csproj
```

If `dotnet ef` is not available:

```powershell
dotnet tool install --global dotnet-ef
```

### Manual SQL Steps

No full standalone SQL deployment script is included in the repository.

However, some migrations contain manual SQL statements inside the migration itself, including:

- data correction for `UserWords.PartOfSpeechId`
- backfill of `QuizResults.QuizSessionId`

That means applying the EF Core migrations is important, not just manually creating tables.

### Seed Data Requirements

The application seeds `PartsOfSpeech` records through EF Core model seeding in `ApplicationDbContext`.

Expected seed data includes 8 parts of speech records such as:

- Noun
- Verb
- Adjective
- Adverb
- Pronoun
- Preposition
- Conjunction
- Interjection

No other explicit seed data requirements were confirmed from the repository.

## 8. Post-Deployment Verification

### Confirm the Frontend Loads

Open the deployed site root in a browser.

Expected result:

- Angular app loads.
- Browser dev tools show successful loads for `index.html`, JS bundles, and CSS.

If the page is blank or returns 500, check the troubleshooting section.

### Confirm the API Is Running

Test these endpoints in a browser, REST client, or Postman:

- `GET /swagger`
- `POST /api/users/register`
- `POST /api/users/login`
- `GET /api/words/lookup/hello`

If Swagger is enabled in production, `/swagger` should load.

### Confirm Login / Register Works

1. Register a new user.
2. Log in with that user.
3. Verify a JWT token is returned.
4. Verify the Angular app stores auth state in browser local storage.

### Confirm Database Connectivity

Indicators that database connectivity is working:

- User registration succeeds.
- Login succeeds.
- Vocabulary add/search operations succeed.
- API logs do not show SQL connection exceptions.

### Confirm Angular Is Pointing To the Correct API URL

Use browser dev tools network tab.

Expected behavior:

- API requests go to `/api/...` on the same domain as the Angular app.

If API requests go to localhost or another host, the wrong frontend build was deployed.

## 9. Troubleshooting

### 500 Errors

Possible causes:

- Invalid connection string
- Missing or invalid JWT settings
- ASP.NET Core runtime mismatch on host
- Missing files from publish output
- App failed to start under IIS

Actions:

- Confirm the site root contains the full backend publish output.
- Confirm the SmarterASP.NET plan supports ASP.NET Core and the required runtime.
- Temporarily enable stdout logging in the root `web.config` if needed.
- Check SmarterASP.NET error logs or application logs in the control panel.

### Missing `web.config`

If the site root does not include the published backend `web.config`, IIS will not know how to start the ASP.NET Core app.

Fix:

- Re-upload the complete contents of `VocabularyApp.WebApi/publish`.

### Wrong API URL

Symptoms:

- frontend loads, but login or data calls fail
- network requests target `localhost`

Fix:

- Rebuild Angular with the production environment.
- Confirm `environment.prod.ts` was used.
- Confirm deployed JS bundles were generated by `npm run build` and not copied from a development server scenario.

### CORS Errors

Symptoms:

- browser console shows blocked cross-origin requests

Fix:

- If frontend and backend are on different origins, add the frontend origin to `Cors:AllowedOrigins`.
- If deployed as one site, verify requests are actually going to the same origin using `/api`.

### Database Connection Failures

Symptoms:

- user registration/login fails
- SQL exceptions in logs

Fix:

- Replace LocalDB connection strings with SmarterASP.NET SQL Server values.
- Confirm database server, database name, username, and password.
- Confirm the database user has permission to create/update schema if you run migrations.

### Angular Routing Refresh Errors

Symptoms:

- direct navigation to a client route returns 404

The backend already contains Angular fallback routing support through ASP.NET Core.

Fix:

- Confirm Angular files are under backend `wwwroot`.
- Confirm the app was published after copying Angular files.
- Confirm the root site is running the ASP.NET Core app, not static-only hosting.

### File Permission Issues

Symptoms:

- uploads succeed but app fails to start
- logs folder cannot be written if stdout logging is enabled

Fix:

- Check SmarterASP.NET file permissions and writable folders.
- If enabling stdout logs, ensure the target log folder can be created or written.

### SmarterASP.NET-Specific Issues

Check these items in the SmarterASP.NET control panel:

- installed ASP.NET Core runtime support
- IIS application path / virtual directory configuration
- SQL Server hostname and credentials
- whether app settings / connection strings can be injected via hosting settings
- whether site root is the actual IIS application root

## 10. Assumptions and Open Questions

### Assumptions Confirmed From Repository

- The backend targets .NET 8.
- The frontend targets Angular 18.
- The intended production frontend API URL is `/api`.
- The backend is intended to serve the Angular frontend as static files.
- EF Core migrations are the intended schema deployment mechanism.

### Items That Could Not Be Fully Confirmed From Repository

- Which exact SmarterASP.NET plan is being used.
- Whether that plan supports .NET 8 in-process ASP.NET Core hosting.
- Whether the database is provisioned as SQL Server on the same host or external.
- Whether the site will be deployed at root or under a virtual directory.
- Whether configuration values will be supplied via `appsettings.json` or hosting panel environment settings.
- Whether production Swagger exposure is acceptable from a security standpoint.

### Items To Check In the SmarterASP.NET Control Panel

- ASP.NET Core runtime version availability
- IIS application root folder
- database server name, database name, username, and password
- FTP destination path
- connection string configuration mechanism
- log viewer / failed request diagnostics availability

## Recommended Manual Deployment Workflow

Use this as the shortest reliable deployment checklist:

1. Clone the repo.
2. Run `dotnet restore .\VocabularyApp.sln`.
3. Run `npm install` in `VocabularyApp.UI`.
4. Run `npm run build` in `VocabularyApp.UI`.
5. Copy `VocabularyApp.UI/dist/vocabulary-app.ui/browser/*` into `VocabularyApp.WebApi/wwwroot/`.
6. Update backend production configuration with the real SQL Server connection string and real JWT secret.
7. Apply EF Core migrations to the production database.
8. Run `dotnet publish -c Release -o .\publish` in `VocabularyApp.WebApi`.
9. Upload everything from `VocabularyApp.WebApi/publish` to the SmarterASP.NET site root.
10. Verify the site root loads, `/swagger` loads, and register/login works.

## Files Inspected For This Guide

- `README.md`
- `FRONTEND-SUMMARY.md`
- `VocabularyApp.sln`
- `VocabularyApp.WebApi/VocabularyApp.WebApi.csproj`
- `VocabularyApp.Data/VocabularyApp.Data.csproj`
- `VocabularyApp.WebApi/Program.cs`
- `VocabularyApp.WebApi/appsettings.json`
- `VocabularyApp.WebApi/appsettings.Development.json`
- `VocabularyApp.WebApi/Properties/launchSettings.json`
- `VocabularyApp.WebApi/publish/web.config`
- `VocabularyApp.UI/package.json`
- `VocabularyApp.UI/angular.json`
- `VocabularyApp.UI/README.md`
- `VocabularyApp.UI/src/environments/environment.ts`
- `VocabularyApp.UI/src/environments/environment.prod.ts`
- `VocabularyApp.UI/src/app/services/api.service.ts`
- `VocabularyApp.UI/src/app/services/auth.service.ts`
- `VocabularyApp.UI/public/web.config`
- `VocabularyApp.Data/ApplicationDbContext.cs`
- `VocabularyApp.Data/Migrations/*`
- `test-api.http`
