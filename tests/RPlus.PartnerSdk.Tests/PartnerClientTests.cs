using Newtonsoft.Json.Linq;
using RPlus.PartnerSdk.Http;
using RPlus.PartnerSdk.Models;
using RPlus.PartnerSdk.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RPlus.PartnerSdk.Tests;

public sealed class PartnerClientTests
{
  [Fact]
  public async Task ScanAsync_WithOtp_UsesShortcodeAndNormalizedOtp()
  {
    var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent("{\"scanId\":\"11111111-1111-1111-1111-111111111111\",\"discountUser\":0,\"discountPartner\":3.33,\"discountTotal\":3.33}", Encoding.UTF8, "application/json")
    });

    using var client = new PartnerClient(new PartnerSdkOptions
    {
      BaseUrl = "https://api.example.com",
      IntegrationKey = "ik_test",
      SigningSecret = "secret"
    }, handler);

    ScanResponse response = await client.ScanAsync(
      new ScanRequest { OtpCode = "12-34 56" },
      idempotencyKey: "idem-1",
      traceId: "trace-1");

    Assert.Equal(new Guid("11111111-1111-1111-1111-111111111111"), response.ScanId);
    Assert.NotNull(handler.LastRequest);
    Assert.Equal("/api/partners/shortcode", handler.LastRequest!.RequestUri!.AbsolutePath);
    Assert.Equal("ik_test", handler.LastRequest.Headers["X-Integration-Key"]);
    Assert.Equal("idem-1", handler.LastRequest.Headers["Idempotency-Key"]);
    Assert.Equal("trace-1", handler.LastRequest.Headers["X-Trace-Id"]);

    var body = JObject.Parse(handler.LastRequest.Body ?? "{}");
    Assert.Equal("123456", body.Value<string>("otpCode"));
    Assert.Null(body["qrToken"]);
  }

  [Fact]
  public async Task OrderClosedAsync_SetsSignatureHeaders()
  {
    var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent("{}", Encoding.UTF8, "application/json")
    });

    using var client = new PartnerClient(new PartnerSdkOptions
    {
      BaseUrl = "https://api.example.com",
      IntegrationKey = "ik_test",
      SigningSecret = "secret"
    }, handler);

    await client.OrderClosedAsync(new OrderClosedRequest
    {
      ScanId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
      OrderId = "order-1",
      ClosedAt = DateTimeOffset.UtcNow,
      FinalOrderTotal = 100m
    });

    Assert.NotNull(handler.LastRequest);
    Assert.Equal("/api/partners/orders/closed", handler.LastRequest!.RequestUri!.AbsolutePath);
    Assert.Equal("ik_test", handler.LastRequest.Headers["X-Integration-Key"]);
    Assert.True(handler.LastRequest.Headers.ContainsKey("X-Integration-Signature"));
    Assert.True(handler.LastRequest.Headers.ContainsKey("X-Integration-Timestamp"));

    string timestampRaw = handler.LastRequest.Headers["X-Integration-Timestamp"];
    string signature = handler.LastRequest.Headers["X-Integration-Signature"];
    long ts = long.Parse(timestampRaw);
    string expectedSignature = HmacSigner.ComputeSignatureHexLower(
      "secret",
      ts,
      "POST",
      HmacSigner.NormalizePathForSignature(handler.LastRequest.RequestUri.AbsolutePath),
      handler.LastRequest.Body ?? string.Empty);

    Assert.Equal(expectedSignature, signature);
  }

  [Fact]
  public async Task OrderCancelledAsync_DoesNotRequireSigningSecret()
  {
    var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent("{}", Encoding.UTF8, "application/json")
    });

    using var client = new PartnerClient(new PartnerSdkOptions
    {
      BaseUrl = "https://api.example.com",
      IntegrationKey = "ik_test",
      SigningSecret = null
    }, handler);

    await client.OrderCancelledAsync(new OrderCancelledRequest
    {
      ScanId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
      OrderId = "order-1",
      Reason = "operator_cancelled"
    });

    Assert.NotNull(handler.LastRequest);
    Assert.Equal("/api/partners/orders/cancelled", handler.LastRequest!.RequestUri!.AbsolutePath);
    Assert.False(handler.LastRequest.Headers.ContainsKey("X-Integration-Signature"));
    Assert.False(handler.LastRequest.Headers.ContainsKey("X-Integration-Timestamp"));
  }

  [Fact]
  public async Task OrderClosedAsync_ThrowsPartnerApiException_OnApiError()
  {
    var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
    {
      Content = new StringContent("{\"error\":\"validation_error\"}", Encoding.UTF8, "application/json")
    });

    using var client = new PartnerClient(new PartnerSdkOptions
    {
      BaseUrl = "https://api.example.com",
      IntegrationKey = "ik_test",
      SigningSecret = "secret"
    }, handler);

    var ex = await Assert.ThrowsAsync<PartnerApiException>(() => client.OrderClosedAsync(new OrderClosedRequest
    {
      ScanId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
      OrderId = "order-1",
      ClosedAt = DateTimeOffset.UtcNow,
      FinalOrderTotal = 100m
    }));

    Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    Assert.Equal("validation_error", ex.ErrorCode);
  }

  private sealed class CapturingHandler : HttpMessageHandler
  {
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public RequestSnapshot? LastRequest { get; private set; }

    public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
      _responder = responder ?? throw new ArgumentNullException(nameof(responder));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      LastRequest = await RequestSnapshot.FromAsync(request).ConfigureAwait(false);
      return _responder(request);
    }
  }

  private sealed class RequestSnapshot
  {
    public HttpMethod Method { get; private set; } = HttpMethod.Get;
    public Uri? RequestUri { get; private set; }
    public string? Body { get; private set; }
    public Dictionary<string, string> Headers { get; private set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static async Task<RequestSnapshot> FromAsync(HttpRequestMessage request)
    {
      var snapshot = new RequestSnapshot
      {
        Method = request.Method,
        RequestUri = request.RequestUri
      };

      foreach (var h in request.Headers)
      {
        snapshot.Headers[h.Key] = string.Join(", ", h.Value ?? Array.Empty<string>());
      }

      if (request.Content != null)
      {
        foreach (var h in request.Content.Headers)
        {
          snapshot.Headers[h.Key] = string.Join(", ", h.Value ?? Array.Empty<string>());
        }

        snapshot.Body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
      }

      return snapshot;
    }
  }
}
