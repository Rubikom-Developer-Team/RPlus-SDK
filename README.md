# RPlus.PartnerSdk (.NET)

This folder contains a standalone .NET SDK for the RPlus Partner Scan API.
It is meant to be reused by POS adapters (iiko, r_keeper, custom POS, etc.).

Build:

```powershell
cd sdk
dotnet build .\\RPlus.PartnerSdk-dotnet.sln -c Release
```

## Usage

```csharp
using RPlus.PartnerSdk.Http;
using RPlus.PartnerSdk.Models;
using RPlus.PartnerSdk.Reliability;
// Optional (separate project): using RPlus.PartnerSdk.Sqlite;

var client = new PartnerClient(new PartnerSdkOptions
{
  BaseUrl = "https://api.example.com",
  IntegrationKey = "pk_live_...",
  SigningSecret = "sk_live_...", // required for orders/closed
  UserAgent = "mypos/1.0 rplus-sdk/1.0"
});

var scan = await client.ScanAsync(new ScanRequest
{
  QrToken = "eyJhbGciOi...",
  OrderId = "order-123",
  OrderSum = 2500.00m,
  TerminalId = "POS-01",
  CashierId = "cashier-1",
  Context = "partner"
}, idempotencyKey: Guid.NewGuid().ToString(), traceId: "trace-123");

// Reliable (soft-mode) commit: enqueue and let background worker retry on network/5xx.
var store = new FileCommitQueueStore("C:\\\\posdata\\\\rplus_pending_commits.json");
var queue = new ReliableCommitQueue("https://api.example.com", client, store);
await queue.InitializeAsync();
queue.Start();

await queue.EnqueueOrderClosedAsync(new OrderClosedRequest
{
  ScanId = scan.ScanId,
  OrderId = "order-123",
  ClosedAt = DateTimeOffset.UtcNow,
  FinalOrderTotal = 17025.00m
});

// Optional: reliable telemetry/events (do not block POS).
var eventsStore = new FileEventQueueStore("C:\\\\posdata\\\\rplus_events.json");
var events = new ReliableEventQueue("https://api.example.com", client, eventsStore);
await events.InitializeAsync();
events.Start();
await events.EnqueueAsync(new PartnerEvent
{
  Type = "scan_succeeded",
  ScanId = scan.ScanId,
  OrderId = "order-123",
  TerminalId = "POS-01",
  Details = new System.Collections.Generic.Dictionary<string, string>
  {
    ["discountUser"] = scan.DiscountUser.ToString("F2"),
    ["discountPartner"] = scan.DiscountPartner.ToString("F2"),
  }
});
```

## SQLite Stores (Optional)

If you want a single local database instead of JSON files, use the separate project `RPlus.PartnerSdk.Sqlite`:

```csharp
using RPlus.PartnerSdk.Sqlite;

var commitStore = new SqliteCommitQueueStore("C:\\\\posdata\\\\rplus_queue.db");
var commitQueue = new ReliableCommitQueue("https://api.example.com", client, commitStore);
await commitQueue.InitializeAsync();
commitQueue.Start();

var eventStore = new SqliteEventQueueStore("C:\\\\posdata\\\\rplus_queue.db");
var eventQueue = new ReliableEventQueue("https://api.example.com", client, eventStore);
await eventQueue.InitializeAsync();
eventQueue.Start();
```

## Contract

See `POS_INTEGRATION_SPEC_V1.md` in the main plugin folder for the server contract (headers, signing, idempotency).
