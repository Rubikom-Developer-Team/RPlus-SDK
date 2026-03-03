using RPlus.PartnerSdk.Security;
using Xunit;

namespace RPlus.PartnerSdk.Tests;

public sealed class HmacSignerTests
{
  [Fact]
  public void ComputeSignatureHexLower_IsDeterministic()
  {
    string signature = HmacSigner.ComputeSignatureHexLower(
      secret: "secret",
      unixSeconds: 1700000000,
      method: "post",
      normalizedPath: "/api/partners/orders/closed",
      rawBodyJson: "{\"a\":1}");

    Assert.Equal("8bafc1d27e1b93b036d3e6b923bb2979e931833e7f21e6c5542e48bfff4b7d86", signature);
  }

  [Fact]
  public void NormalizePathForSignature_TrimsGatewayPrefix()
  {
    string path = HmacSigner.NormalizePathForSignature("/gw/v1/api/partners/orders/closed");

    Assert.Equal("/api/partners/orders/closed", path);
  }
}
