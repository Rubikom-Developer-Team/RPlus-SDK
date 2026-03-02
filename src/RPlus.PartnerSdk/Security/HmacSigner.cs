using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RPlus.PartnerSdk.Security;

public static class HmacSigner
{
  // Matches the plugin/server spec:
  // payload = <timestamp><METHOD><normalized_path><raw_body_json>
  public static string ComputeSignatureHexLower(string secret, long unixSeconds, string method, string normalizedPath, string rawBodyJson)
  {
    if (secret == null) secret = string.Empty;
    if (method == null) method = string.Empty;
    if (normalizedPath == null) normalizedPath = string.Empty;
    if (rawBodyJson == null) rawBodyJson = string.Empty;

    string payload =
      unixSeconds.ToString(CultureInfo.InvariantCulture) +
      method.ToUpperInvariant() +
      normalizedPath +
      rawBodyJson;

    byte[] key = Encoding.UTF8.GetBytes(secret);
    byte[] msg = Encoding.UTF8.GetBytes(payload);
    using (HMACSHA256 h = new HMACSHA256(key))
    {
      byte[] hash = h.ComputeHash(msg);
      StringBuilder sb = new StringBuilder(hash.Length * 2);
      for (int i = 0; i < hash.Length; i++)
        sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
      return sb.ToString();
    }
  }

  // Normalize any gateway prefix before "/api/".
  public static string NormalizePathForSignature(string absolutePath)
  {
    string p = (absolutePath ?? string.Empty).Trim();
    int idx = p.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
    if (idx >= 0)
      p = p.Substring(idx);
    if (string.IsNullOrWhiteSpace(p))
      p = "/api/partners/orders/closed";
    return p;
  }
}

