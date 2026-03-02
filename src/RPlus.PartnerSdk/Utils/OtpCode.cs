using System;
using System.Text;

namespace RPlus.PartnerSdk.Utils;

public static class OtpCode
{
  // Accepts "000000" or "000-000" and normalizes to digits-only "000000".
  public static bool TryNormalize(string? input, out string digits, out string error)
  {
    digits = string.Empty;
    error = string.Empty;

    string s = (input ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(s))
    {
      error = "otp_empty";
      return false;
    }

    StringBuilder sb = new StringBuilder(6);
    for (int i = 0; i < s.Length; i++)
    {
      char c = s[i];
      if (c >= '0' && c <= '9')
      {
        if (sb.Length < 6)
          sb.Append(c);
        else
        {
          error = "otp_too_long";
          return false;
        }
      }
      else if (c == '-' || c == ' ' || c == '\t')
      {
        continue;
      }
      else
      {
        error = "otp_invalid_char";
        return false;
      }
    }

    if (sb.Length != 6)
    {
      error = "otp_wrong_length";
      return false;
    }

    digits = sb.ToString();
    return true;
  }
}

