# Policy and Access Matrix

Identify users/entities, their permissions, and why those permissions are granted in the MonsterASP.NET deployment model.

| Access Entity | Policy Attached | Justification |
| --- | --- | --- |
| MonsterASP Control Panel Account | Full management access to website settings, SQL Server database, and deployment configuration | Used for creating and managing the hosted website and database; should be restricted to authorized administrators only. |
| MonsterASP WebDeploy Profile User | Publish permission scoped to the created website (`kstech` subdomain) | Used by Visual Studio publish via downloaded MonsterASP profile; should be restricted to release operators only. |
| MonsterASP SQL Login (runtime) | App DB access (`SELECT`, `INSERT`, `UPDATE`, `DELETE`, `EXECUTE`) for KSTech database only | Required for normal ASP.NET runtime operations while avoiding server-wide SQL admin rights. |
| MonsterASP SQL Login (deployment) | Temporary elevated rights only while executing generated schema+data script in SSMS | Supports first-time DB import and major releases; privileges should be reduced after deployment window. |
| Third-Party API Credentials (Brevo, eBay, Steam, Cloudinary, Google) | API keys and secrets scoped to each service's specific function (email delivery, market data, game metadata, report archival, staff login) | Each integration uses its own credentials stored in configuration. Keys should be kept in environment variables and rotated regularly to limit exposure. |

---

# Threat Model (STRIDE)

Using the STRIDE model, identify possible vulnerabilities in your application and deployment strategy as well as mitigations to prevent exposure of data.

| Threat | Vulnerability | Justification |
| --- | --- | --- |
| Spoofing | Partially Vulnerable | If authentication cookies are stolen or credentials are weak, attackers could impersonate legitimate users. BCrypt hashing, login lockout, and separate admin/customer cookie schemes reduce this risk. |
| Tampering | Partially Vulnerable | Data in transit could be modified if HTTPS is not enforced between the client and MonsterASP. EF Core parameterized queries and role-based checks reduce SQL injection and unauthorized modification risk. |
| Repudiation | Partially Vulnerable | Without complete audit retention, users could deny performing actions like order changes or deletions. The system logs events to `SystemLogs`, but immutable backup and monitoring would strengthen this. |
| Information Disclosure | Vulnerable | API keys stored in `appsettings.json` could be leaked through source control. Using MonsterASP environment variables for secrets and least-privilege SQL accounts significantly lowers exposure. |
| Denial of Service (DoS) | Vulnerable | The shared hosting on MonsterASP could be overwhelmed by high traffic or malicious requests. Without rate limiting or a WAF, the application is susceptible to service disruption. |
| Elevation of Privilege | Low Vulnerability | If role checks are enforced and SQL credentials follow least privilege, escalation risk is minimal. Owner-scope enforcement and tenant isolation via `OwnerUserID` limit cross-tenant access. |

---

# Security Hardening Checklist (MonsterASP Deployment)
- Set `Security:EnforceHttpsOnly` to `true` to enable HTTPS redirection and HSTS.
- Move all secrets (API keys, connection strings, credentials) to MonsterASP environment variables instead of `appsettings*.json`.
- Rotate Google, Brevo, eBay, Steam, Cloudinary, and admin credentials before production cutover.
- Use dedicated SQL users (runtime vs deployment) with minimal permissions.
- Back up database before each deployment.
- Restrict MonsterASP control panel and WebDeploy credentials to authorized administrators only.
- Monitor login failures, lockouts, and abnormal admin activity via `SystemLogs`.
