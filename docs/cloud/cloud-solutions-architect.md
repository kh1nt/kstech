# Cloud Computing Model

Design the cloud system and define how all parts of the cloud lifecycle fit together.

Figure 1: Cloud Computing Model

```text
Users
  |
  v
MonsterASP IIS Website (kstech.runasp.net)
ASP.NET Core MVC (.NET 8)
  |
  v
MonsterASP SQL Server
  |
  +--> Brevo API
  +--> eBay API
  +--> Steam API
  +--> Cloudinary API
```

# Operational and Economical Feasibility

This section compares each current service with one alternative using simple terms for performance, security, scalability, and cost.

| Service | Performance | Security | Scalability | Cost |
| --- | --- | --- | --- | --- |
| Razor Views via ASP.NET Core MVC (`Current`) | Fast enough for forms, dashboard pages, and store pages because HTML is rendered on the server, with low browser-side processing | Login, session cookies, and form protection are in one app, so there are fewer security gaps and fewer integration points to harden | Scales together with backend and is enough for current project traffic, especially for admin and transactional workloads | Low cost because it is already built, does not need a separate frontend host, and has lower maintenance overhead |
| React / Next.js (Alternative) | Very good for highly interactive pages and rich UI behavior, especially for dynamic client-side experiences | Needs a separate API security setup (tokens/sessions), which adds more risk if misconfigured and needs stricter API boundary controls | Frontend can scale independently through CDN/edge platforms, which is useful when traffic grows quickly | Medium cost because of extra hosting, separate deployment pipeline, and added development complexity |

Figure 2: Frontend Stack

Why this is chosen: Razor is a better fit for KSTech right now because the system is mostly transaction and admin workflows. Performance is good enough for form-heavy pages, security is simpler because UI and backend are in one app, scalability is acceptable for current traffic, and cost stays low since no separate frontend platform is needed.

| Service | Performance | Security | Scalability | Cost |
| --- | --- | --- | --- | --- |
| MonsterASP (`Current`) | Good response time for the current small-to-medium workload, with acceptable latency for normal user actions | Managed IIS hosting with HTTPS support and baseline hosting protections that reduce manual server hardening tasks | Shared plan has limits, but it is enough for current usage and expected near-term growth | Low monthly cost and easy deployment flow, which fits project budget and team capacity |
| Vercel (Alternative) | Very fast delivery for frontend-heavy workloads using edge/CDN, especially for globally distributed users | Built-in HTTPS and good edge-level protections, with strong managed platform defaults | Strong automatic scaling across regions and better handling of sudden traffic spikes | Medium cost and can increase as traffic/features grow, especially with advanced platform usage |

Figure 3: Client Deployment

Why this is chosen: MonsterASP supports the current ASP.NET setup directly, so deployment is simpler and more practical for the project. It gives adequate performance for current users, built-in HTTPS support for baseline security, enough shared-plan scalability for now, and lower monthly cost than moving early to a larger platform.

| Service | Performance | Security | Scalability | Cost |
| --- | --- | --- | --- | --- |
| MonsterASP Shared IIS (`Current`) | Stable performance for normal transaction traffic, including common CRUD and dashboard operations | Managed environment with HTTPS-capable deployment and simpler operational security setup | Limited by shared-hosting plan limits, but still workable for current scale and user volume | Low and predictable monthly cost, making it easier to control spending each term |
| Azure App Service (Alternative) | Strong managed performance and autoscaling support, better for heavier or unpredictable workloads | More advanced identity and network security controls, including stronger enterprise integration options | Better vertical and horizontal scaling options for growth and higher uptime requirements | Medium to high cost depending on selected tier and scaling configuration |

Figure 4: Server

Why this is chosen: Shared IIS is enough for the current user load and budget. Current performance and security needs are met, scalability limits are acceptable at this stage, and cost is predictable. Azure App Service becomes the better choice when higher traffic, stronger uptime targets, and larger scale are required.

