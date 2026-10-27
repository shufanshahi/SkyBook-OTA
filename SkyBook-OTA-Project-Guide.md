# SkyBook OTA — ASP.NET Core MVC Portfolio Project (Step-by-Step Build Guide)

**Target role:** Jr. Engineer – Software Development, Triplover Limited & Firsttrip Limited (US-Bangla Airlines sister concerns)
**Deadline:** October 30, 2026 (today is October 25, so you have about 5–6 days)
**Goal:** Build a small but realistic **Online Travel Agency (OTA) web application** — an **ASP.NET Core MVC** site with a real browser UI where travel agents search flights, book, ticket, cancel and refund, and admins manage agents. Push it to GitHub and put the link on your CV.

> **Progress legend:** ✅ on a heading = already built in this repo. Checked boxes in the day plan (Section 15) show the same.

> **Focus rule: this is a backend project.** The recruiter is hiring for backend work (supplier integration, SQL, async, booking logic, tests). The MVC UI exists **only to visualize and trigger the backend**. Use the default Bootstrap template, plain tables and forms, no custom CSS, no JavaScript beyond what the template ships. Roughly **80% of your time goes to Sections 6–9, 11–12** (domain, database, suppliers, services, tests) and **at most 20% to Section 10** (views). All business logic lives in services, never in controllers or views; controllers just call a service and pick a view.

> **How to read this guide:** You are learning ASP.NET as you build. So every part says *what* to do, *why*, and *how to check it worked*. Do the steps in order. Do not copy code you do not understand — read the "Concept" boxes first, then type the code yourself where you can.

---

## Table of contents

