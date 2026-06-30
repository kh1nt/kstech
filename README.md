<div align="center">
  <h1>KSTech</h1>
  <p><i>Computer Parts Inventory and Customer Engagement System

</i></p>

  <p>
    <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 8" />
    <img src="https://img.shields.io/badge/ASP.NET_Core_MVC-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt="ASP.NET Core MVC" />
    <img src="https://img.shields.io/badge/SQL_Server-CC2927?style=for-the-badge&logo=microsoft-sql-server&logoColor=white" alt="SQL Server" />
    <img src="https://img.shields.io/badge/Bootstrap-7952B3?style=for-the-badge&logo=bootstrap&logoColor=white" alt="Bootstrap" />
  </p>
</div>

---

> KSTech is an intelligent, robust, and beautifully crafted Enterprise Resource Planning (ERP) and storefront application tailored specifically for **PC builders, hardware enthusiasts, and tech retail stores**.

With a seamless digital experience, KSTech bridges the gap between complex back-office operations and a modern customer-centric storefront. From tracking high-value GPU inventory to automatically syncing real-time market prices, KSTech is engineered to handle your tech retail business effortlessly.

---

## 🌟 Key Features

### 🛒 E-Commerce & Customer Experience
*   **Immersive Storefront:** A visually stunning and responsive landing page designed to attract and convert tech enthusiasts.
*   **Customer Portal:** A dedicated, secure hub for shoppers to track their orders, build lists, and browse available hardware.
*   **Loyalty & Rewards System:** Built-in reward tracking to keep customers engaged and coming back for their next big PC upgrade.
*   **Seamless Login:** Frictionless onboarding with Google Authentication alongside traditional secure sign-in.

### 📦 Powerhouse Logistics & Inventory
*   **Smart Inventory Control:** Advanced item tracking, low-stock alerts, and streamlined re-stocking workflows to ensure you never run out of critical components.
*   **Real-time Market Sync:** Actively monitors and syncs market pricing utilizing integrations with **eBay** and **Steam**, ensuring your prices stay competitive automatically.
*   **Secure Role Management:** Robust separation of privileges between administrators and customers for complete data integrity.

### 📈 Business Intelligence & Insight
*   **Interactive Dashboards:** Clear, real-time insights into sales, revenue trends, and overall business health.
*   **Professional Reporting:** Automatically generates pixel-perfect, detailed financial and inventory reports using **QuestPDF**.
*   **Cloud Archiving:** Safely and automatically archives your business reports to the cloud using **Cloudinary**.

### ✉️ Marketing Automation
*   **Automated Campaigns:** Engage your customer base with built-in mailing systems powered by **Brevo**.
*   **Reliable Delivery:** Ensures your promotions, receipts, and newsletters are delivered reliably through optimized background email queues.

---

## 🛠️ Technology Stack

*   **Framework:** .NET 8.0 & ASP.NET Core MVC
*   **Database:** Microsoft SQL Server with Entity Framework Core 8
*   **Authentication:** Multi-Scheme Cookie Authentication & OAuth (Google)
*   **Integrations:** 
    *   **QuestPDF:** For dynamic PDF generation
    *   **Cloudinary:** For media and report cloud storage
    *   **Brevo API:** For transactional and marketing emails
    *   **eBay & Steam APIs:** For market pricing analysis

---

## 🚀 Getting Started

Follow these steps to get a local development environment up and running.

### Prerequisites
*   [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
*   Microsoft SQL Server (LocalDB or a dedicated instance)
*   Visual Studio 2022 or Visual Studio Code

### 1. Configuration
Clone the repository and update the `appsettings.json` (or create an `appsettings.Development.json`) with your local environment values:

*   **Database:** Update the `DefaultConnection` string to point to your SQL Server.
*   **API Keys Configuration:** To unlock full functionality, provide your keys for:
    *   Google OAuth (`ClientId` & `ClientSecret`)
    *   Brevo 
    *   eBay Browse API
    *   Steam API
    *   Cloudinary

### 2. Database Migrations
Apply the initial schema using Entity Framework Core tools. Open your terminal in the project directory and run:
```bash
dotnet ef database update
```
*(If using the Package Manager Console in Visual Studio, run: `Update-Database`)*

### 3. Launch
Run the application via visual studio or using the .NET CLI:
```bash
dotnet run
```
On the first successful startup, the system will automatically seed necessary administrative roles, sample product catalogs, and starter data (configurable via `SeedOptions`).

---

<p align="center">
  <i>Built for performance. Designed for enthusiasts.</i>
</p>