| Service | Performance | Security | Scalability | Cost |
| --- | --- | --- | --- | --- |
| ASP.NET Core MVC (.NET 8) (`Current`) | Fast runtime and efficient request handling for current workload, with strong performance for server-rendered business pages | Built-in support for authentication, authorization, cookies, and anti-forgery checks, reducing custom security code | Works for current scale and can be moved later to larger platforms with minimal architecture change | Low extra cost because the project is already built in .NET and the team is already familiar with it |
| Laravel (Alternative) | Good framework performance with a large ecosystem and many available packages | Strong built-in security features if configured correctly, but requires equivalent policy redesign in this project | Can scale well with proper infrastructure and operational tuning | Medium to high cost because it requires major rewrite, team retraining, and migration testing effort |

Figure 5: Backend Stack

Why this is chosen: ASP.NET Core (.NET 8) is chosen because the system is primarily designed for this stack. It provides reliable performance for transaction-heavy workflows, strong built-in security features, a clear path to scale on larger hosting tiers, and lower overall cost by avoiding platform mismatch and major rework.

| Service | Performance | Security | Scalability | Cost |
| --- | --- | --- | --- | --- |
| Cloudinary (`Current`) | Good upload and retrieval speed for current report/media volume, with simple integration in the current workflow | API key access over HTTPS, with manageable security setup for current team size | Can scale by plan and is enough for current archive size and near-term report growth | Low cost at current usage level, with less setup effort for the current project phase |
| Amazon S3 (Alternative) | Strong performance for large file storage workloads, especially when data volume becomes much higher | Strong encryption and IAM-based access controls suitable for stricter governance needs | Very high scalability for growth workloads and long-term enterprise storage requirements | Low-to-medium usage-based cost depending on data volume, but may require added operational setup |

Figure 6: Object Storage

Why this is chosen: Cloudinary is easier to integrate now and has low current cost. Its performance and security are enough for current report/media usage, and plan-based scaling is sufficient for now. Amazon S3 is the better next step when storage volume, compliance, and long-term scalability become more demanding.

| Service | Performance | Security | Scalability | Cost |
| --- | --- | --- | --- | --- |
| MonsterASP SQL Server (`Current`) | Good enough for current transactional database operations, including product/order/account flows | DB-scoped SQL access with encrypted connection support and simple deployment alignment | Limited by shared-hosting service tiers, but acceptable while concurrency is still moderate | Low-to-medium fixed monthly cost, which helps keep infrastructure spending predictable |
| Azure SQL Database (Alternative) | Better managed performance and reliability features, useful for heavier read/write workloads | Strong auditing and threat-detection features that support stricter security/compliance targets | Better elastic scaling through managed tiers for larger or bursty workloads | Medium to high cost based on service tier and performance requirements |

Figure 7: Database Platform

Why this is chosen: MonsterASP SQL Server fits current workload and budget. It provides enough performance and baseline security for present operations, and its scalability is acceptable while usage is still moderate. Azure SQL is the future option when better scale controls and advanced security/compliance features are required.

| Service | Performance | Security | Scalability | Cost |
| --- | --- | --- | --- | --- |
| Brevo (`Current`) | Reliable email sending speed for current transaction volume, such as order and account notifications | API key security over HTTPS with sender controls, sufficient for current communication scope | Scales well for low-to-moderate campaign usage and normal transactional bursts | Low and budget-friendly, with a good feature-to-cost fit for current needs |
| SendGrid (Alternative) | High throughput with advanced delivery tooling, better for larger campaign and notification volume | Strong domain and authentication controls with more enterprise-grade deliverability options | Supports larger enterprise-scale sending with stronger high-volume tooling | Medium cost, usually volume-based, and can rise faster as send volume increases |

Figure 8: Email Delivery Service

Why this is chosen: Brevo meets current feature needs at lower cost. It provides reliable delivery performance for present email volume, secure API-based integration over HTTPS, and enough scalability for current campaigns. SendGrid is a strong upgrade option when higher volume and more advanced deliverability controls are needed.
