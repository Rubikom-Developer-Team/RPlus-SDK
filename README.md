# RPlus.PartnerSdk (.NET)

Публичный SDK для интеграции POS-систем с API программы лояльности RPlus.

SDK закрывает типовой поток:
1. Проверка QR/OTP (`scan`/`shortcode`).
2. Передача финала заказа (`orders/closed`).
3. Передача отмены (`orders/cancelled`).
4. Отправка событий/телеметрии (`events`).

Подходит для iiko, r_keeper и любых кастомных POS-решений на .NET.

## Что умеет SDK

- Упрощает работу с HTTP API (готовый `PartnerClient`).
- Автоматически ставит служебные заголовки (`X-Integration-Key`, `Idempotency-Key`, `X-Trace-Id`).
- Поддерживает HMAC-подпись запросов:
  - `X-Integration-Signature`
  - `X-Integration-Timestamp`
- Нормализует OTP (в scan-запросах).
- Даёт надежные очереди с ретраями:
  - `ReliableCommitQueue` для `orders/closed` и `orders/cancelled`
  - `ReliableEventQueue` для `events`
- Есть 2 варианта хранения очереди:
  - JSON-файлы (`FileCommitQueueStore`, `FileEventQueueStore`)
  - SQLite (проект `RPlus.PartnerSdk.Sqlite`)

## Совместимость

- `RPlus.PartnerSdk`: `netstandard2.0`
- `RPlus.PartnerSdk.Sqlite`: `netstandard2.0`
- Работает с:
  - .NET Framework 4.7.2+
  - .NET 6/7/8+

## Cross-platform status

CI runs on every push and pull request for:
- Windows
- Linux
- macOS

Workflow file:
- `.github/workflows/ci.yml`

## Релизы

Автоматический workflow релиза:
- `.github/workflows/release.yml`

Как выпустить новую версию:
1. Создать тег формата `vX.Y.Z` (например `v1.1.0`).
2. Запушить тег в GitHub.
3. Workflow сам:
  - соберет и протестирует SDK,
  - сформирует `.nupkg` и `.snupkg`,
  - создаст GitHub Release с артефактами.

Опционально: если в репозитории задан секрет `NUGET_API_KEY`, пакеты автоматически публикуются в NuGet.org.

## Установка

Сейчас самый простой путь: project reference или исходники из репозитория.

Пример:

```xml
<ItemGroup>
  <ProjectReference Include="src\RPlus.PartnerSdk\RPlus.PartnerSdk.csproj" />
</ItemGroup>
```

Если нужен SQLite-стор:

```xml
<ItemGroup>
  <ProjectReference Include="src\RPlus.PartnerSdk.Sqlite\RPlus.PartnerSdk.Sqlite.csproj" />
</ItemGroup>
```

## Сборка

```powershell
cd sdk
dotnet build .\RPlus.PartnerSdk-dotnet.sln -c Release
```

## Быстрый старт (минимальная интеграция)

```csharp
using RPlus.PartnerSdk.Http;
using RPlus.PartnerSdk.Models;

var client = new PartnerClient(new PartnerSdkOptions
{
  BaseUrl = "https://api.example.com",
  IntegrationKey = "pk_live_xxx",
  SigningSecret = "sk_live_xxx", // обязателен для orders/closed
  UserAgent = "mypos/1.0 rplus-sdk/1.0"
});

// 1) Сканирование QR или OTP
var scan = await client.ScanAsync(
  new ScanRequest
  {
    QrToken = "eyJhbGciOi...", // или OtpCode = "123456"
    OrderId = "order-123",
    OrderSum = 14985.00m,
    TerminalId = "POS-01",
    CashierId = "cashier-1",
    Context = "partner"
  },
  idempotencyKey: Guid.NewGuid().ToString(),
  traceId: Guid.NewGuid().ToString("N")
);

// 2) После успешной оплаты отправляем closed
await client.OrderClosedAsync(new OrderClosedRequest
{
  ScanId = scan.ScanId,
  OrderId = "order-123",
  ClosedAt = DateTimeOffset.UtcNow,
  FinalOrderTotal = 14486.00m
});
```

## Типовой флоу для POS

1. Кассир сканирует QR или вводит OTP.
2. POS вызывает `ScanAsync(...)`.
3. POS применяет скидку в заказ.
4. Заказ оплачен:
  - вызвать `OrderClosedAsync(...)`.
5. Если применение отменено/заказ отменён:
  - вызвать `OrderCancelledAsync(...)` (с reason).

