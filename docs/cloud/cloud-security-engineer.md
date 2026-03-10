# Policy and Access Matrix

Identify users/entities, their permissions, and why those permissions are granted in the MonsterASP.NET deployment model.

| Access Entity | Policy Attached | Justification |
| --- | --- | --- |
| MonsterASP Control Panel Account | Full management access to website settings, SQL Server database, and deployment configuration | Required to create and manage the hosted website, database, and deployment settings. Access should be limited to authorized administrators only. |
| MonsterASP WebDeploy Profile User | Publish permission scoped to the `kstech` website only | Used by Visual Studio publish via downloaded MonsterASP WebDeploy profile. Access should be limited to release operators only. |
| MonsterASP SQL Login (Single Account) | DB-scoped access for KSTech database (`SELECT`, `INSERT`, `UPDATE`, `DELETE`, `EXECUTE`, plus required schema actions during approved releases) | Simplifies project operations while keeping access limited to one database. Credentials must be strong, rotated, and not stored in source control. |
| Third-Party API Credentials (Brevo, eBay, Steam, Cloudinary, Google) | API keys/secrets scoped per integration function (email delivery, market data, game metadata, report archival, OAuth login) | Separate credentials per service reduce blast radius and support safer key rotation through environment variables. |

# Threat Model

Using the STRIDE model, identify possible vulnerabilities in your application and deployment strategy as well as mitigations to prevent exposure of data.

| Threat | Vulnerability | Justification |
| --- | --- | --- |
| Spoofing | Partially Vulnerable | Attackers can try to impersonate valid users through stolen credentials or session hijacking. The system reduces this risk with BCrypt password hashing, failed-login lockout after repeated attempts, secure cookie settings (`HttpOnly`, `SameSite`, secure policy), and HTTPS so credentials and auth cookies are encrypted in transit. |
| Tampering | Partially Vulnerable | Request/form payloads can be modified before reaching controllers, potentially altering product, order, or account data. Mitigation is enforced through server-side model validation, anti-forgery token checks on state-changing requests, EF Core parameterized SQL generation, and authorization checks before update actions are executed. |
| Repudiation | Partially Vulnerable | Users can deny actions such as login attempts, updates, and admin operations if activity evidence is weak. The system mitigates this by writing `SystemLogs` audit entries with actor ID, action text, owner scope, and UTC timestamp, improving traceability for incident review and dispute handling. |
| Information Disclosure | Vulnerable | Sensitive data can leak through exposed secrets, excessive data access scope, or misconfigured deployment settings. Mitigation includes environment-variable secret storage, role/owner-scope query filtering, DB-scoped SQL login, and encrypted transport (HTTPS/TLS), but risk remains if secret-handling and config hygiene are not consistently enforced across releases. |
| Denial of Service (DoS) | Vulnerable | Shared-hosting resource limits can be exhausted by bursts or abusive traffic, causing slowdowns or outages. Current mitigation is only partial (login lockout and operational monitoring); app-wide inbound request throttling/rate-limiting middleware is not yet enabled, so this remains a higher-priority hardening gap. |
| Elevation of Privilege | Low Vulnerability | Users may attempt to access records or endpoints outside their permitted role/tenant scope via crafted requests. The system mitigates this with role-based `[Authorize]` usage, owner-tenant isolation (`OwnerUserID`) in queries, and controller-level permission checks that block unauthorized privileged operations. |

# Threat Model Traceability

Relate each STRIDE threat to controls documented in the other cloud documents.

| STRIDE Threat | Control Focus in this Document | Related Cloud Document Mapping |
| --- | --- | --- |
| Spoofing | Password hashing, lockout, secure cookies, HTTPS | `cloud-data-engineer.md`: `Creation` and `Usage` security controls; `cloud-operations-engineer.md`: deployment HTTPS and credential handling |
| Tampering | Server-side validation, anti-forgery, authorization | `cloud-data-engineer.md`: `Creation` and `Archiving` control rows; `cloud-operations-engineer.md`: post-deployment create/update smoke tests |
| Repudiation | `SystemLogs` audit trail with actor/time/action | `cloud-data-engineer.md`: `Archiving` row (audit logging); `cloud-operations-engineer.md`: activity-log verification in smoke tests |
| Information Disclosure | Secret management and least-necessary data access | `cloud-operations-engineer.md`: environment variables and deployment security checklist; `cloud-solutions-architect.md`: security comparison columns |
| Denial of Service (DoS) | Availability risk and throttling gap | `cloud-solutions-architect.md`: shared-hosting scalability/capacity constraints; `cloud-operations-engineer.md`: monitoring and operational hardening |
| Elevation of Privilege | Role + tenant-scope authorization | `cloud-data-engineer.md`: `Usage/Sharing` owner-scope controls; `cloud-operations-engineer.md`: restricted deployment/operator access |
