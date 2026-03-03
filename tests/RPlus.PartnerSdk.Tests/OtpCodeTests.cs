using RPlus.PartnerSdk.Utils;
using Xunit;

namespace RPlus.PartnerSdk.Tests;

public sealed class OtpCodeTests
{
  [Fact]
  public void TryNormalize_AcceptsDigitsAndSeparators()
  {
    bool ok = OtpCode.TryNormalize("12-34 56", out string digits, out string error);

    Assert.True(ok);
    Assert.Equal("123456", digits);
    Assert.Equal(string.Empty, error);
  }

  [Fact]
  public void TryNormalize_RejectsInvalidChars()
  {
    bool ok = OtpCode.TryNormalize("12AB56", out string digits, out string error);

    Assert.False(ok);
    Assert.Equal(string.Empty, digits);
    Assert.Equal("otp_invalid_char", error);
  }

  [Fact]
  public void TryNormalize_RejectsWrongLength()
  {
    bool ok = OtpCode.TryNormalize("12345", out _, out string error);

    Assert.False(ok);
    Assert.Equal("otp_wrong_length", error);
  }
}