Рекомендуется всегда передавать:
- `OrderId`
- `TerminalId`
- `CashierId`
- `Idempotency-Key`
- `X-Trace-Id`

## Надежный режим (очередь + ретраи)

Для реальной эксплуатации лучше не блокировать кассу сетью.  
Используйте очереди SDK: запись локально + фоновая отправка.

### Вариант 1: JSON-файлы

```csharp
using RPlus.PartnerSdk.Http;
using RPlus.PartnerSdk.Models;
using RPlus.PartnerSdk.Reliability;

var client = new PartnerClient(new PartnerSdkOptions
{
  BaseUrl = "https://api.example.com",
  IntegrationKey = "pk_live_xxx",
  SigningSecret = "sk_live_xxx"
});

var commitStore = new FileCommitQueueStore(@"C:\posdata\rplus_commits.json");
var commitQueue = new ReliableCommitQueue("https://api.example.com", client, commitStore);
await commitQueue.InitializeAsync();
commitQueue.Start();

await commitQueue.EnqueueOrderClosedAsync(new OrderClosedRequest
{
  ScanId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
  OrderId = "order-123",
  ClosedAt = DateTimeOffset.UtcNow,
  FinalOrderTotal = 14486.00m
});

var eventStore = new FileEventQueueStore(@"C:\posdata\rplus_events.json");
var eventQueue = new ReliableEventQueue("https://api.example.com", client, eventStore);
await eventQueue.InitializeAsync();
eventQueue.Start();

await eventQueue.EnqueueAsync(new PartnerEvent
{
  Type = "scan_succeeded",
  OrderId = "order-123",
  TerminalId = "POS-01"
});
```

### Вариант 2: SQLite

```csharp
using RPlus.PartnerSdk.Http;
using RPlus.PartnerSdk.Reliability;
using RPlus.PartnerSdk.Sqlite;

var client = new PartnerClient(new PartnerSdkOptions
{
  BaseUrl = "https://api.example.com",
  IntegrationKey = "pk_live_xxx",
  SigningSecret = "sk_live_xxx"
});

var commitStore = new SqliteCommitQueueStore(@"C:\posdata\rplus_queue.db");
var commitQueue = new ReliableCommitQueue("https://api.example.com", client, commitStore);
await commitQueue.InitializeAsync();
commitQueue.Start();

var eventStore = new SqliteEventQueueStore(@"C:\posdata\rplus_queue.db");
var eventQueue = new ReliableEventQueue("https://api.example.com", client, eventStore);
await eventQueue.InitializeAsync();
eventQueue.Start();
```

## Обработка ошибок

Основное исключение SDK: `PartnerApiException`.

Что можно получить:
- `StatusCode` (HTTP-код)
- `ErrorCode` (код ошибки сервера, если есть)
- `ResponseBody` (сырой ответ сервера)

Пример:

```csharp
try
{
  await client.OrderClosedAsync(request);
}
catch (PartnerApiException ex)
{
  Console.WriteLine($"HTTP={(int)ex.StatusCode}, code={ex.ErrorCode}");
  Console.WriteLine(ex.ResponseBody);
}
```

## Логирование и безопасность

В `PartnerClient` можно передать `traceLogger`:

```csharp
var client = new PartnerClient(options, traceLogger: line => Console.WriteLine(line));
```

SDK маскирует чувствительные значения в логах:
- токены/подписи в headers
- `qrToken` и `otpCode` в body

## Базовые рекомендации по эксплуатации

- Для кассового ПО используйте очереди (`ReliableCommitQueue`, `ReliableEventQueue`), а не "чистые" прямые вызовы.
- Храните локальные файлы очередей в папке с правами на запись для POS-процесса.
- Передавайте стабильный `Idempotency-Key` для повторяемых операций.
- Всегда корректно закрывайте очередь при остановке приложения:
  - `await queue.StopAsync();`

## Структура репозитория

```text
sdk/
  src/
    RPlus.PartnerSdk/           // основной SDK (HTTP, models, reliability)
    RPlus.PartnerSdk.Sqlite/    // optional SQLite stores
  RPlus.PartnerSdk-dotnet.sln
  README.md
```

## Планы публикации

Репозиторий готов для публичного GitHub.  
При необходимости можно добавить:
- NuGet package publish pipeline
- GitHub Actions (build/test/pack)
- versioning policy (SemVer)

## Контракт API

Контракт серверных endpoint и бизнес-правила интеграции описаны в основном проекте:
- `project/docs/POS_INTEGRATION_SPEC_V1.md`
