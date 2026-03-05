# Data Lifecycle

Explain what happens to the data in each stage of the KSTech Data Lifecycle.

| Lifecycle Stage | What Happens to the Data | Cloud Service Used | Security Control |
| --- | --- | --- | --- |
| Creation | User submits data through web forms (registration, orders, inquiries, products) | MonsterASP IIS Web App (ASP.NET Core) | Input validation, HTTPS |
| Storage | Data saved to SQL tables (Users, Products, Orders, Payments, Employees, SystemLogs) | MonsterASP SQL Server | Least-privilege SQL user, password hashing (BCrypt) |
| Usage | Backend reads and processes data for dashboard, inventory, CRM, BI, and store modules | MonsterASP Web App + SQL Server | Role-based access control, authenticated session cookies |
| Sharing | Data sent to external services for emails (Brevo), market pricing (eBay API), game metadata (Steam API), and report archival (Cloudinary) | Brevo API, eBay API, Steam API, Cloudinary API | API keys in environment variables, HTTPS |
| Archiving | Generated BI reports uploaded to Cloudinary; employees marked as archived (`IsArchived` flag); users deactivated via `IsActive` flag | Cloudinary + MonsterASP SQL Server | Access-controlled Cloudinary credentials, role-restricted archive actions |
| Deletion | Cart items removed after checkout; sent emails purged by background worker; purchase orders deleted by owner-only action | MonsterASP Web App + SQL Server | Owner/admin-only permissions, audit logging via SystemLogs |

**Table 1: Data Lifecycle Matrix**

The Data Lifecycle Matrix shows how data moves through the system from user input to deletion. Data is first collected through secure web forms, stored in SQL Server, and processed by backend modules (dashboard, inventory, CRM, BI, and store). Selected data is shared externally via APIs for email delivery, market data, and report archival. Security controls such as role-based access, HTTPS, and least-privilege SQL users help protect data at each stage.

For archiving, the system uses soft-delete patterns — employees are flagged as archived and users are deactivated rather than permanently removed. Only transactional data like cart items and sent email records are hard-deleted. BI reports are archived to Cloudinary for long-term cloud storage.

Overall, the system follows basic cloud data governance principles including access control, lifecycle awareness, and structured data flow. Improvements such as a documented retention schedule, automated log cleanup, and separated runtime/migration SQL identities could further strengthen data governance.
