# Data Lifecycle

This section explains, in simple terms, what happens to KSTech data at each stage when the system runs on MonsterASP.NET.

Based on the professor reference image:
- `Creation/Storage` = actions that mainly use `INSERT`.
- `Usage/Sharing` = actions that mainly use `SELECT`.
- `Archiving/Deletion` = actions that mainly use `UPDATE` (soft delete/status changes) and planned future `DELETE`.

| Lifecycle Stage | What Happens to the Data | Cloud Service Used | Security Control |
| --- | --- | --- | --- |
| Creation | Add product - staff submits product form, app checks required fields and values, then inserts a new `Products` row.<br>Register account - app validates user details, then inserts `Users` and `Customers`/`Employees` rows.<br>Add to cart - app inserts or updates `CartItems` for guest or signed-in customer sessions.<br>Checkout order - app checks stock and request data, then inserts `Orders`, `OrderDetails`, and `Payments`.<br>Submit technical inquiry - customer sends inquiry form, app validates input, then inserts `TechnicalInquiries`.<br>Create campaign - staff submits campaign form, app validates input, then inserts `Campaigns`.<br>Queue outbound messages - app inserts `EmailNotifications` and `EmailOutbox` for verification, campaign, and CRM sends.<br>Request password reset - app inserts `PasswordResetTokens` and queues reset email in `EmailOutbox`.<br>Create monthly budget (first month entry) - owner saves budget and app inserts `FinancialBudgets`. | MonsterASP-hosted ASP.NET Core + MonsterASP SQL Server | Security happens at submit time: server-side validation checks required fields/ranges, invalid payloads are rejected before insert, anti-forgery tokens are required on state-changing requests, role checks limit who can create sensitive records, and HTTPS encrypts form/session data in transit. |
| Storage | Save account data - app stores `Users`, `Customers`, and `Employees` in SQL Server.<br>Save business data - app stores `Products`, `CartItems`, `Orders`, `OrderDetails`, `Payments`, `TechnicalInquiries`, `Campaigns`, `PurchaseOrders`, `PurchaseOrderLines`, and `InventoryMovements`.<br>Save planning and audit data - app stores `FinancialBudgets`, `BudgetEvents`, `SystemLogs`, `PasswordResetTokens`, `EmailNotifications`, and `EmailOutbox`.<br>Save report files - generated report files are uploaded to Cloudinary for cloud storage. | MonsterASP SQL Server + Cloudinary | Storage security happens through controlled write paths: the app uses its configured SQL account (not end-user direct DB access), passwords are stored as BCrypt hashes, EF Core uses parameterized queries, and Cloudinary uploads use authenticated API credentials and scoped folders. |
| Usage | Transactions that retrieve or view existing data in the system (`SELECT`).<br>View product table/details - users open product listings and product pages to read item info, price, and stock status.<br>Search and filter records - users/staff run searches and filters to find products, orders, inquiries, and other records.<br>View order/payment history - customers and staff read order number, item list, quantity, total amount, payment status, and timeline.<br>Retrieve reports and dashboards - staff/owner opens summaries, analytics, and logs for monitoring and decisions. | MonsterASP web app + SQL Server | Usage security happens before data is returned: after login, the app issues an auth cookie (`KSTech.AdminAuth` or `KSTech.StoreAuth`) and the browser sends it on each request so the server can validate identity and role. Then `[Authorize]` and role checks gate each page/endpoint, owner-scope filters limit results to allowed workspace data, and `HttpOnly`/secure cookies plus HTTPS reduce session token exposure. |
| Sharing | Superadmin sharing view - can view all business records across modules for full monitoring and governance.<br>Owner sharing view - can view customers, orders, inventory, inquiries, and reports inside their own owner workspace for daily management and support (not other owners' workspaces).<br>Inventory Manager sharing view - can view shared inventory, stock movement, and procurement data needed for inventory operations.<br>Sales Staff sharing view - can view shared sales, customer, and inquiry records needed to handle sales and follow-up tasks.<br>Customer sharing view - can view public product/catalog data for browsing and ordering, plus their account, cart, order, and inquiry data (no access to other customers' private data). API data exchange in this stage: app sends email content to Brevo, receives delivery status webhooks, fetches external reference data from eBay/Steam, and uploads report files to Cloudinary. | MonsterASP website/database + Brevo API + eBay API + Steam API + Cloudinary API | Sharing security happens at access and integration time: role/owner checks run before cross-user data is shown, API keys are loaded from app config/environment, external calls use HTTPS, webhook updates are matched to known message records, and access activity is written to logs for traceability. |
| Archiving | Archive employee - app updates `Employees.IsArchived` and linked `Users.IsActive` values.<br>Archive product - app updates `Products.MarketPriceSource` to `Archived` (and unarchive restores normal visibility).<br>Resolve inquiry - app updates `TechnicalInquiries` fields like `IsResolved` and resolution details.<br>Archive/restore budget - app updates budget status and records changes in events/logs.<br>Archive by workflow state - app updates campaign, procurement, and order statuses, then records audit logs. | MonsterASP SQL Server + Cloudinary | Archiving security happens through controlled update actions: only authorized roles can archive/restore, the app updates status flags instead of unsafe hard delete, every archive change records actor/time/action in `SystemLogs`, and Cloudinary archive uploads require authenticated credentials. |
| Deletion (Future Implementation) | Current cleanup delete - app removes cart items, processed outbox rows, and eligible draft procurement rows.<br>Current limited admin delete - owner/superadmin can remove some draft procurement data.<br>Future retention delete - planned cleanup job will delete old eligible data with approval and logs. | Planned background cleanup job + SQL Server | Delete security happens with strict guardrails: only authorized roles can trigger delete endpoints, anti-forgery checks block forged delete requests, only eligible records can be removed, and planned retention jobs will include backup-before-delete plus audit logging of who deleted what and when. |

