# KSTech Cloud Deployment Guide (MonsterASP.NET)

This document describes the deployment architecture and step-by-step process for deploying the KSTech ASP.NET Core application and SQL Server database to MonsterASP.NET.

## Architecture Overview

- `Application`: ASP.NET Core MVC (`net8.0`) deployed to MonsterASP IIS.
- `Database`: SQL Server database hosted in MonsterASP.
- `Authentication`: Cookie authentication (Admin/Customer) with optional Google OAuth.
- `API’s`: Brevo API, eBay Browse API, Steam API.

## System Architecture

User Browser
    |
 HTTPS
    v
MonsterASP Website (kstech.runasp.net)
ASP.NET Core MVC App (KSTech)
    |
MonsterASP SQL Server Database
    v
Outbound HTTPS
    +--> Brevo API
    +--> eBay API
    +--> Steam API
    +--> Cloudinary API (File Storage)

## 1. Prerequisites
- MonsterASP account with:
  - Active web hosting site (`runasp.net`).
  - SQL Server database created.
  - Web Deploy credentials.
- Visual Studio 2022 (or newer) with ASP.NET and Web Deploy support.
- .NET 8 SDK installed.
- Access to this project source code.


## 2. Prepare Production Configuration

### 2.1 Move secrets out of source-controlled settings
Store production secrets as hosting environment variables or a non-committed production settings file. 


2.1.1 Configure environment variables in MonsterASP (Scripting)

1.	Login to MonsterASP Control Panel
2.	Go to: Websites → (select your site) → Scripting
3.	Add the required environment variables using the key format supported by ASP.NET Core, where nested config uses double underscores <__>.

Required keys:
- ‘ASPNETCORE_ENVIRONMENT=Production’
- `ConnectionStrings__DefaultConnection`
- `Authentication__Google__ClientId`
- `Authentication__Google__ClientSecret`
- `Brevo__ApiKey`
- `Ebay__ClientId`
- `Ebay__ClientSecret`
- `Steam__ApiKey`
- ‘Cloudinary__ApiKey’
- ‘Cloudinary__ApiSecret’


### 2.2 Production connection string

Server=tcp:<monsterasp-sql-server>,1433;Initial Catalog=<database-name>;User ID=<db-user>;Password=<db-password>;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=True;


## 3. Create Website in MonsterASP

### 3.1 Create website and subdomain
1. Log in to MonsterASP Control Panel.
2. Open `Website`.
3. Click `Create Website`.
4. Set subdomain to `kstech`.
5. Save and wait until the website is provisioned.
6. Confirm your URL (`kstech.runasp.net`).

### 3.2 Collect publish details
1. Open your created website in MonsterASP.
2. Click `Deploy FTP/WebDeploy`.
3. Enable `WebDeploy Access`.
4. Download the publish profile file.
5. Keep the downloaded profile because it will be used in Visual Studio publish.


## 4. Prepare the MonsterASP SQL Database (Your SSMS Flow)

### 4.1 Create database on MonsterASP
1. Log in to MonsterASP Control Panel.
2. Open `Database`.
3. Click `Add Database`.
4. Create the database.
5. Copy the SQL Server details:
- Server name
- Database name
- SQL username
- SQL password

### 4.2 Connect to SQL Server in SSMS
1. Open SQL Server Management Studio (SSMS).
2. Connect to your local SQL Server instance (where the current `kstechdb` exists).
3. Connect to the MonsterASP SQL Server using the copied credentials.

### 4.3 Generate script from local database
1. In SSMS, right-click your local database.
2. Select `Tasks` -> `Generate Scripts`.
3. In scripting options set:
- `Script USE DATABASE` = `False`
- `Types of data to script` = `Schema and data`
4. Generate and save the `.sql` script file.

### 4.4 Run script on MonsterASP database
1. In SSMS, go to the MonsterASP SQL Server connection.
2. Right-click the server database and open `New Query`.
3. Paste/open your generated SQL script.
4. Execute the script until completion without errors.

## 5. Update App Connection String Before Publish

Edit `kstech/appsettings.json` and set `ConnectionStrings:DefaultConnection` to the MonsterASP SQL Server connection string.

Example:
Server=tcp:<monsterasp-sql-server>,1433;Initial Catalog=<database-name>;User ID=<db-user>;Password=<db-password>;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=True;


## 6. Deploy Website to MonsterASP (Web Deploy)

### 6.1 Enable WebDeploy and download publish profile
1. In MonsterASP, open your website (`kstech`).
2. Click `Deploy FTP/WebDeploy`.
3. Enable `WebDeploy Access` (if not enabled yet).
4. Download the publish profile.

### 6.2 Publish from Visual Studio using the downloaded profile
1. Open `kstech.sln`.
2. Right-click the `kstech` project.
3. Select `Publish`.
4. Select `Import Profile`.
5. Choose the downloaded MonsterASP publish profile file.
6. Click `Publish`.

### 6.3 Optional CLI build validation before publish

dotnet restore .\kstech\kstech.csproj
dotnet build .\kstech\kstech.csproj -c Release
dotnet publish .\kstech\kstech.csproj -c Release

## 7. Post-Deployment Verification
1. Browse `https://kstech.runasp.net/ or ‘http://kstech.runasp.net `.
2. Verify login page and admin page load.
3. Validate DB connectivity by executing a read/write flow:
- Register or login.
- Create/update a sample record (product/customer/order).
4. Check critical features:
- Google login (if configured).
- Email outbox processing.
- Inventory and reporting screens.
5. Confirm logs are being generated and no startup exceptions occur.

## 8. Security Checklist for Production
- Enforce HTTPS-only access.
- Rotate all API keys and passwords before go-live.
- Use least-privilege SQL account for runtime.
- Keep backups/snapshots before each deployment.
- Restrict who can use Web Deploy credentials.

## 9. Additional Documentation
- `docs/cloud/cloud-solutions-architect.md`
- `docs/cloud/cloud-security-engineer.md`
- `docs/cloud/cloud-data-engineer.md`
