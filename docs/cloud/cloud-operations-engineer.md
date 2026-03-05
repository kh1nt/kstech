# Deployment Documentation

Create a guide on how to set up the cloud solution deployment for KSTech where both website and database are deployed on MonsterASP.NET.

## Cloud Deployment Guide
This document describes the deployment architecture and step-by-step process for deploying the KSTech ASP.NET Core MVC application to MonsterASP.NET.

## Architecture Overview
- `Frontend + Backend`: ASP.NET Core MVC (`net8.0`) in a single IIS-hosted web app.
- `Hosting`: MonsterASP.NET (`runasp.net`) with Web Deploy.
- `Database`: MonsterASP SQL Server database.
- `ORM`: Entity Framework Core migrations.
- `Auth`: Cookie-based auth with optional Google OAuth.
- `Background Worker`: Hosted email outbox processor.
- `Integrations`: Brevo, eBay, Steam APIs.

---

# System Architecture
```text
User
  |
 HTTPS
  v
MonsterASP Website (kstech.runasp.net)
  |
EF Core / SQL Client (TLS)
  v
MonsterASP SQL Server Database
  |
Outbound HTTPS
  +--> Brevo API
  +--> eBay API
  +--> Steam API
```

---

# 1. Prepare the Project for Deployment
## Step 1: Restore and build
```powershell
dotnet restore .\kstech\kstech.csproj
dotnet build .\kstech\kstech.csproj -c Release
```

## Step 2: Prepare production configuration (non-secret files only)
Keep source-controlled `appsettings*.json` with placeholders only.

Set production values as MonsterASP environment variables:
```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection
Authentication__Google__ClientId
Authentication__Google__ClientSecret
Brevo__ApiKey
Ebay__ClientId
Ebay__ClientSecret
Steam__ApiKey
Cloudinary__Enabled
Cloudinary__CloudName
Cloudinary__ApiKey
Cloudinary__ApiSecret
Cloudinary__ReportFolder
Seed__SuperAdminEmail
Seed__SuperAdminPassword
```

## Step 3: Disable dev-only seeding in production
```text
Seed__EnableAutomaticSeeding=false
Seed__EnableInDevelopmentOnly=true
```

---

# 2. Create Website in MonsterASP
## Step 1: Create website
In MonsterASP Control Panel:
1. Open `Website`.
2. Click `Create Website`.
3. Set subdomain to `kstech`.
4. Save and wait for website provisioning.

## Step 2: Confirm website URL and publish target
Record the website endpoint (example: `kstech.runasp.net`), then:
1. Open `Deploy FTP/WebDeploy`.
2. Enable `WebDeploy Access`.
3. Download the publish profile file.

---

# 3. Provision MonsterASP SQL Database
## Step 1: Create database in MonsterASP
In MonsterASP Control Panel:
1. Open `Database`.
2. Click `Add Database`.
3. Create your target database.
4. Copy credentials.

Collect:
- SQL Server host name
- Database name
- SQL login username/password

## Step 2: Open SSMS and connect to both servers
1. Connect to your local SQL Server (source DB).
2. Connect to MonsterASP SQL Server (destination DB).

## Step 3: Generate script from local DB
1. Right-click local database in SSMS.
2. Select `Tasks` -> `Generate Scripts`.
3. Configure:
- `Script USE DATABASE` = `False`
- `Types of data to script` = `Schema and data`
4. Save generated script file (`.sql`).

## Step 4: Execute script on MonsterASP SQL Server
1. In MonsterASP SQL connection, open `New Query`.
2. Paste/open the generated script.
3. Execute script and wait for completion.

## Step 5: Update application connection string before publish
In MonsterASP environment variables, set:
```text
ConnectionStrings__DefaultConnection=<MonsterASP SQL connection string>
```

Also confirm:
```text
ASPNETCORE_ENVIRONMENT=Production
```

---

# 4. Deploy Website via Web Deploy (MSDeploy)
## Step 1: Get publish profile from MonsterASP
1. Open website `kstech` in MonsterASP.
2. Click `Deploy FTP/WebDeploy`.
3. Enable `WebDeploy Access`.
4. Download the publish profile.

## Step 2: Publish from Visual Studio using downloaded profile
1. Open solution `kstech.sln`.
2. Right-click project `kstech` > `Publish`.
3. Click `Import Profile`.
4. Select downloaded publish profile file.
5. Click `Publish`.

## Step 3: Validate publish output
Check app startup and ensure no runtime config error pages appear.

---

# 5. Post-Deployment Smoke Test
1. Open `https://kstech.runasp.net/`.
2. Verify login and store pages load.
3. Confirm DB operations:
- Create/update product data.
- Place a test order.
- Check activity logs.
4. Validate external API paths (if enabled):
- Brevo email queue processing.
- eBay market lookup.
- Steam data fetch.

---

# 6. Rollback and Recovery
## Application rollback
Re-publish last known good package/profile from Visual Studio.

## Database rollback
Restore backup or apply reverse migration SQL script.

## Recovery checklist
- Confirm app starts.
- Confirm DB connectivity.
- Re-run smoke tests.

---

# 7. Deployment Security Controls
- Use HTTPS-only endpoint.
- Limit Web Deploy credentials to trusted operators.
- Use least-privilege SQL user for runtime access.
- Keep secrets out of source control.
- Rotate API keys and credentials periodically.
- Keep database backup/snapshot before importing schema+data script and before each publish.