1. [Why this project (JD mapping)](#1-why-this-project-jd-mapping)
2. [What you are building (screens + architecture)](#2-what-you-are-building)
3. [MVC explained for this project](#3-mvc-explained-for-this-project)
4. [Install everything (step by step)](#4-install-everything-step-by-step)
5. [Create the solution](#5-create-the-solution-step-by-step)
6. [Domain layer](#6-domain-layer)
7. [Database: SQL Server, EF Core, stored procedures](#7-database-sql-server-ef-core-stored-procedures)
8. [Mock suppliers (Go NDC + SOAP GDS)](#8-mock-suppliers)
9. [Supplier integration layer](#9-supplier-integration-layer)
10. [Build the MVC web app (UI)](#10-build-the-mvc-web-app-ui)
11. [Cross-cutting concerns](#11-cross-cutting-concerns)
12. [Testing](#12-testing)
13. [Docker Compose](#13-docker-compose)
14. [CI with GitHub Actions](#14-ci-with-github-actions)
15. [Day-by-day plan](#15-day-by-day-plan-oct-25--oct-30)
16. [Project README template](#16-readme-template-for-your-github-repo)
17. [CV entry](#17-using-the-project-in-your-application)
18. [Interview prep](#18-interview-prep)
19. [Final checklist](#19-final-checklist)

---

## 1. Why this project (JD mapping)

Recruiters skim. Make every JD bullet visible in your repo.

| JD requirement | Where it appears in SkyBook |
|---|---|
| C#/.NET and/or Go | Main app in ASP.NET Core 8 MVC; mock NDC airline service in **Go** |
| ASP.NET Core for OTA booking, admin, agent management | MVC controllers + Razor views for Booking, Admin area, Agent management |
| REST **and SOAP** APIs, async | REST NDC supplier (JSON) + SOAP GDS supplier (XML), called in parallel with `async/await` and `Task.WhenAll` |
| MSSQL queries, stored procedures, indexing | SQL Server schema, 4 stored procedures, indexes with before/after note |
| EF Core and/or Dapper | EF Core for CRUD/migrations, Dapper for search-heavy reads and stored procedures |
| GDS / airline API / aggregator / NDC integration | `IFlightSupplier` abstraction + aggregator that merges and normalizes results |
| Search → pricing → booking → ticketing → cancellation → refund | Full booking lifecycle with a state machine, each step a screen |
| Test cases, test REST APIs and async workflows | xUnit unit tests, integration tests with `WebApplicationFactory`, written test-case table, Postman collection for the mock suppliers |
| Troubleshooting third-party integrations | Timeouts, retries (Polly), structured logging with correlation IDs, supplier-log screen for admins |
| Git, dev practices | Clean commits, GitHub Actions CI, Docker Compose |
| Microservices / distributed systems (advantage) | 3 services (web app + 2 mock suppliers) via Docker Compose |

---

## 2. What you are building

**SkyBook** is a B2B OTA website. A travel **Agent** logs in, searches flights across two suppliers, reviews the price, enters passenger details, books (creates a PNR), issues the ticket (money is deducted from the agent wallet), and can cancel/refund. An **Admin** manages agents, wallets, markup, and troubleshoots supplier calls.

### 2.1 Screens (minimal UI checklist — plain pages, no polish)

Each screen is just a form or table that exercises one backend capability. Build them in the order the backend becomes ready; if time runs short, Admin screens 11–13 can be reduced to a bare table or skipped.

| # | Screen | URL | Who |
|---|---|---|---|
| 1 | Login | `/Account/Login` | everyone |
| 2 | Agent dashboard (wallet balance, recent bookings) | `/` | Agent |
| 3 | Flight search form | `/Flights/Search` | Agent |
| 4 | Search results (offers from both suppliers, sorted by price, warning banner if a supplier failed) | `/Flights/Results` | Agent |
| 5 | Review & passenger details (re-prices the offer; shows "price changed" alert) | `/Bookings/Create?offerId=...` | Agent |
| 6 | Booking confirmation (PNR, hold deadline) | `/Bookings/Details/{pnr}` | Agent |
| 7 | My bookings (filter + paging) | `/Bookings` | Agent |
| 8 | Booking details with **Issue Ticket / Cancel / Request Refund** buttons | `/Bookings/Details/{pnr}` | Agent |
| 9 | Admin: agents list, create agent, top-up wallet, set markup, suspend | `/Admin/Agents` | Admin |
| 10 | Admin: all-bookings search | `/Admin/Bookings` | Admin |
| 11 | Admin: sales report | `/Admin/Reports/Sales` | Admin |
| 12 | Admin: supplier logs (search by correlation ID) | `/Admin/SupplierLogs` | Admin |
| 13 | Admin: pending refunds → approve | `/Admin/Refunds` | Admin |

A tiny wireframe of the results screen, so you know what you are aiming at:

```
┌──────────────────────────────────────────────────────────────┐
│ SkyBook   Search  My Bookings            agent1  Wallet 50,000│
├──────────────────────────────────────────────────────────────┤
│ ⚠ SkyGDS did not respond. Showing results from AeroNDC only.  │
│ DAC → CGP · 14 Nov 2026 · 1 Adult · Economy                   │
│ ┌────────────────────────────────────────────────────────┐   │
│ │ SB 141  08:00 → 08:55   Refundable   Bag 20kg          │   │
│ │ Supplier: SkyGDS            BDT 5,200      [ Select ]  │   │
│ ├────────────────────────────────────────────────────────┤   │
│ │ AN 305  10:30 → 11:25   Non-refundable                 │   │
│ │ Supplier: AeroNDC           BDT 5,450      [ Select ]  │   │
│ └────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────┘
```

### 2.2 Services

```
                 ┌───────────────────────────────┐
  Browser  ────► │       SkyBook.Web             │        ┌──────────────┐
  (Razor UI)     │ ASP.NET Core 8 MVC            │ ─────► │  SQL Server  │
                 │ Cookie auth, EF Core, Dapper  │        └──────────────┘
                 └──────┬───────────┬────────────┘
                        │ SOAP/XML  │ REST/JSON
                        ▼           ▼
           ┌─────────────────┐  ┌──────────────────┐
           │ MockGds (SOAP)  │  │ MockNdc (Go)     │
           │ ASP.NET minimal │  │ net/http         │
           │ "SkyGDS"        │  │ "AeroNDC"        │
           └─────────────────┘  └──────────────────┘
```

- **SkyBook.Web** — the real product: controllers, views, business logic wiring.
- **MockGds** — pretends to be a GDS (like Sabre/Amadeus/Galileo). Speaks **SOAP 1.1 over HTTP with XML**.
- **MockNdc** (Go) — pretends to be an airline NDC API. Speaks **REST/JSON**. Uses Bangladesh routes (DAC, CGP, CXB, ZYL, JSR, SPD, BZL) plus a few international (CCU, KUL, BKK, DXB, SIN).

> Do **not** use real airline names or logos in mock data. Use fictional carriers like `SB` "SkyBangla Air" and `AN` "AeroNova".

### 2.3 Tech stack

| Area | Choice |
|---|---|
| Runtime | .NET 8 SDK (LTS). A newer SDK also works, but keep `net8.0` as the target so the guide matches |
| Web | **ASP.NET Core MVC** (controllers + Razor views), Bootstrap 5 (comes with the template), Areas for Admin |
| DB | SQL Server 2022 in Docker |
| ORM | EF Core 8 (migrations, writes) + Dapper (reads, stored procedures) |
| Auth | **Cookie authentication** (the normal choice for server-rendered MVC), roles `Admin` / `Agent`, `PasswordHasher` |
| Validation | DataAnnotations on view models + a plain C# rules class for business rules |
| Resilience | `Microsoft.Extensions.Http.Resilience` (Polly v8) |
| Logging | Serilog + correlation ID middleware |
| Tests | xUnit, FluentAssertions, NSubstitute, `Microsoft.AspNetCore.Mvc.Testing` |
| Go service | Go 1.22+, standard library only |
| DevOps | Docker Compose, GitHub Actions |

---

## 3. MVC explained for this project

Read this once; you will use it every day this week.

**MVC = Model–View–Controller.** A browser request goes to a **Controller**, which gets/saves data (the **Model**) and chooses a **View** (an HTML template) to render.

```
Browser ── GET /Flights/Results?... ──► Router ──► FlightsController.Results()
                                                       │ calls FlightSearchService
                                                       ▼
                                              builds a ViewModel
                                                       │
Browser ◄──────── HTML ◄──── Razor view Results.cshtml ◄┘
```

| Piece | In SkyBook | Folder |
|---|---|---|
| **Model** (domain) | `Booking`, `Agent`, `BookingStatus` — business data and rules | `SkyBook.Domain` |
| **ViewModel** | A class shaped exactly for one screen, e.g. `FlightSearchViewModel`. Views never receive raw database entities | `SkyBook.Web/Models` |
| **View** | Razor `.cshtml` files: HTML plus `@` C# expressions | `SkyBook.Web/Views/<Controller>/` |
| **Controller** | Receives the request, validates input, calls services, returns a view or redirects | `SkyBook.Web/Controllers` |

Concepts you will meet (each is introduced where first used):

- **Routing** — `/Bookings/Details/AB12CD` → `BookingsController.Details("AB12CD")` by convention `{controller}/{action}/{id?}`.
- **Action results** — `View()`, `RedirectToAction()`, `NotFound()`, `Forbid()`.
- **Model binding** — form fields are automatically copied into your ViewModel parameter.
- **Model validation** — `[Required]` etc. + `ModelState.IsValid`; errors show next to fields.
- **Tag helpers** — `asp-controller`, `asp-action`, `asp-for` make links and form fields without hand-writing URLs.
- **Layouts & partials** — `_Layout.cshtml` is the shared page frame; partial views are reusable fragments.
- **Anti-forgery tokens** — every POST form needs one (`[ValidateAntiForgeryToken]`); tag helpers add it automatically.
- **Post/Redirect/Get (PRG)** — after a successful POST, *redirect* to a GET page so refreshing doesn't resubmit the form (important when the form books a flight!).
- **TempData** — one-time messages that survive a redirect (e.g. "Ticket issued").
- **Dependency injection** — controllers receive services via their constructor; you register them in `Program.cs`.
- **Middleware** — the request pipeline in `Program.cs` (auth, routing, error handling, correlation ID).
- **Areas** — a mini-MVC inside the app; we use one called `Admin`.

---

## 4. Install everything (step by step)

The commands below are for **Ubuntu/Debian Linux** (what you are on). Windows/macOS alternatives are given in a line each. After each install there is a **verify** command — do not skip it.

### 4.1 Git

```bash
sudo apt update
sudo apt install -y git
git --version
git config --global user.name  "Your Name"
git config --global user.email "you@example.com"
```

### 4.2 .NET 8 SDK

The SDK includes the compiler, the `dotnet` CLI, and ASP.NET Core.

```bash
sudo apt install -y dotnet-sdk-8.0
dotnet --version        # expect 8.0.x
dotnet --list-sdks
```

- Windows/macOS: download the **.NET 8 SDK** installer from https://dotnet.microsoft.com/download and run it.
- If your distro does not package 8.0, follow Microsoft's "Install .NET on Linux" page for your version.
- If you have a newer SDK (9/10) installed, that's fine; the projects will still target `net8.0` only if you install the 8 runtime too. Simplest: install 8.

Install the EF Core command-line tool (used for migrations):

```bash
dotnet tool install --global dotnet-ef --version 8.*
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc && source ~/.bashrc
dotnet ef --version
```

### 4.3 Docker (runs SQL Server and, later, all services)

```bash
sudo apt install -y docker.io docker-compose-v2
sudo usermod -aG docker $USER      # then LOG OUT and back in
docker --version
docker compose version
docker run --rm hello-world
```

- Windows/macOS: install **Docker Desktop**.
- SQL Server in Docker needs **at least ~2 GB RAM** free.

### 4.4 Go (for the mock airline service)

The distro `golang-go` package is often too old (we need **1.22+** for the routing syntax). Install from go.dev:

```bash
# check https://go.dev/dl/ for the current version number, e.g. 1.23.x
wget https://go.dev/dl/go1.23.4.linux-amd64.tar.gz
sudo rm -rf /usr/local/go && sudo tar -C /usr/local -xzf go1.23.4.linux-amd64.tar.gz
echo 'export PATH="$PATH:/usr/local/go/bin"' >> ~/.bashrc && source ~/.bashrc
go version
```

### 4.5 Editor

**VS Code** (https://code.visualstudio.com) with extensions:
- **C# Dev Kit** (includes C#) — IntelliSense, debugging, test explorer
- **SQL Server (mssql)** — connect to the database and run queries
- **Go** (by Go Team at Google)
- **Docker**
- **REST Client** or use Postman (below)

Alternatives: Visual Studio 2022 (Windows) or JetBrains Rider.

> Azure Data Studio has been retired by Microsoft; use the VS Code **mssql** extension (or SSMS on Windows, DBeaver anywhere).

### 4.6 Postman (or curl)

Used to poke the **mock suppliers** directly. https://www.postman.com/downloads/ — or just use `curl`, which you already have.

### 4.7 GitHub

Create a GitHub account, then set up an SSH key or use `gh auth login` (GitHub CLI: `sudo apt install gh`).

### 4.8 Final check

```bash
dotnet --version && dotnet ef --version && go version && docker --version && git --version
```
All five must print a version. If one fails, fix it now.

---

## 5. Create the solution (step by step)

### 5.1 Repository structure (target) ✅

```
skybook-ota/
├── README.md
├── docker-compose.yml
├── .github/workflows/ci.yml
├── docs/
│   ├── architecture.png
│   ├── booking-state-machine.md
│   ├── test-cases.md
│   ├── db-performance-notes.md
│   └── screenshots/
├── db/
│   ├── seed.sql
│   └── procedures/
│       ├── usp_SearchBookings.sql
│       ├── usp_IssueTicket.sql
│       ├── usp_CancelBooking.sql
│       └── usp_AgentSalesReport.sql
├── postman/SkyBook.MockSuppliers.postman_collection.json
├── src/
│   ├── SkyBook.Web/                  # MVC: Controllers, Views, ViewModels, Areas/Admin, wwwroot
│   ├── SkyBook.Application/          # services, DTOs, interfaces
│   ├── SkyBook.Domain/               # entities, enums, state machine rules
│   ├── SkyBook.Infrastructure/       # EF DbContext, Dapper repos, supplier clients
│   └── SkyBook.MockGds/              # SOAP mock supplier
├── mock-ndc-go/
│   ├── go.mod
│   ├── main.go
│   └── flights.go
└── tests/
    ├── SkyBook.UnitTests/
    └── SkyBook.IntegrationTests/
```

**Why several projects?** Layering. `Domain` knows nothing about the web or database. `Application` defines *what* the app does (interfaces). `Infrastructure` implements *how* (EF, HTTP clients). `Web` is only the UI. Dependencies point inward: `Web → Infrastructure → Application → Domain`. This is a favourite interview topic.

### 5.2 Create projects ✅

```bash
mkdir skybook-ota && cd skybook-ota
git init -b main
dotnet new gitignore
dotnet new sln -n SkyBook

dotnet new mvc      -n SkyBook.Web            -o src/SkyBook.Web
dotnet new classlib -n SkyBook.Application    -o src/SkyBook.Application
dotnet new classlib -n SkyBook.Domain         -o src/SkyBook.Domain
dotnet new classlib -n SkyBook.Infrastructure -o src/SkyBook.Infrastructure
dotnet new web      -n SkyBook.MockGds        -o src/SkyBook.MockGds
dotnet new xunit    -n SkyBook.UnitTests        -o tests/SkyBook.UnitTests
dotnet new xunit    -n SkyBook.IntegrationTests -o tests/SkyBook.IntegrationTests

dotnet sln add src/*/*.csproj tests/*/*.csproj
```

(`dotnet new sln` on SDK 10 creates `SkyBook.slnx` instead of `.sln`; everything below works the same.)

Delete the `Class1.cs` files the class-library template creates:

```bash
rm src/SkyBook.Application/Class1.cs src/SkyBook.Domain/Class1.cs src/SkyBook.Infrastructure/Class1.cs
```

### 5.3 Wire project references ✅

```bash
dotnet add src/SkyBook.Application    reference src/SkyBook.Domain
dotnet add src/SkyBook.Infrastructure reference src/SkyBook.Application src/SkyBook.Domain
dotnet add src/SkyBook.Web            reference src/SkyBook.Application src/SkyBook.Infrastructure
dotnet add tests/SkyBook.UnitTests        reference src/SkyBook.Application src/SkyBook.Domain src/SkyBook.Infrastructure
dotnet add tests/SkyBook.IntegrationTests reference src/SkyBook.Web
```

### 5.4 Add NuGet packages ✅

```bash
dotnet add src/SkyBook.Infrastructure package Microsoft.EntityFrameworkCore.SqlServer --version 8.*
dotnet add src/SkyBook.Infrastructure package Microsoft.EntityFrameworkCore.Design    --version 8.*
dotnet add src/SkyBook.Infrastructure package Dapper
dotnet add src/SkyBook.Infrastructure package Microsoft.Data.SqlClient
dotnet add src/SkyBook.Infrastructure package Microsoft.Extensions.Http.Resilience --version 8.*

dotnet add src/SkyBook.Web package Microsoft.EntityFrameworkCore.Design --version 8.*
dotnet add src/SkyBook.Web package Serilog.AspNetCore

dotnet add tests/SkyBook.UnitTests package FluentAssertions
dotnet add tests/SkyBook.UnitTests package NSubstitute
dotnet add tests/SkyBook.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing --version 8.*
dotnet add tests/SkyBook.IntegrationTests package FluentAssertions
```

### 5.5 First run — see the template work (partly done: it builds and serves; the About-page exercise is still yours)

```bash
dotnet build
dotnet run --project src/SkyBook.Web
```

Open the URL printed in the terminal (e.g. `http://localhost:5xxx`). You should see the default "Welcome" page. Stop with `Ctrl+C`.

**Look around the template** (this is your ASP.NET orientation):

| File/folder | What it is |
|---|---|
| `Program.cs` | App entry. Registers services (DI) and builds the middleware pipeline |
| `Controllers/HomeController.cs` | A controller with `Index`, `Privacy`, `Error` actions |
| `Views/Home/Index.cshtml` | The view for `Home/Index` |
| `Views/Shared/_Layout.cshtml` | Page frame (navbar, footer, `@RenderBody()`) |
| `Views/_ViewImports.cshtml` | `@using` + `@addTagHelper` shared by all views |
| `Views/_ViewStart.cshtml` | Sets the default layout |
| `Models/ErrorViewModel.cs` | A ViewModel example |
| `wwwroot/` | Static files: css, js, Bootstrap, jQuery |
| `appsettings.json` | Configuration |
| `Properties/launchSettings.json` | Ports used by `dotnet run` |

**Exercise (15 min):** change the text in `Views/Home/Index.cshtml`, refresh the browser, and see it change (use `dotnet watch --project src/SkyBook.Web` for hot reload). Add a new action `public IActionResult About() => View();` and a `Views/Home/About.cshtml`; visit `/Home/About`. Congratulations — that is the whole MVC loop.

### 5.6 Commit (committed locally; pushing to GitHub is still to do)

```bash
git add . && git commit -m "chore: scaffold solution (MVC web, layers, tests)"
```

Create a GitHub repo (empty, public) and push:

```bash
git remote add origin git@github.com:<you>/skybook-ota.git
git push -u origin main
```

---

## 6. Domain layer ✅

Plain C# classes. No EF, no ASP.NET references. Work in `src/SkyBook.Domain`.

### 6.1 Entities ✅

| Entity | Key fields |
|---|---|
| `User` | Id, Email, PasswordHash, Role (`Admin`/`Agent`), AgentId (nullable), IsActive |
| `Agent` | Id, AgencyName, Code, WalletBalance (decimal), CreditLimit, MarkupPercent, Status |
| `WalletTransaction` | Id, AgentId, Amount, Type (`TopUp`, `TicketDebit`, `RefundCredit`), Reference, CreatedAt |
| `Booking` | Id, Pnr, AgentId, Supplier (`SkyGDS`/`AeroNDC`), SupplierRef, Status, TotalFare, Currency, OfferId, HoldExpiresAt, IdempotencyKey, CreatedAt |
| `BookingSegment` | Id, BookingId, Carrier, FlightNo, Origin, Destination, DepartureUtc, ArrivalUtc, CabinClass |
| `Passenger` | Id, BookingId, Type (`ADT`/`CHD`/`INF`), Title, FirstName, LastName, DateOfBirth, PassportNo (nullable) |
| `Ticket` | Id, BookingId, PassengerId, TicketNumber, IssuedAt |
| `Refund` | Id, BookingId, RefundAmount, Penalty, Status (`Pending`/`Approved`), ProcessedAt |
| `SupplierLog` | Id, CorrelationId, Supplier, Operation, RequestBody, ResponseBody, HttpStatus, DurationMs, CreatedAt |

Write one class per file. Example:

```csharp
namespace SkyBook.Domain.Entities;

public class Agent
{
    public int Id { get; set; }
    public string AgencyName { get; set; } = "";
    public string Code { get; set; } = "";
    public decimal WalletBalance { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal MarkupPercent { get; set; }
    public string Status { get; set; } = "Active";   // Active | Suspended
    public byte[] RowVersion { get; set; } = [];       // optimistic concurrency token
}
```

Add `public class DomainException(string message) : Exception(message);` in `SkyBook.Domain`.

### 6.2 Booking state machine ✅

```
   Search ─► Price ─► [Book] ─► HELD ──[Issue]──► TICKETED ──[Cancel]──► CANCELLED ──[Refund]──► REFUNDED
                                  │                                       ▲
                                  ├──[Cancel]─────────────────────────────┘ (no money taken, no refund)
                                  └──[hold expires]──► EXPIRED
```

```csharp
public enum BookingStatus { Held, Ticketed, Cancelled, Refunded, Expired, Failed }

public static class BookingTransitions
{
    private static readonly Dictionary<BookingStatus, BookingStatus[]> Allowed = new()
    {
        [BookingStatus.Held]      = [BookingStatus.Ticketed, BookingStatus.Cancelled, BookingStatus.Expired],
        [BookingStatus.Ticketed]  = [BookingStatus.Cancelled],
        [BookingStatus.Cancelled] = [BookingStatus.Refunded],
    };

    public static bool CanMove(BookingStatus from, BookingStatus to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);
}
```

In `Booking`:

```csharp
public void MoveTo(BookingStatus next)
{
    if (!BookingTransitions.CanMove(Status, next))
        throw new DomainException($"Cannot move booking {Pnr} from {Status} to {next}.");
    Status = next;
}
```

**Why here?** Business rules in the domain are unit-testable without a database or browser. Your first unit test (do it now, 10 minutes): in `tests/SkyBook.UnitTests`, one `[Theory]` listing every valid and invalid pair.

```bash
dotnet test
git add . && git commit -m "feat(domain): entities and booking state machine with tests"
```

> **View buttons follow the state machine.** Later, the booking details view will only show the *Issue Ticket* button if `BookingTransitions.CanMove(status, Ticketed)`. One source of truth.

---

## 7. Database: SQL Server, EF Core, stored procedures ✅

### 7.1 Start SQL Server in Docker ✅

```bash
docker run -d --name skybook-sql \
  -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=Your_strong_Pass123" \
  -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest

docker logs skybook-sql | tail -5     # wait for "SQL Server is now ready for client connections"
```

Connect from VS Code (mssql extension → *Add Connection*): server `localhost,1433`, user `sa`, password above, tick **Trust server certificate**. Run `SELECT @@VERSION;` to verify.

### 7.2 Connection string and user-secrets ✅

Don't commit passwords. Use **user-secrets** locally:

```bash
cd src/SkyBook.Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=SkyBook;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True"
cd ../..
```

### 7.3 DbContext ✅

In `SkyBook.Infrastructure/Persistence/SkyBookDbContext.cs`:

```csharp
public class SkyBookDbContext(DbContextOptions<SkyBookDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingSegment> BookingSegments => Set<BookingSegment>();
    public DbSet<Passenger> Passengers => Set<Passenger>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<SupplierLog> SupplierLogs => Set<SupplierLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Agent>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.WalletBalance).HasPrecision(18, 2);
            e.Property(x => x.CreditLimit).HasPrecision(18, 2);
            e.Property(x => x.MarkupPercent).HasPrecision(5, 2);
        });
        b.Entity<Booking>(e =>
        {
            e.HasIndex(x => x.Pnr).IsUnique();
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TotalFare).HasPrecision(18, 2);
            e.HasIndex(x => new { x.AgentId, x.CreatedAt })
             .IsDescending(false, true)
             .IncludeProperties(x => new { x.Pnr, x.Status, x.TotalFare })
             .HasDatabaseName("IX_Bookings_Agent_Created");
            e.HasIndex(x => new { x.Status, x.HoldExpiresAt })
             .HasFilter("[Status] = 'Held'")
             .HasDatabaseName("IX_Bookings_Status_HoldExpires");
        });
        b.Entity<SupplierLog>().HasIndex(x => x.CorrelationId);
        b.Entity<WalletTransaction>().Property(x => x.Amount).HasPrecision(18, 2);
    }
}
```

The filtered index `IX_Bookings_Status_HoldExpires` is what the hold-expiry background job uses — a good interview talking point.

### 7.4 Register and migrate ✅

In `SkyBook.Web/Program.cs` (before `builder.Build()`):

```csharp
builder.Services.AddDbContext<SkyBookDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
```

Create the first migration and the database:

```bash
dotnet ef migrations add InitialCreate \
  --project src/SkyBook.Infrastructure --startup-project src/SkyBook.Web
dotnet ef database update \
  --project src/SkyBook.Infrastructure --startup-project src/SkyBook.Web
```

**Verify:** refresh the database in VS Code; the `SkyBook` database has your tables plus `__EFMigrationsHistory`.

> *Concept:* a **migration** is a C# file describing a schema change. `--project` is where migrations live; `--startup-project` is the app that supplies the connection string.

### 7.5 Stored procedures ✅

Keep them as `.sql` files in `/db/procedures` and apply them from a migration so a fresh database gets them automatically:

```bash
dotnet ef migrations add AddStoredProcedures --project src/SkyBook.Infrastructure --startup-project src/SkyBook.Web
```

Edit the generated (empty) `Up` method:

```csharp
foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "procedures"), "*.sql"))
    migrationBuilder.Sql(File.ReadAllText(file));
```

and make the `.sql` files copy to the output folder (in `SkyBook.Infrastructure.csproj`):

```xml
<ItemGroup>
  <Content Include="..\..\db\procedures\*.sql" Link="procedures\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Write all four. Each file starts with `CREATE OR ALTER PROCEDURE`, so it can be re-run safely.

**`usp_IssueTicket`** — the most important one: atomic wallet debit + status change, safe under concurrency.

```sql
CREATE OR ALTER PROCEDURE dbo.usp_IssueTicket
    @BookingId BIGINT,
    @AgentId   INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;

    DECLARE @Fare DECIMAL(18,2), @Status VARCHAR(20), @Pnr CHAR(6);

    SELECT @Fare = TotalFare, @Status = Status, @Pnr = Pnr
    FROM dbo.Bookings WITH (UPDLOCK, ROWLOCK)
    WHERE Id = @BookingId AND AgentId = @AgentId;

    IF @Status IS NULL      THROW 50001, 'Booking not found.', 1;
    IF @Status <> 'Held'    THROW 50002, 'Booking is not in Held status.', 1;

    DECLARE @Available DECIMAL(18,2);
    SELECT @Available = WalletBalance + CreditLimit
    FROM dbo.Agents WITH (UPDLOCK, ROWLOCK)
    WHERE Id = @AgentId;

    IF @Available < @Fare   THROW 50003, 'Insufficient wallet balance.', 1;

    UPDATE dbo.Agents SET WalletBalance = WalletBalance - @Fare WHERE Id = @AgentId;

    INSERT dbo.WalletTransactions (AgentId, Amount, Type, Reference, CreatedAt)
    VALUES (@AgentId, -@Fare, 'TicketDebit', @Pnr, SYSUTCDATETIME());

    UPDATE dbo.Bookings SET Status = 'Ticketed' WHERE Id = @BookingId;

    COMMIT;
END
```

The supplier ticketing call happens in C# **before** this SP. If the supplier succeeds but the SP fails, log it and flag the booking for manual review. Write that trade-off in your README — it shows real-world thinking.

**`usp_CancelBooking`** — validates status, sets `Cancelled`, inserts a `Refund` row (`Pending`) with a penalty (e.g. 10% or BDT 500) if it was ticketed.

**`usp_SearchBookings`** — admin search with optional filters and paging:

```sql
CREATE OR ALTER PROCEDURE dbo.usp_SearchBookings
    @AgentId INT = NULL, @Status VARCHAR(20) = NULL, @Pnr CHAR(6) = NULL,
    @FromDate DATETIME2 = NULL, @ToDate DATETIME2 = NULL,
    @Page INT = 1, @PageSize INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    SELECT b.Id, b.Pnr, a.AgencyName, b.Supplier, b.Status, b.TotalFare, b.CreatedAt,
           COUNT(*) OVER() AS TotalCount
    FROM dbo.Bookings b
    JOIN dbo.Agents a ON a.Id = b.AgentId
    WHERE (@AgentId IS NULL OR b.AgentId = @AgentId)
      AND (@Status  IS NULL OR b.Status  = @Status)
      AND (@Pnr     IS NULL OR b.Pnr     = @Pnr)
      AND (@FromDate IS NULL OR b.CreatedAt >= @FromDate)
      AND (@ToDate   IS NULL OR b.CreatedAt <  @ToDate)
    ORDER BY b.CreatedAt DESC
    OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY
    OPTION (RECOMPILE);
END
```

`OPTION (RECOMPILE)` avoids parameter-sniffing problems with catch-all queries — mention in `db-performance-notes.md`.

**`usp_AgentSalesReport`** — `GROUP BY` agent and month: ticket count, gross sales, refunds, net.

Apply with `dotnet ef database update ...` and test each SP in the mssql extension.

### 7.6 Calling an SP with Dapper ✅

```csharp
public class BookingQueries(IConfiguration config)
{
    public async Task<IReadOnlyList<BookingRow>> SearchAsync(BookingSearchFilter f, CancellationToken ct)
    {
        await using var conn = new SqlConnection(config.GetConnectionString("Default"));
        var rows = await conn.QueryAsync<BookingRow>(new CommandDefinition(
            "dbo.usp_SearchBookings",
            new { f.AgentId, f.Status, f.Pnr, f.FromDate, f.ToDate, f.Page, f.PageSize },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
        return rows.AsList();
    }
}
```

### 7.7 Seed data ✅

`db/seed.sql` (or a C# `DbSeeder` run at startup in Development): one admin, two agents (with wallet balance), two sample bookings. Hash passwords with `PasswordHasher<User>` in C# — don't put plaintext in SQL. Demo logins: `admin@skybook.test / Admin@123`, `agent1@skybook.test / Agent@123`.

### 7.8 Performance experiment (`docs/db-performance-notes.md`) ✅

1. Seed ~100k bookings with a script (a `WHILE` loop or a recursive CTE).
2. Run the agent-history query **without** `IX_Bookings_Agent_Created`; capture `SET STATISTICS IO, TIME ON` output and the execution plan (table/clustered index scan).
3. Add the index; run again (index seek). Record logical reads before/after.
4. Write 5–6 lines on what you learned. This directly answers "assist with indexing and database performance optimization."

```bash
git add . && git commit -m "feat(db): EF Core model, migrations, stored procedures"
```

---

## 8. Mock suppliers ✅

You need fake airlines to call. Build these before the real integration, and test them with curl/Postman.

### 8.1 MockNdc in Go (REST/JSON) ✅

```bash
mkdir mock-ndc-go && cd mock-ndc-go
go mod init mockndc
```

`main.go` (standard library only):

```go
package main

import (
    "encoding/json"
    "log"
    "net/http"
    "sync"
    "time"
)

type SearchReq struct {
    Origin      string `json:"origin"`
    Destination string `json:"destination"`
    Date        string `json:"date"`
    Cabin       string `json:"cabin"`
}

type Offer struct {
    OfferID   string    `json:"offerId"`
    Carrier   string    `json:"carrier"`
    FlightNo  string    `json:"flightNo"`
    Departure time.Time `json:"departure"`
    Arrival   time.Time `json:"arrival"`
    BaseFare  float64   `json:"baseFare"`
    Taxes     float64   `json:"taxes"`
    Currency  string    `json:"currency"`
    Seats     int       `json:"seats"`
    ExpiresAt time.Time `json:"expiresAt"`
}

var (
    mu     sync.RWMutex
    orders = map[string]string{} // orderId -> status
)

func search(w http.ResponseWriter, r *http.Request) {
    var req SearchReq
    if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
        http.Error(w, `{"error":"invalid body"}`, http.StatusBadRequest)
        return
    }
    offers := generateOffers(req) // in flights.go: deterministic fake data per route/date
    w.Header().Set("Content-Type", "application/json")
    json.NewEncoder(w).Encode(map[string]any{"offers": offers})
}

func main() {
    mux := http.NewServeMux()
    mux.HandleFunc("POST /v1/offers/search", search)
    mux.HandleFunc("POST /v1/offers/{id}/price", price)
    mux.HandleFunc("POST /v1/orders", createOrder)
    mux.HandleFunc("POST /v1/orders/{id}/ticket", ticketOrder)
    mux.HandleFunc("POST /v1/orders/{id}/cancel", cancelOrder)
    mux.HandleFunc("GET /health", func(w http.ResponseWriter, _ *http.Request) { w.Write([]byte("ok")) })

    log.Println("AeroNDC mock listening on :8081")
    log.Fatal(http.ListenAndServe(":8081", mux))
}
```

You must write `generateOffers`, `price`, `createOrder`, `ticketOrder`, `cancelOrder` yourself (in `flights.go`). Guidance: derive fare from a hash of origin+destination+date so results are deterministic; return 3–5 offers; guard the `orders` map with `mu.Lock()/RLock()`.

Return an NDC-style error body like `{"errors":[{"code":"OFFER_EXPIRED","message":"..."}]}` with HTTP 409 when an offer has expired, so the C# side maps it to a clean message.

**Run and test:**

```bash
go run .
curl -s -X POST localhost:8081/v1/offers/search \
  -H 'Content-Type: application/json' \
  -d '{"origin":"DAC","destination":"CGP","date":"2026-11-14","cabin":"Economy"}'
```

### 8.2 MockGds (SOAP, C# minimal API) ✅

`src/SkyBook.MockGds/Program.cs` handles `POST /soap/air`, reads the `SOAPAction` header, and dispatches to `AirShopping`, `AirPrice`, `AirBook`, `AirTicket`, `AirCancel`. It returns SOAP XML envelopes. Add behaviors you can trigger on purpose:

| Trigger | Behavior |
|---|---|
| Origin = `ERR` | Returns a SOAP `<soap:Fault>` |
| Destination = `SLW` | Sleeps 8 seconds (tests timeout) |
| Price call on an offer 50% of the time | Returns a slightly **higher** price (tests price-change handling) |
| `AirTicket` twice for same ref | Returns fault "Already ticketed" |

Test it with curl by sending a hand-written envelope:

```bash
curl -s -X POST localhost:8080/soap/air \
  -H 'Content-Type: text/xml' -H 'SOAPAction: "http://skygds.mock/air/v1/AirShopping"' \
  -d '<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:gds="http://skygds.mock/air/v1"><soap:Body><gds:AirShoppingRQ><gds:Origin>DAC</gds:Origin><gds:Destination>CGP</gds:Destination><gds:DepartureDate>2026-11-14</gds:DepartureDate><gds:Adults>1</gds:Adults><gds:Cabin>Economy</gds:Cabin></gds:AirShoppingRQ></soap:Body></soap:Envelope>'
```

> *Concept — SOAP vs REST:* SOAP always POSTs an XML **envelope** to one URL; the operation is named in `SOAPAction`/the body, and errors are `<Fault>` elements (often with HTTP 200 or 500). REST uses many URLs + HTTP verbs + status codes + JSON.

Save curl/Postman examples into `/postman` as a collection for the mock suppliers.

```bash
git add . && git commit -m "feat: mock NDC (Go) and mock GDS (SOAP) suppliers"
```

---

## 9. Supplier integration layer ✅

Lives in `SkyBook.Application` (interfaces/models) and `SkyBook.Infrastructure` (clients).

### 9.1 Common abstraction ✅

```csharp
public interface IFlightSupplier
{
    string Name { get; }
    Task<IReadOnlyList<FlightOffer>> SearchAsync(FlightSearchRequest req, CancellationToken ct);
    Task<PricedOffer> PriceAsync(string offerId, CancellationToken ct);
    Task<SupplierBookingResult> BookAsync(SupplierBookingRequest req, CancellationToken ct);
    Task<SupplierTicketResult> IssueTicketAsync(string supplierRef, CancellationToken ct);
    Task<SupplierCancelResult> CancelAsync(string supplierRef, CancellationToken ct);
}

public record FlightOffer(
    string OfferId,          // e.g. "SkyGDS:9f2c..."  (prefix tells you which supplier)
    string Supplier,
    IReadOnlyList<Segment> Segments,
    decimal BaseFare, decimal Taxes, decimal Total,
    string Currency, string CabinClass, int SeatsAvailable,
    string BaggageAllowance, bool Refundable,
    DateTimeOffset ExpiresAt);
```

> *Concept — why an interface?* The rest of the app depends on `IFlightSupplier`, not on SOAP or REST. Adding a third supplier means adding one class; nothing else changes (Open/Closed + Dependency Inversion).

### 9.2 SOAP client (hand-rolled `HttpClient` + `XDocument`) ✅

Interviewers like seeing that you understand the envelope.

```csharp
public sealed class SkyGdsSupplier(HttpClient http, ILogger<SkyGdsSupplier> log) : IFlightSupplier
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Gds  = "http://skygds.mock/air/v1";
    public string Name => "SkyGDS";

    public async Task<IReadOnlyList<FlightOffer>> SearchAsync(FlightSearchRequest r, CancellationToken ct)
    {
        var envelope = new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", Soap),
                new XAttribute(XNamespace.Xmlns + "gds", Gds),
                new XElement(Soap + "Body",
                    new XElement(Gds + "AirShoppingRQ",
                        new XElement(Gds + "Origin", r.Origin),
                        new XElement(Gds + "Destination", r.Destination),
                        new XElement(Gds + "DepartureDate", r.DepartureDate.ToString("yyyy-MM-dd")),
                        new XElement(Gds + "Adults", r.Adults),
                        new XElement(Gds + "Cabin", r.Cabin)))));

        using var content = new StringContent(envelope.ToString(), Encoding.UTF8, "text/xml");
        content.Headers.Add("SOAPAction", "\"http://skygds.mock/air/v1/AirShopping\"");

        using var resp = await http.PostAsync("/soap/air", content, ct);
        var xml = XDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        var fault = xml.Descendants(Soap + "Fault").FirstOrDefault();
        if (fault is not null)
            throw new SupplierException(Name, fault.Element("faultstring")?.Value ?? "Unknown SOAP fault");

        return xml.Descendants(Gds + "Itinerary").Select(MapItinerary).ToList();
    }
    // MapItinerary(XElement) -> FlightOffer ...
}
```

### 9.3 REST client ✅

```csharp
public sealed class AeroNdcSupplier(HttpClient http) : IFlightSupplier
{
    public string Name => "AeroNDC";

    public async Task<IReadOnlyList<FlightOffer>> SearchAsync(FlightSearchRequest r, CancellationToken ct)
    {
        var resp = await http.PostAsJsonAsync("/v1/offers/search", new
        {
            origin = r.Origin, destination = r.Destination,
            date = r.DepartureDate.ToString("yyyy-MM-dd"),
            pax = new { adt = r.Adults, chd = r.Children, inf = r.Infants },
            cabin = r.Cabin
        }, ct);

        if (!resp.IsSuccessStatusCode)
            throw new SupplierException(Name, $"HTTP {(int)resp.StatusCode}");

        var body = await resp.Content.ReadFromJsonAsync<NdcSearchResponse>(cancellationToken: ct);
        return body!.Offers.Select(Map).ToList();
    }
}
```

### 9.4 Registration with resilience (in `Program.cs`) ✅

```csharp
builder.Services.AddHttpClient<SkyGdsSupplier>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Suppliers:SkyGds:BaseUrl"]!);
    c.Timeout = TimeSpan.FromSeconds(15);
})
.AddStandardResilienceHandler(o =>
{
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
    o.Retry.MaxRetryAttempts = 2;
});

// Same for AeroNdcSupplier, then:
builder.Services.AddScoped<IFlightSupplier>(sp => sp.GetRequiredService<SkyGdsSupplier>());
builder.Services.AddScoped<IFlightSupplier>(sp => sp.GetRequiredService<AeroNdcSupplier>());
builder.Services.AddMemoryCache();
builder.Services.AddScoped<FlightSearchService>();
```

Add to `appsettings.Development.json`: `"Suppliers": { "SkyGds": { "BaseUrl": "http://localhost:8080" }, "AeroNdc": { "BaseUrl": "http://localhost:8081" } }`.

> Only retry **idempotent** operations (search, price). Do **not** auto-retry Book or IssueTicket — a retry could create a double booking. Use a separate client without retries for those. Say this in the README.

### 9.5 Aggregator (parallel async search) ✅

```csharp
public sealed class FlightSearchService(IEnumerable<IFlightSupplier> suppliers,
                                        IOfferCache cache, ILogger<FlightSearchService> log)
{
    public async Task<SearchResult> SearchAsync(FlightSearchRequest req, CancellationToken ct)
    {
        var tasks = suppliers.Select(async s =>
        {
            try { return (s.Name, Offers: await s.SearchAsync(req, ct), Error: (string?)null); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Supplier {Supplier} search failed", s.Name);
                return (s.Name, Offers: (IReadOnlyList<FlightOffer>)[], Error: ex.Message);
            }
        });

        var results = await Task.WhenAll(tasks);

        var offers = results.SelectMany(r => r.Offers).OrderBy(o => o.Total).ToList();

        await cache.StoreAsync(offers, TimeSpan.FromMinutes(15)); // needed for Price/Book later

        return new SearchResult(offers,
            results.Where(r => r.Error != null).Select(r => new SupplierWarning(r.Name, r.Error!)).ToList());
    }
}
```

Key behaviors to explain in interviews:
- **Partial results**: if one supplier is down, still return the other's offers plus a warning (your UI shows the yellow banner).
- **Offer cache** (`IMemoryCache`): offer IDs from search are stored so Price/Book can find which supplier owns them.
- **Agent markup** applied after aggregation: `Total * (1 + MarkupPercent/100)`.

> *Concept — `async/await`:* while a supplier call waits for the network, the thread is released to serve other requests. `Task.WhenAll` starts both supplier calls at once, so total time ≈ the slowest supplier, not the sum. Never call `.Result` or `.Wait()` in a controller (thread-pool starvation).

### 9.6 Supplier logging ✅

A `DelegatingHandler` added to each `HttpClient` saves request/response bodies, status code and duration to `SupplierLogs` with the current correlation ID. **Mask passport numbers and DOB** before saving. This feeds the Admin → Supplier Logs screen — exactly how real OTA teams troubleshoot third-party integrations.

```bash
git add . && git commit -m "feat: supplier abstraction, SOAP+REST clients, aggregator"
```

---

## 10. Build the MVC web app (thin UI over the backend) ✅

This layer exists so you can *see and click* the backend working. **Keep it ugly and fast.** Rules for this section:

- **Thin controllers:** a controller action = read input → call a service (from Sections 7–9) → return a view or redirect. No business rules, no SQL, no supplier calls inside controllers.
- **Time box:** about 30–45 minutes per screen, then move on. No custom CSS, no JS frameworks, no design work.
- **Build the service first** (with a unit test), then the controller + a bare view that displays its result.
- If you are stuck on a view, render the ViewModel in a plain `<table>` or `<pre>@Model</pre>` and move on.

Each step: **service already done → controller → view model → bare view → test in the browser → commit.**

> **What the finished code does differently from the sketches below** (the repo is the source of truth; read the real files in `src/`):
> - **Search and results are one page** (`Flights/Search`, a GET form) instead of separate Search and Results views.
> - **Refunds:** cancelling a *ticketed* booking runs `usp_CancelBooking`, which creates the `Pending` refund itself. So there is no agent "Request refund" button; the admin approves it on `Admin/Refunds` (`AdminService.ApproveRefundAsync`: wallet credit + `Cancelled → Refunded` in one transaction).
> - **Services live in `SkyBook.Infrastructure`:** `BookingService` (create, ticket, cancel, expire), `AdminService` (agents, wallet, markup, refunds, supplier logs) and `BookingQueries` (Dapper stored-procedure calls). Controllers only call them.
> - **Two HTTP clients per supplier:** one with retries for search/price, one with retries switched off for book/ticket/cancel.
> - **Per-supplier search deadline of 6 s** in `FlightSearchService`, so a slow supplier becomes a warning instead of a slow page.
> - **Pre-built check, not just the SP:** ticketing checks the wallet *before* calling the supplier, and the SP re-checks under a lock.
> - **PNR is generated by SkyBook** (the supplier's reference is stored separately as `SupplierRef`).
> - `tests/e2e/smoke_e2e.py` drives the whole UI over HTTP against a running stack (38 checks).


### 10.1 Program.cs — the full pipeline ✅

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<SkyBookDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/AccessDenied";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
    });
builder.Services.AddAuthorization();

// supplier clients, FlightSearchService, BookingService, etc. (Section 9)
builder.Services.AddHostedService<HoldExpiryService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();     // who are you?   (must come before…)
app.UseAuthorization();      // what may you do?

app.MapControllerRoute(name: "areas", pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

public partial class Program { }   // lets integration tests reference Program
```

> **Order matters.** Middleware runs top to bottom for requests. `UseAuthentication` must precede `UseAuthorization`; both go after `UseRouting`.

### 10.2 Step 1 — Layout and navigation ✅

Edit `Views/Shared/_Layout.cshtml` so the navbar shows different links per role:

```cshtml
<nav class="navbar navbar-expand-lg navbar-dark bg-primary">
  <div class="container">
    <a class="navbar-brand" asp-controller="Home" asp-action="Index">✈ SkyBook</a>
    <ul class="navbar-nav me-auto">
      @if (User.IsInRole("Agent"))
      {
        <li class="nav-item"><a class="nav-link" asp-controller="Flights" asp-action="Search">Search</a></li>
        <li class="nav-item"><a class="nav-link" asp-controller="Bookings" asp-action="Index">My Bookings</a></li>
      }
      @if (User.IsInRole("Admin"))
      {
        <li class="nav-item"><a class="nav-link" asp-area="Admin" asp-controller="Agents" asp-action="Index">Agents</a></li>
        <li class="nav-item"><a class="nav-link" asp-area="Admin" asp-controller="Bookings" asp-action="Index">All Bookings</a></li>
        <li class="nav-item"><a class="nav-link" asp-area="Admin" asp-controller="Reports" asp-action="Sales">Sales</a></li>
        <li class="nav-item"><a class="nav-link" asp-area="Admin" asp-controller="SupplierLogs" asp-action="Index">Supplier Logs</a></li>
      }
    </ul>
    @if (User.Identity?.IsAuthenticated == true)
    {
      <form asp-controller="Account" asp-action="Logout" method="post" class="d-flex">
        <span class="navbar-text me-3">@User.Identity.Name</span>
        <button class="btn btn-outline-light btn-sm">Logout</button>
      </form>
    }
  </div>
</nav>
<div class="container mt-4">
  <partial name="_Alerts" />
  @RenderBody()
</div>
```

Create `Views/Shared/_Alerts.cshtml` (a **partial view**) that renders `TempData["Success"]` and `TempData["Error"]` as Bootstrap alerts. Controllers set `TempData["Success"] = "Ticket issued.";` then redirect.

### 10.3 Step 2 — Login / Logout (authentication) ✅

**ViewModel** (`Models/LoginViewModel.cs`):

```csharp
public class LoginViewModel
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
```

**Controller** (`Controllers/AccountController.cs`):

```csharp
public class AccountController(SkyBookDbContext db, IPasswordHasher<User> hasher) : Controller
{
    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await db.Users.Include(u => u.Agent)
                                 .SingleOrDefaultAsync(u => u.Email == vm.Email && u.IsActive);
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, vm.Password)
                            == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError("", "Invalid email or password.");
            return View(vm);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Email),
            new(ClaimTypes.Role, user.Role),                       // "Admin" or "Agent"
        };
        if (user.AgentId is int agentId) claims.Add(new Claim("AgentId", agentId.ToString()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(principal);

        if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl)) return Redirect(vm.ReturnUrl);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }
}
```

Register `builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();`.

**View** (`Views/Account/Login.cshtml`):

```cshtml
@model LoginViewModel
<form asp-action="Login" method="post" class="col-md-4 mx-auto">
  <h3>Sign in</h3>
  <div asp-validation-summary="ModelOnly" class="text-danger"></div>
  <input type="hidden" asp-for="ReturnUrl" />
  <div class="mb-3">
    <label asp-for="Email" class="form-label"></label>
    <input asp-for="Email" class="form-control" />
    <span asp-validation-for="Email" class="text-danger"></span>
  </div>
  <div class="mb-3">
    <label asp-for="Password" class="form-label"></label>
    <input asp-for="Password" class="form-control" />
    <span asp-validation-for="Password" class="text-danger"></span>
  </div>
  <button class="btn btn-primary w-100">Login</button>
</form>
@section Scripts { <partial name="_ValidationScriptsPartial" /> }
```

Protect everything else: add `[Authorize]` to controllers (or set a global fallback policy), `[Authorize(Roles = "Agent")]` on agent controllers and `[Authorize(Roles = "Admin")]` on admin ones.

**Test:** log in as the seeded agent; the nav shows Search/My Bookings. Visit `/Admin/Agents` as an agent → redirected to Access Denied.

> *Concepts learned:* `[HttpGet]/[HttpPost]` overloads, anti-forgery, model binding, `ModelState`, claims, roles, cookie auth, open-redirect protection (`Url.IsLocalUrl`).

### 10.4 Step 3 — Flight search form ✅

**ViewModel:**

```csharp
public class FlightSearchViewModel
{
    [Required, StringLength(3, MinimumLength = 3), Display(Name = "From")] public string Origin { get; set; } = "";
    [Required, StringLength(3, MinimumLength = 3), Display(Name = "To")]   public string Destination { get; set; } = "";
    [Required, DataType(DataType.Date), Display(Name = "Departure date")]  public DateTime DepartureDate { get; set; } = DateTime.Today.AddDays(7);
    [Range(1, 9)] public int Adults { get; set; } = 1;
    [Range(0, 9)] public int Children { get; set; }
    [Range(0, 9)] public int Infants { get; set; }
    public string Cabin { get; set; } = "Economy";

    public static readonly string[] Airports = ["DAC","CGP","CXB","ZYL","JSR","SPD","BZL","CCU","KUL","BKK","DXB","SIN"];
}
```

**Controller** (`FlightsController`, `[Authorize(Roles="Agent")]`):

- `GET Search` → `View(new FlightSearchViewModel())`
- `POST Search` → validate (origin ≠ destination, date not in the past, infants ≤ adults); if invalid, return the same view with errors; otherwise call `FlightSearchService.SearchAsync` and return `View("Results", resultsViewModel)`.

For learning simplicity the POST renders results directly. (Cleaner alternative: make the search form a `method="get"` so results are bookmarkable and the browser's refresh button is safe — searching has no side effects, so GET is correct. Do that.)

**View:** a form with `<select asp-for="Origin" asp-items="...">` dropdowns, date input, passenger counts, **Search** button. Fields are rendered with `asp-for`, so validation attributes become client-side validation automatically.

### 10.5 Step 4 — Results page ✅

**ViewModel:**

```csharp
public class FlightResultsViewModel
{
    public FlightSearchViewModel Search { get; set; } = new();
    public IReadOnlyList<OfferRow> Offers { get; set; } = [];
    public IReadOnlyList<SupplierWarning> Warnings { get; set; } = [];
}
```

**View** (loop with `@foreach`, a warning banner for each `Warnings` entry, and a **Select** link per offer):

```cshtml
@foreach (var w in Model.Warnings)
{
  <div class="alert alert-warning">⚠ @w.Supplier did not respond (@w.Message). Showing results from the other supplier only.</div>
}
@foreach (var o in Model.Offers)
{
  <div class="card mb-2"><div class="card-body d-flex justify-content-between">
    <div>
      <strong>@o.Carrier @o.FlightNo</strong> @o.Departure.ToString("HH:mm") → @o.Arrival.ToString("HH:mm")
      <div class="text-muted">@o.Supplier · @(o.Refundable ? "Refundable" : "Non-refundable") · Bag @o.Baggage</div>
    </div>
    <div class="text-end">
      <div class="fs-5">@o.Currency @o.Total.ToString("N0")</div>
      <a class="btn btn-primary btn-sm" asp-controller="Bookings" asp-action="Create" asp-route-offerId="@o.OfferId">Select</a>
    </div>
  </div></div>
}
@if (!Model.Offers.Any()) { <p>No flights found.</p> }
```

Tip: extract each card as a **partial view** (`_OfferCard.cshtml`) with `<partial name="_OfferCard" model="o" />`.

**Test:** search DAC → CGP with both mocks running. Then stop the Go mock and search again — you should see the warning banner and only GDS offers. Search `ERR` → `CGP` to trigger a SOAP fault, and `DAC` → `SLW` for the timeout (page should still return within ~7 s).

### 10.6 Step 5 — Review price & passenger details (book) ✅

This is the most important screen.

`BookingsController.Create` **GET** `(string offerId)`:
1. Look up the offer in the cache; if missing/expired → `TempData["Error"] = "Offer expired, search again."` and redirect to Search.
2. **Re-price** with the owning supplier (`PriceAsync`). If the new total differs, set `vm.PriceChanged = true` and show an alert with old/new fare — the agent must confirm.
3. Build `BookingCreateViewModel` with the offer summary, a list of empty `PassengerInput` rows (one per traveller), and a **new `Guid IdempotencyKey`** stored in a hidden field.

`BookingsController.Create` **POST**:

```csharp
[HttpPost, ValidateAntiForgeryToken]
public async Task<IActionResult> Create(BookingCreateViewModel vm, CancellationToken ct)
{
    if (!ModelState.IsValid) return View(vm);

    var errors = PassengerRules.Validate(vm.Passengers, vm.TravelDate, vm.IsInternational);
    foreach (var e in errors) ModelState.AddModelError("", e);
    if (!ModelState.IsValid) return View(vm);

    try
    {
        var pnr = await bookingService.CreateAsync(AgentId, vm.OfferId, vm.ExpectedTotal,
                                                   vm.IdempotencyKey, MapPassengers(vm), ct);
        TempData["Success"] = $"Booking created. PNR {pnr}. Ticket before the hold expires.";
        return RedirectToAction(nameof(Details), new { pnr });          // PRG
    }
    catch (PriceChangedException ex)
    {
        ModelState.AddModelError("", $"Price changed to {ex.NewTotal:N0}. Please review and confirm.");
        vm.ExpectedTotal = ex.NewTotal; vm.PriceChanged = true;
        return View(vm);
    }
    catch (SupplierException ex)
    {
        ModelState.AddModelError("", $"Supplier error: {ex.Message}");
        return View(vm);
    }
}
```

`AgentId` is a helper property: `int.Parse(User.FindFirstValue("AgentId")!)`. **Never** accept an agent ID from a form — it comes from the signed-in user's claims.

Business rules (in `PassengerRules`, a plain static class so it's unit-testable):
- Name required; max 9 passengers; infants ≤ adults.
- Child/infant age must match DOB at the travel date.
- Passport required for international routes.
- Same `IdempotencyKey` returns the existing booking instead of creating a new one (unique index backs this up). Why a hidden key? Double-clicking the **Book** button or hitting refresh would otherwise create two PNRs.

**Dynamic passenger rows:** render with an index so model binding works:
```cshtml
@for (int i = 0; i < Model.Passengers.Count; i++)
{
  <input asp-for="Passengers[i].FirstName" class="form-control" />
  ...
}
```

### 10.7 Step 6 — My bookings + details ✅

**`Index`** (`GET /Bookings?status=&page=`): query only the signed-in agent's bookings with paging; table with PNR link, route, status badge, total, created date; Previous/Next links.

**`Details`** (`GET /Bookings/Details/{pnr}`): load by PNR **and** `AgentId == currentAgent`; if not found → `NotFound()` (agent A must not learn that agent B's PNR exists).

Action buttons are separate **POST forms** (never GET links for state changes!):

```cshtml
@if (Model.CanIssue)
{
  <form asp-action="Ticket" asp-route-pnr="@Model.Pnr" method="post" class="d-inline">
    <button class="btn btn-success" onclick="return confirm('Issue ticket and debit @Model.Total?')">Issue Ticket</button>
  </form>
}
@if (Model.CanCancel) { <form asp-action="Cancel" ...><button class="btn btn-outline-danger">Cancel</button></form> }
@if (Model.CanRefund) { <form asp-action="Refund" ...><button class="btn btn-warning">Request Refund</button></form> }
```

`CanIssue = BookingTransitions.CanMove(Status, Ticketed)` — the state machine drives the UI.

Each POST action: call service → set `TempData["Success"]`/`["Error"]` → `RedirectToAction("Details", new { pnr })`. Catch `DomainException` (e.g. invalid transition, insufficient balance — map SQL error numbers 50001–50003) and show `TempData["Error"]`.

**Ticket flow in the service**, in order: (1) load booking + check ownership; (2) call supplier `IssueTicketAsync` (no retry); (3) call `usp_IssueTicket` via Dapper (wallet debit + status change in one transaction); (4) create `Ticket` rows. On failure at (3) after (2) succeeded → log at `Error` level, flag booking `NeedsReview`.

### 10.8 Step 7 — Dashboard ✅

`HomeController.Index` for an Agent shows wallet balance, credit limit, last 5 bookings, and a big "Search flights" button. For Admin, show counts (agents, today's bookings, pending refunds). Teaches: different ViewModels per role, `ViewComponent` (optional) for the wallet widget in the navbar.

### 10.9 Step 8 — Admin area ✅

Create folders `Areas/Admin/Controllers`, `Areas/Admin/Views`, `Areas/Admin/Models`. Add `Areas/Admin/Views/_ViewImports.cshtml` and `_ViewStart.cshtml` (copy from `Views/`). Every Admin controller gets:

```csharp
[Area("Admin"), Authorize(Roles = "Admin")]
public class AgentsController : Controller { ... }
```

| Controller | Actions / screens |
|---|---|
| `AgentsController` | `Index` (list + filter), `Create` (creates `Agent` + agent `User`), `Edit` markup %, `ToggleStatus` (POST), `TopUp` (POST; inserts a `WalletTransaction`) |
| `BookingsController` | `Index` — search form → `usp_SearchBookings` via Dapper, paged table |
| `ReportsController` | `Sales` — `usp_AgentSalesReport`, table by agent/month |
| `SupplierLogsController` | `Index?correlationId=` — table + expandable request/response XML/JSON (`<details>` element) |
| `RefundsController` | `Index` (pending), `Approve` (POST) → credit wallet, set booking `Refunded` |

**Wallet top-up concurrency note:** `Agent` has a `RowVersion`. If two admins edit the same agent, EF throws `DbUpdateConcurrencyException` → catch and show "This agent was changed by someone else, reload".

### 10.10 Step 9 — Hold-expiry background job ✅

`HoldExpiryService : BackgroundService` runs every minute using `IServiceScopeFactory` to create a scope (DbContext is scoped), finds `Held` bookings with `HoldExpiresAt < now` (uses the filtered index), cancels them at the supplier, marks them `Expired`.

### 10.11 UI polish: deliberately skipped ✅

Do not spend time here. Only: use the Bootstrap classes the template already includes (`table`, `alert`, `badge`), and take 3–4 plain screenshots for the README (`docs/screenshots/`) once the backend works: results page with the supplier warning, booking details, supplier logs.

```bash
git add . && git commit -m "feat(web): login, search, booking, admin screens"   # commit after every step, not just once
```

---

## 11. Cross-cutting concerns

- **Error page**: `/Home/Error` shows a friendly message and the `TraceId`/correlation ID so a user can quote it to support. Controllers catch *expected* exceptions (`DomainException`, `SupplierException`, `PriceChangedException`) and show alerts; everything else falls to the global handler.
- **Correlation ID middleware**: read `X-Correlation-Id` or generate one, push into Serilog `LogContext`, forward to suppliers as a header, show on the error page and store in `SupplierLogs`.
- **Config**: `appsettings.json` + environment variables; no secrets committed (`dotnet user-secrets` locally, env vars in Docker).
- **CancellationToken**: accept it in async controller actions and pass it down.
- **Authorization rule of thumb**: authenticate with cookies, authorize by role at the controller, and *scope data by `AgentId` from claims* in every query.
- **Security basics you should be able to name**: anti-forgery tokens on all POSTs, HTML encoding by Razor (XSS), parameterized queries via EF/Dapper (SQL injection), hashed passwords, open-redirect check.

---

## 12. Testing

### 12.1 Unit tests (aim for 20+)

Run with `dotnet test`.

- `BookingTransitions`: every valid and invalid transition.
- `FlightSearchService`: both suppliers succeed; one throws → partial results + warning; both throw; results sorted by price; markup applied.
- `PassengerRules`: infant > adults, missing passport on international, child DOB mismatch.
- Refund penalty calculation.
- SOAP response mapping: feed a sample XML string → assert `FlightOffer` fields; SOAP fault → `SupplierException`.
- **Controller tests** (MVC specific): create `FlightsController` with fake services and assert `result.Should().BeOfType<ViewResult>()` and the model type; assert a failed POST returns the same view with `ModelState` errors; assert a successful booking returns `RedirectToActionResult`.

Example:

```csharp
[Fact]
public async Task Search_returns_partial_results_when_one_supplier_fails()
{
    var ok = Substitute.For<IFlightSupplier>();
    ok.Name.Returns("AeroNDC");
    ok.SearchAsync(default!, default).ReturnsForAnyArgs([TestData.Offer(total: 5200)]);

    var bad = Substitute.For<IFlightSupplier>();
    bad.Name.Returns("SkyGDS");
    bad.SearchAsync(default!, default).ThrowsAsyncForAnyArgs(new SupplierException("SkyGDS", "timeout"));

    var sut = new FlightSearchService([ok, bad], new FakeOfferCache(), NullLogger<FlightSearchService>.Instance);

    var result = await sut.SearchAsync(TestData.DacToCgp(), CancellationToken.None);

    result.Offers.Should().HaveCount(1);
    result.Warnings.Should().ContainSingle(w => w.Supplier == "SkyGDS");
}
```

### 12.2 Integration tests

`WebApplicationFactory<Program>` boots your whole MVC app in memory. Replace suppliers with fakes and use a test database. Because the app uses cookie auth + anti-forgery, a test must: GET the login page → read the `__RequestVerificationToken` hidden field → POST credentials with the token → keep the cookie (`HandleCookies = true` in `WebApplicationFactoryClientOptions`).

Tests to write:
- Anonymous request to `/Bookings` → redirects (302) to `/Account/Login`.
- Agent can't open `/Admin/Agents` → redirect to AccessDenied.
- Full flow: login → search → book → ticket → cancel → refund; assert wallet balance changes.
- Idempotency: two POSTs with the same key → one booking.
- Agent A cannot open agent B's booking (expect 404).
- Concurrency: fire 5 parallel ticket requests whose total exceeds the wallet → only the affordable ones succeed (proves `UPDLOCK` works).

### 12.3 Manual test cases (`docs/test-cases.md`)

Write 20–30 like a QA engineer, with a screen-based flavour:

| ID | Scenario | Steps | Expected | Result |
|---|---|---|---|---|
| TC-01 | One-way search DAC→CGP | Fill the search form, click Search | Offers from both suppliers, sorted by price | Pass |
| TC-07 | Supplier timeout | Search DAC→SLW | AeroNDC offers only + SkyGDS warning banner, page < 7s | Pass |
| TC-12 | Price changed before booking | Select an offer that re-prices higher | Alert shows old/new fare; booking only after confirm | Pass |
| TC-15 | Ticket with insufficient balance | Click Issue Ticket with wallet < fare | Red alert "Insufficient wallet balance"; booking stays Held | Pass |
| TC-18 | Double-click Book | Click Book twice quickly | Only one PNR created | Pass |
| TC-20 | Cancel already-cancelled booking | Resubmit cancel | Error alert; no Cancel button shown | Pass |
| TC-24 | Agent opens another agent's PNR URL | Edit the PNR in the URL | 404 page | Pass |

### 12.4 Postman collection

Postman is for the **mock suppliers** (REST for AeroNDC; XML body for SkyGDS), not for the MVC app (it returns HTML). Environment variables `ndcUrl`, `gdsUrl`; every request has `pm.test(...)` assertions. Export to `/postman`.

---

## 13. Docker Compose

Add a `Dockerfile` in `src/SkyBook.Web`, `src/SkyBook.MockGds`, and `mock-ndc-go`. The .NET one (multi-stage; build from the repo root so project references resolve):

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SkyBook.Web -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "SkyBook.Web.dll"]
```

Go Dockerfile:

```dockerfile
FROM golang:1.22-alpine AS build
WORKDIR /app
COPY . .
RUN go build -o mockndc .

FROM alpine:3.20
COPY --from=build /app/mockndc /mockndc
EXPOSE 8081
ENTRYPOINT ["/mockndc"]
```

`docker-compose.yml`:

```yaml
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "Your_strong_Pass123"
    ports: ["1433:1433"]

  mock-gds:
    build:
      context: .
      dockerfile: src/SkyBook.MockGds/Dockerfile
    ports: ["8080:8080"]

  mock-ndc:
    build: ./mock-ndc-go
    ports: ["8081:8081"]

  web:
    build:
      context: .
      dockerfile: src/SkyBook.Web/Dockerfile
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Default: "Server=sqlserver;Database=SkyBook;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True"
      Suppliers__SkyGds__BaseUrl: "http://mock-gds:8080"
      Suppliers__AeroNdc__BaseUrl: "http://mock-ndc:8081"
    ports: ["5000:8080"]
    depends_on: [sqlserver, mock-gds, mock-ndc]
```

The web app should apply EF migrations and seed demo data on startup in Development (with a retry loop, because SQL Server takes ~20 s to boot).

**Goal:** `docker compose up --build` → open `http://localhost:5000` → log in with the demo user → everything works. Reviewers won't spend more than 2 minutes setting up.

---

## 14. CI with GitHub Actions

`.github/workflows/ci.yml`:

```yaml
name: CI
on: [push, pull_request]
jobs:
  dotnet:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet restore
      - run: dotnet build --no-restore -c Release
      - run: dotnet test tests/SkyBook.UnitTests --no-build -c Release
  go:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-go@v5
        with: { go-version: '1.22' }
      - run: cd mock-ndc-go && go vet ./... && go build ./...
```

Add the CI badge to the README.

---

## 15. Day-by-day plan (Oct 25 → Oct 30)

"MVP" = must-have. Cut from the bottom of the list if you fall behind.

### Day 1 — Sun Oct 25 (today): Setup + foundation
- [x] Install everything (Section 4), verify each tool
- [ ] Create solution, run the template site, do the About-page exercise (Section 5)
- [x] Domain entities + `BookingTransitions` + unit tests (Section 6)
- [x] SQL Server in Docker, DbContext, first migration (Section 7.1–7.4) — all of Section 7 is done, including stored procedures, Dapper, seeding and the index experiment

### Day 2 — Mon Oct 26: Mock suppliers + login
- [x] Go MockNdc: search, price, order, ticket, cancel, health
- [x] MockGds SOAP with `ERR` / `SLW` triggers
- [x] Seed users; bare login page + roles (10.1–10.3)

### Day 3 — Tue Oct 27: Integration layer (backend-heavy)
- [x] `IFlightSupplier`, both clients, resilience, supplier logging handler, aggregator, offer cache
- [x] Unit tests for aggregator + XML mapping
- [x] Bare search form + results page to see it work (10.4–10.5)

### Day 4 — Wed Oct 28: Booking lifecycle + SQL (backend-heavy)
- [x] `BookingService`: re-price, idempotency, create/ticket/cancel (refund approval is in `AdminService`)
- [x] 4 stored procedures via Dapper (tried by hand and through the e2e script; no automated SQL tests yet)
- [x] Bare booking, details and my-bookings pages (10.6–10.7)

### Day 5 — Thu Oct 29: Admin backend + packaging
- [x] Admin services (agents, top-up, markup, booking search, supplier logs); bare tables only (10.9)
- [x] Hold-expiry job; integration tests (full flow, idempotency, concurrency, isolation) — 16 tests in `tests/SkyBook.IntegrationTests`, plus controller unit tests
- [x] Dockerfiles + Compose; clean `docker compose up --build` tested (e2e smoke 38/38)
- [ ] CI green (workflow written, runs unit + integration + Go; needs a push to GitHub to confirm); [x] test-cases.md; [x] index experiment notes

### Day 6 — Fri Oct 30: Apply (deadline day — don't leave it to the last hour)
- [x] README with screenshots + architecture diagram (Graphviz source in `docs/architecture.dot`)
- [ ] Update CV + cover letter (Section 17)
- [ ] Submit application **in the morning**

**If you fall behind, cut in this order:** concurrency test → sales report → hold-expiry job → refunds approval screen → Go CI job. **Never cut:** both suppliers, the search → book → ticket flow with UI, at least one stored procedure via Dapper, unit tests, README with screenshots, Docker Compose.

### Stretch goals (after applying, before interview)
- Round-trip and multi-city search
- Redis instead of `IMemoryCache`
- Outbox pattern / RabbitMQ for "ticket issued" email events
- Rate limiting (`AddRateLimiter`) on search
- Print-friendly e-ticket page (PDF)
- A JSON API controller (`[ApiController]` + Swagger) alongside the MVC site
- OpenTelemetry tracing across the three services

---

## 16. README template for your GitHub repo

```markdown
# SkyBook OTA – Flight Booking Web App (ASP.NET Core MVC)
![CI](badge-url)

An Online Travel Agency web application. Travel agents search flights aggregated from a
SOAP-based GDS and a REST-based NDC airline API, then book, ticket, cancel and refund.
Admins manage agents, wallets, markup and troubleshoot supplier calls.

![Search results](docs/screenshots/results.png)

## Features
- Parallel multi-supplier flight search (SOAP/XML + REST/JSON) with partial-failure handling
- Search → Price → Book (PNR) → Ticket → Cancel → Refund with a domain state machine
- Agent wallet with atomic ticketing via MSSQL stored procedure (UPDLOCK, transactions)
- Admin area: agents, markup, top-ups, booking search, sales report, supplier logs
- Idempotent booking, price-change detection, automatic hold expiry
- Cookie auth with Admin/Agent roles; anti-forgery, validation
- Go microservice (mock airline NDC API)

## Tech stack
ASP.NET Core 8 MVC (Razor) · C# · EF Core · Dapper · SQL Server · Go · xUnit · Docker · GitHub Actions

## Architecture
(diagram + layer explanation)

## Run it
docker compose up --build
Open http://localhost:5000
Demo users: admin@skybook.test / Admin@123, agent1@skybook.test / Agent@123

## Try these scenarios
(search DAC→CGP, trigger supplier failure with ERR, timeout with SLW, price change, insufficient balance ...)

## Design decisions
- Why EF Core + Dapper together
- Why Book/Ticket are not retried
- What happens if the supplier tickets but the DB update fails
- Why state changes are POST + redirect (PRG) with anti-forgery
- Indexing results (docs/db-performance-notes.md)

## Testing
dotnet test · test cases in /docs/test-cases.md · Postman collection for mock suppliers in /postman

## Screenshots
(login, search, results, review, booking details, admin agents, supplier logs)
```

---

## 17. Using the project in your application

**CV project entry (adapt wording to what you actually built):**

> **SkyBook OTA – Flight Booking Web App** (ASP.NET Core 8 MVC, C#, Go, MSSQL, Docker) — github.com/you/skybook-ota
> - Built a server-rendered ASP.NET Core MVC OTA application (Razor views, role-based cookie auth, admin area) covering search, pricing, PNR creation, ticketing, cancellation and refund.
> - Aggregated flight offers from a SOAP/XML GDS and a REST/JSON NDC supplier in parallel with `async/await`, returning partial results when a supplier fails.
> - Wrote MSSQL stored procedures for atomic agent-wallet ticketing using row locks and transactions; reduced logical reads on booking history with a covering index.
> - Developed a Go microservice simulating an airline NDC API; containerized all services with Docker Compose and added CI with GitHub Actions.
> - Wrote 25+ xUnit unit/integration tests plus a written test-case suite covering failure and concurrency scenarios.

**Cover letter line:** say you built this specifically to understand OTA supplier integrations and the booking-to-refund workflow, and that you'd like to apply it to Triplover/Firsttrip's GDS and NDC integrations.

---

## 18. Interview prep

Learn these well; they're likely given the JD.

1. **Explain MVC and the request lifecycle in ASP.NET Core.** Request → middleware pipeline → routing → controller action → model binding/validation → service calls → ViewModel → Razor view → HTML response.
2. **ViewModel vs entity?** A ViewModel is shaped for one screen and contains only what that view needs; it prevents over-posting and keeps EF entities out of the UI.
3. **What is model binding / over-posting / anti-forgery (CSRF)?**
4. **Authentication vs authorization; cookie vs JWT?** Cookie for server-rendered browser apps; JWT for stateless API clients.
5. **REST vs SOAP?** SOAP: XML envelope, WSDL contract, `SOAPAction`, faults; common in GDS. REST: resources, JSON, verbs, status codes; NDC and modern airlines.
6. **What is a GDS / NDC / PNR?** GDS = global distribution system (Sabre, Amadeus, Travelport). NDC = IATA's New Distribution Capability standard for airlines to sell directly. PNR = Passenger Name Record, the booking reference.
7. **Why price again before booking?** Search results are cached and fares change; booking at a stale price causes losses.
8. **How do you avoid double booking / double charging?** Idempotency key in a hidden field + unique index, PRG after POST, no automatic retries on non-idempotent supplier calls, DB transactions with `UPDLOCK`.
9. **`async/await` — what does it do?** Frees the thread during I/O; `Task.WhenAll` for parallel supplier calls; avoid `.Result`/`.Wait()`.
10. **EF Core vs Dapper?** EF: productivity, change tracking, migrations. Dapper: hand-tuned SQL, stored procedures, high-volume reads.
11. **Clustered vs non-clustered index, covering index, filtered index, parameter sniffing.**
12. **Stored procedure vs inline query trade-offs.**
13. **Debug "bookings fail for one airline since this morning"?** Correlation ID → supplier logs screen → compare request/response → check supplier status/credentials/schema change → reproduce with Postman.
14. **Supplier issues the ticket but your DB write fails?** Log, flag for reconciliation, background reconciliation job; discuss eventual consistency / outbox.
15. **SOLID in your code:** `IFlightSupplier` = open/closed + dependency inversion; adding a third supplier needs no change to the aggregator.
16. **DSA basics:** array/string/hashmap problems, sorting, binary search, BFS on a route graph (e.g. "find connecting flights with ≤1 stop").

---

## 19. Final checklist

- [ ] Repo is **public**, pinned, with a clear description and topics (`aspnetcore`, `aspnet-mvc`, `dotnet`, `golang`, `sql-server`, `ota`, `gds`, `ndc`)
- [ ] `docker compose up --build` works from a fresh clone
- [ ] No secrets or real personal data committed
- [ ] README has screenshots, architecture diagram, run instructions, demo credentials, design decisions
- [ ] CI badge is green
- [ ] At least 20 meaningful commits with readable messages (not one giant "final" commit)
- [ ] You can explain every file in the repo — interviewers will ask

Good luck with the application.