**Table 1: Data Lifecycle Matrix**

This matrix shows the full path of data: it is collected from forms, validated, stored in SQL Server, used by system modules, shared only when allowed, then archived by status updates. This is the current lifecycle pattern used in production.

At present, KSTech mostly closes lifecycle records through archiving (`UPDATE`) instead of broad hard deletion. Full retention-based `DELETE` workflows are planned for future versions.

## Transaction Specification

The table below lists the real business transactions in the system and maps each one to the SQL behavior behind it.

| Transaction ID | Business Transaction | Primary SQL Operations | Main Tables/Data Objects | Lifecycle Mapping |
| --- | --- | --- | --- | --- |
| TX-01 | Owner/staff account registration | `INSERT` | `Users`, `Employees`, `EmailOutbox` | Creation, Storage |
| TX-02 | Customer account registration | `INSERT` | `Users`, `Customers`, `EmailOutbox` | Creation, Storage |
| TX-03 | Login and lockout control | `SELECT`, `UPDATE`, `INSERT` | `Users` (failed attempts/lockout), `SystemLogs` | Usage, Archiving/Control |
| TX-04 | Password reset request and reset completion | `SELECT`, `INSERT`, `UPDATE` | `PasswordResetTokens`, `Users`, `EmailOutbox` | Creation, Usage, Archiving/Control |
| TX-05 | Product create/edit/archive operations | `INSERT`, `UPDATE` | `Products`, `InventoryMovements`, `SystemLogs` | Creation, Archiving/Control |
| TX-06 | Cart operations (add/update/remove/clear) | `SELECT`, `INSERT`, `UPDATE`, limited `DELETE` | `CartItems`, `Products` | Usage, Archiving/Deletion (limited) |
| TX-07 | Checkout and order creation | `SELECT`, `INSERT`, `UPDATE`, limited `DELETE` | `Orders`, `OrderDetails`, `Payments`, `Products`, `InventoryMovements`, `Customers`, `CartItems`, `SystemLogs` | Creation, Usage, Archiving/Deletion (limited) |
| TX-08 | Process pending payment | `UPDATE`, `INSERT` or `UPDATE` | `Orders`, `Payments`, `SystemLogs` | Archiving/Control |
| TX-09 | Cancel customer order | `UPDATE`, `INSERT` | `Orders`, `Products` (stock return), `InventoryMovements`, `SystemLogs` | Archiving/Control |
| TX-10 | Submit technical inquiry | `INSERT` | `TechnicalInquiries` | Creation, Storage |
| TX-11 | Reply and resolve inquiry | `SELECT`, `INSERT`, `UPDATE` | `TechnicalInquiries`, `EmailNotifications`, `EmailOutbox`, `SystemLogs` | Usage, Sharing, Archiving |
| TX-12 | Campaign create/execute/cancel | `SELECT`, `INSERT`, `UPDATE` | `Campaigns`, `EmailNotifications`, `EmailOutbox`, `SystemLogs` | Creation, Sharing, Archiving |
| TX-13 | Employee lifecycle actions | `INSERT`, `UPDATE` | `Users`, `Employees`, `SystemLogs` | Creation, Archiving |
| TX-14 | Procurement workflow | `INSERT`, `UPDATE`, limited `DELETE` | `PurchaseOrders`, `PurchaseOrderLines`, `InventoryMovements`, `BudgetEvents` | Creation, Archiving/Deletion (limited) |
| TX-15 | Reporting and cloud archival | `SELECT` + external upload | SQL reporting tables + Cloudinary object storage | Usage, Sharing, Archiving |
| TX-16 | Admin settings profile/security updates | `SELECT`, `UPDATE`, `INSERT` | `Users`, `Employees`, `Customers`, `EmailOutbox` | Usage, Archiving/Control |
| TX-17 | Customer profile/security updates | `SELECT`, `UPDATE`, `INSERT` | `Users`, `Customers`, `EmailOutbox` | Usage, Archiving/Control |
| TX-18 | BI budget planning lifecycle (save/archive/restore) | `SELECT`, `INSERT`, `UPDATE` | `FinancialBudgets`, `BudgetEvents`, `SystemLogs` | Creation, Archiving |
| TX-19 | BI order correction actions (admin cancel/refund) | `SELECT`, `UPDATE`, `INSERT` | `Orders`, `Products`, `InventoryMovements` | Archiving/Control |
| TX-20 | BI and inventory price update/sync actions | `SELECT`, `UPDATE` | `Products` | Usage, Archiving/Control |
| TX-21 | Email delivery lifecycle and webhook status updates | `SELECT`, `UPDATE`, limited `DELETE` | `EmailOutbox`, `EmailNotifications`, `Campaigns` | Sharing, Archiving/Deletion (limited) |
| TX-22 | Owner-scope switch auditing | `INSERT` | `SystemLogs` | Usage, Archiving/Control |

**Table 2: Transaction Matrix**

This transaction matrix gives a direct mapping of features to data operations. It also confirms that most production control is based on `INSERT`, `SELECT`, and `UPDATE`, while `DELETE` is currently limited to specific cleanup cases (cart cleanup, outbox cleanup, and draft procurement cleanup).

## Transaction Flow (Simple English)

Add product - Staff submits the product form, the server validates the values, then a new row is saved in `Products`. Related stock and audit records may also be saved so the action can be tracked later.

Archive product - Staff archives a product by updating `Products.MarketPriceSource` to `Archived`, so the product is hidden from active listings until staff unarchives it.

Customer registration - Customer submits signup form, app validates the data, creates `Users` and `Customers` rows, then queues a verification email in `EmailOutbox`.

Checkout order - App reads cart items and stock, creates `Orders`, `OrderDetails`, and `Payments`, updates inventory with stock-out movements, logs the action in `SystemLogs`, then clears `CartItems`.

Cancel order - App checks if the order can still be canceled, updates order/payment status, returns stock quantities, writes inventory movement records, and saves an audit log.

Submit inquiry - Customer sends contact form, app validates input, then inserts a new `TechnicalInquiries` row with `IsResolved = false`.

Resolve inquiry - Staff marks an inquiry as resolved, and app updates `TechnicalInquiries` fields such as `IsResolved`, resolved date, and notes so CRM history stays complete.

Save budget - Owner/superadmin saves or updates monthly budget, app writes to `FinancialBudgets`, creates budget events in `BudgetEvents`, and logs context in `SystemLogs`.

Webhook/email status update - Brevo webhook updates delivery status in `EmailNotifications`, while the email worker updates campaign/outbox states and removes processed rows from `EmailOutbox`.
