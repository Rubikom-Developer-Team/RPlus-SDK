using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RPlus.PartnerSdk.Models;
using RPlus.PartnerSdk.Security;
using RPlus.PartnerSdk.Utils;
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RPlus.PartnerSdk.Http;

public sealed class PartnerClient : IDisposable
{
  private readonly PartnerSdkOptions _options;
  private readonly HttpClient _http;
  private readonly Action<string>? _traceLogger;

  public PartnerClient(PartnerSdkOptions options, HttpMessageHandler? handler = null, Action<string>? traceLogger = null)
  {
    _options = options ?? throw new ArgumentNullException(nameof(options));
    _traceLogger = traceLogger;
    if (string.IsNullOrWhiteSpace(_options.BaseUrl))
      throw new ArgumentException("BaseUrl is required.", nameof(options));
    if (string.IsNullOrWhiteSpace(_options.IntegrationKey))
      throw new ArgumentException("IntegrationKey is required.", nameof(options));

    _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
    _http.Timeout = _options.Timeout;

    if (!string.IsNullOrWhiteSpace(_options.UserAgent))
      _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
  }

  public void Dispose()
  {
    _http.Dispose();
  }

  public async Task<ScanResponse> ScanAsync(ScanRequest request, string? idempotencyKey = null, string? traceId = null, CancellationToken ct = default)
  {
    if (request == null) throw new ArgumentNullException(nameof(request));

    // Normalize OTP if it looks like OTP.
    string? otpDigits = null;
    if (!string.IsNullOrWhiteSpace(request.OtpCode) && OtpCode.TryNormalize(request.OtpCode, out string d, out _))
      otpDigits = d;

    var body = new JObject();
    if (!string.IsNullOrWhiteSpace(otpDigits))
    {
      body["otpCode"] = otpDigits;
    }
    else
    {
      if (string.IsNullOrWhiteSpace(request.QrToken))
        throw new ArgumentException("Either QrToken or OtpCode must be provided.", nameof(request));
      body["qrToken"] = request.QrToken;
    }

    if (!string.IsNullOrWhiteSpace(request.OrderId)) body["orderId"] = request.OrderId;
    if (request.OrderSum.HasValue) body["orderSum"] = request.OrderSum.Value;
    if (!string.IsNullOrWhiteSpace(request.TerminalId)) body["terminalId"] = request.TerminalId;
    if (!string.IsNullOrWhiteSpace(request.CashierId)) body["cashierId"] = request.CashierId;
    if (!string.IsNullOrWhiteSpace(request.Context)) body["context"] = request.Context;

      string raw = body.ToString(Formatting.None);
      string scanEndpoint = !string.IsNullOrWhiteSpace(otpDigits)
        ? Combine(_options.BaseUrl, "/api/partners/shortcode")
        : Combine(_options.BaseUrl, "/api/partners/scan");

      using (var msg = new HttpRequestMessage(HttpMethod.Post, scanEndpoint))
      {
        ApplyCommonHeaders(msg, idempotencyKey, traceId);
        msg.Content = new StringContent(raw, Encoding.UTF8, "application/json");

        LogHttpRequest(msg);

        using (HttpResponseMessage resp = await _http.SendAsync(msg, ct).ConfigureAwait(false))
        {
          string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
          LogHttpResponse(resp, scanEndpoint, text);
          if (!resp.IsSuccessStatusCode)
            throw BuildApiException(resp.StatusCode, text);

        var j = JObject.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        ScanResponse sr = new ScanResponse();
        sr.RawJson = text;

        sr.ScanId = TryReadGuid(j, "scanId", "scan_id", "id");
        sr.Warnings = TryReadStringArray(j, "warnings");

        // discounts: preferred nested shape
        if (j.TryGetValue("discounts", out JToken? discountsTok) && discountsTok is JObject discountsObj)
        {
          // Legacy keys:
          // - rplusAmount / partnerAmount (older contract)
          // - rplus / partner (new contract)
          // - discountUser / discountPartner (some integrations still use flat style inside nested too)
          sr.DiscountUser = ReadDecimalAny(
            discountsObj,
            "rplusAmount",
            "rplus",
            "discountUser");
          sr.DiscountPartner = ReadDecimalAny(
            discountsObj,
            "partnerAmount",
            "partner",
            "discountPartner");
          sr.DiscountTotal = ReadDecimalAny(
            discountsObj,
            "total",
            "discountTotal");
        }
        else
        {
          sr.DiscountUser = ReadDecimal(j, "discountUser");
          sr.DiscountPartner = ReadDecimal(j, "discountPartner");
          sr.DiscountTotal = ReadDecimal(j, "discountTotal");
          if (sr.DiscountUser == 0m && sr.DiscountPartner == 0m)
          {
            sr.DiscountUser = ReadDecimal(j, "rplus");
            sr.DiscountPartner = ReadDecimal(j, "partner");
          }
          if (sr.DiscountTotal == 0m)
            sr.DiscountTotal = ReadDecimal(j, "total");
        }

        if (j.TryGetValue("user", out JToken? userTok) && userTok is JObject userObj)
        {
          sr.User = new UserInfo
          {
            FirstName = userObj.Value<string>("firstName"),
            LastName = userObj.Value<string>("lastName"),
            MiddleName = userObj.Value<string>("middleName"),
            FullName = userObj.Value<string>("fullName"),
            PositionTitle = userObj.Value<string>("positionTitle"),
            AvatarUrl = userObj.Value<string>("avatarUrl"),
          };
        }

        return sr;
      }
    }
  }

  public async Task OrderClosedAsync(OrderClosedRequest request, string? idempotencyKey = null, string? traceId = null, CancellationToken ct = default)
  {
    if (request == null) throw new ArgumentNullException(nameof(request));
    if (request.ScanId == Guid.Empty) throw new ArgumentException("ScanId is required.", nameof(request));
    if (string.IsNullOrWhiteSpace(request.OrderId)) throw new ArgumentException("OrderId is required.", nameof(request));

    await PostJsonAsync("/api/partners/orders/closed", request, idempotencyKey ?? request.ScanId.ToString(), traceId, requireSignature: true, ct: ct).ConfigureAwait(false);
  }

  public async Task OrderCancelledAsync(OrderCancelledRequest request, string? idempotencyKey = null, string? traceId = null, CancellationToken ct = default)
  {
    if (request == null) throw new ArgumentNullException(nameof(request));
    if (request.ScanId == Guid.Empty) throw new ArgumentException("ScanId is required.", nameof(request));
    if (string.IsNullOrWhiteSpace(request.OrderId)) throw new ArgumentException("OrderId is required.", nameof(request));

    await PostJsonAsync("/api/partners/orders/cancelled", request, idempotencyKey ?? request.ScanId.ToString(), traceId, requireSignature: false, ct: ct).ConfigureAwait(false);
  }

  public async Task PostEventAsync(PartnerEvent evt, string? idempotencyKey = null, string? traceId = null, CancellationToken ct = default)
  {
    if (evt == null) throw new ArgumentNullException(nameof(evt));
    if (evt.EventId == Guid.Empty) throw new ArgumentException("EventId is required.", nameof(evt));
    if (string.IsNullOrWhiteSpace(evt.Type)) throw new ArgumentException("Type is required.", nameof(evt));

    await PostJsonAsync("/api/partners/events", evt, idempotencyKey ?? evt.EventId.ToString(), traceId, requireSignature: false, ct: ct).ConfigureAwait(false);
  }

  public async Task<ReconcileResponse> ReconcileAsync(ReconcileRequest request, string? idempotencyKey = null, string? traceId = null, CancellationToken ct = default)
  {
    if (request == null) throw new ArgumentNullException(nameof(request));

    // Server contract is intentionally flexible. We send the minimum and parse best-effort.
    string url = Combine(_options.BaseUrl, "/api/partners/reconcile");
    string rawJson = JsonUtil.Serialize(request);

    ReconcileResponse rr = new ReconcileResponse();

    using (var msg = new HttpRequestMessage(HttpMethod.Post, url))
    {
      ApplyCommonHeaders(msg, idempotencyKey, traceId);

      string secret = (_options.SigningSecret ?? string.Empty).Trim();
      if (string.IsNullOrWhiteSpace(secret))
        throw new InvalidOperationException("SigningSecret is required for this request.");

      var u = new Uri(url);
      string normalizedPath = HmacSigner.NormalizePathForSignature(u.AbsolutePath);
      long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      string sig = HmacSigner.ComputeSignatureHexLower(secret, ts, "POST", normalizedPath, rawJson);
      msg.Headers.Add("X-Integration-Signature", sig);
      msg.Headers.Add("X-Integration-Timestamp", ts.ToString(CultureInfo.InvariantCulture));

      msg.Content = new StringContent(rawJson, Encoding.UTF8, "application/json");

      LogHttpRequest(msg);

      using (HttpResponseMessage resp = await _http.SendAsync(msg, ct).ConfigureAwait(false))
      {
        string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        LogHttpResponse(resp, url, text);
        if (!resp.IsSuccessStatusCode)
          throw BuildApiException(resp.StatusCode, text);

        rr.RawJson = text;
        try
        {
          var j = JObject.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
          if (j.TryGetValue("items", out JToken? itemsTok) && itemsTok is JArray arr)
          {
            var list = new System.Collections.Generic.List<ReconcileResultItem>();
            foreach (var it in arr)
            {
              if (it is not JObject o) continue;
              Guid scanId = TryReadGuid(o, "scanId", "scan_id", "id");
              list.Add(new ReconcileResultItem
              {
                ScanId = scanId,
                Status = o.Value<string>("status"),
                Error = o.Value<string>("error"),
              });
            }
            rr.Items = list.Count == 0 ? null : list.ToArray();
          }
        }
        catch
        {
          // ignore parsing errors; 200 OK is enough for operational reconcile
        }

        return rr;
      }
    }
  }

  public Task PostJsonAsync(string path, object bodyObj, string? idempotencyKey, string? traceId, bool requireSignature, CancellationToken ct)
  {
    string url = Combine(_options.BaseUrl, path);
    string rawJson = JsonUtil.Serialize(bodyObj);
    return PostRawAsync(url, rawJson, idempotencyKey, traceId, requireSignature, ct);
  }

  // Advanced usage: stable signing across retries by persisting raw JSON.
  public Task PostRawAsync(string url, string rawJson, string? idempotencyKey, string? traceId, bool requireSignature, CancellationToken ct)
  {
    return PostRawInternalAsync(url, rawJson, idempotencyKey, traceId, requireSignature, extraHeaders: null, ct: ct);
  }

  // Advanced usage: add extra headers (e.g., X-Dry-Run) without forking SDK logic.
  public Task PostRawAsync(string url, string rawJson, string? idempotencyKey, string? traceId, bool requireSignature, System.Collections.Generic.IDictionary<string, string>? extraHeaders, CancellationToken ct)
  {
    return PostRawInternalAsync(url, rawJson, idempotencyKey, traceId, requireSignature, extraHeaders, ct);
  }

  private async Task PostRawInternalAsync(string url, string rawJson, string? idempotencyKey, string? traceId, bool requireSignature, System.Collections.Generic.IDictionary<string, string>? extraHeaders, CancellationToken ct)
  {
    LogCustomLine($"HTTP REQUEST START: POST {url}");

    using (var msg = new HttpRequestMessage(HttpMethod.Post, url))
    {
      ApplyCommonHeaders(msg, idempotencyKey, traceId);
      if (extraHeaders != null)
      {
        foreach (var kv in extraHeaders)
        {
          if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null)
            msg.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }
      }

      // Sign when possible; for cancelled it's recommended but not strictly required.
      string secret = (_options.SigningSecret ?? string.Empty).Trim();
      if (!string.IsNullOrWhiteSpace(secret))
      {
        var u = new Uri(url);
        string normalizedPath = HmacSigner.NormalizePathForSignature(u.AbsolutePath);
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string sig = HmacSigner.ComputeSignatureHexLower(secret, ts, "POST", normalizedPath, rawJson);
        msg.Headers.Add("X-Integration-Signature", sig);
        msg.Headers.Add("X-Integration-Timestamp", ts.ToString(CultureInfo.InvariantCulture));
      }
      else if (requireSignature)
      {
        throw new InvalidOperationException("SigningSecret is required for this request.");
      }

      msg.Content = new StringContent(rawJson, Encoding.UTF8, "application/json");

      LogHttpRequest(msg);

      using (HttpResponseMessage resp = await _http.SendAsync(msg, ct).ConfigureAwait(false))
      {
        string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        LogHttpResponse(resp, url, text);
        if (!resp.IsSuccessStatusCode)
          throw BuildApiException(resp.StatusCode, text);
      }
    }
  }

  private void ApplyCommonHeaders(HttpRequestMessage msg, string? idempotencyKey, string? traceId)
  {
    msg.Headers.TryAddWithoutValidation("X-Integration-Key", _options.IntegrationKey);
    if (!string.IsNullOrWhiteSpace(idempotencyKey))
      msg.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
    if (!string.IsNullOrWhiteSpace(traceId))
      msg.Headers.TryAddWithoutValidation("X-Trace-Id", traceId);
  }

  private static PartnerApiException BuildApiException(HttpStatusCode statusCode, string responseBody)
  {
    string? errorCode = null;
    try
    {
      if (!string.IsNullOrWhiteSpace(responseBody))
      {
        var j = JObject.Parse(responseBody);
        errorCode = j.Value<string>("error");
      }
    }
    catch
    {
    }

    string msg = string.IsNullOrWhiteSpace(errorCode)
      ? $"HTTP {(int)statusCode}"
      : $"HTTP {(int)statusCode}: {errorCode}";

    return new PartnerApiException(statusCode, msg, errorCode, responseBody);
  }

  private static Guid TryReadGuid(JObject j, params string[] keys)
  {
    try
    {
      if (j == null || keys == null || keys.Length == 0)
        return Guid.Empty;

      foreach (string key in keys)
      {
        if (string.IsNullOrWhiteSpace(key))
          continue;
        string? s = j.Value<string>(key);
        if (!string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out Guid g))
          return g;

        try
        {
          JToken? nested = j.SelectToken(key);
          if (nested != null && nested.Type == JTokenType.String)
          {
            string? ns = nested.Value<string>();
            if (!string.IsNullOrWhiteSpace(ns) && Guid.TryParse(ns, out Guid ng))
              return ng;
          }
        }
        catch
        {
        }
      }
    }
    catch
    {
    }
    return Guid.Empty;
  }

  private static string[]? TryReadStringArray(JObject j, string key)
  {
    try
    {
      if (!j.TryGetValue(key, out JToken? tok) || tok == null || tok.Type == JTokenType.Null)
        return null;
      if (tok is JArray arr)
      {
        var list = new System.Collections.Generic.List<string>();
        foreach (var it in arr)
        {
          if (it == null) continue;
          string? s = it.Type == JTokenType.String ? it.Value<string>() : it.ToString();
          if (!string.IsNullOrWhiteSpace(s))
            list.Add(s!);
        }
        return list.Count == 0 ? null : list.ToArray();
      }
    }
    catch
    {
    }
    return null;
  }

  private static decimal ReadDecimalAny(JObject j, params string[] keys)
  {
    if (j == null || keys == null || keys.Length == 0)
      return 0m;

    foreach (string key in keys)
    {
      decimal v = ReadDecimal(j, key);
      if (v != 0m)
        return v;
    }
    return 0m;
  }

  private static decimal ReadDecimal(JObject j, string key)
  {
    try
    {
      JToken? tok = j.GetValue(key, StringComparison.OrdinalIgnoreCase);
      if (tok == null || tok.Type == JTokenType.Null)
        return 0m;
      if (tok.Type == JTokenType.Float || tok.Type == JTokenType.Integer)
        return tok.Value<decimal>();
      if (tok.Type == JTokenType.String)
      {
        string s = tok.Value<string>() ?? string.Empty;
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d))
          return d;
      }
    }
    catch
    {
    }
    return 0m;
  }

  private static string Combine(string baseUrl, string path)
  {
    if (string.IsNullOrWhiteSpace(baseUrl)) return path;
    if (string.IsNullOrWhiteSpace(path)) return baseUrl;
    return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
  }

  private void LogCustomLine(string text)
  {
    _traceLogger?.Invoke(text);
  }

  private void LogHttpRequest(HttpRequestMessage msg)
  {
    if (_traceLogger == null || msg == null)
      return;

    StringBuilder sb = new StringBuilder();
    sb.AppendLine($"[SDK] {msg.Method} {msg.RequestUri}");

    sb.AppendLine("Headers:");
    AppendHeaders(sb, msg.Headers);
    if (msg.Content != null)
    {
      sb.AppendLine("Content-Headers:");
      AppendHeaders(sb, msg.Content.Headers);
    }

    if (msg.Content != null)
    {
      string content = msg.Content.ReadAsStringAsync().GetAwaiter().GetResult();
      sb.AppendLine("Body:");
      sb.AppendLine(MaskSensitiveJson(content));
    }

    LogCustomLine(sb.ToString().TrimEnd());
  }

  private void LogHttpResponse(HttpResponseMessage resp, string? requestUrl = null, string? body = null)
  {
    if (_traceLogger == null || resp == null)
      return;

    StringBuilder sb = new StringBuilder();
    sb.AppendLine($"[SDK] response for {requestUrl ?? string.Empty} => {(int) resp.StatusCode} {resp.ReasonPhrase}");
    sb.AppendLine("Headers:");
    AppendHeaders(sb, resp.Headers);
    if (resp.Content != null)
    {
      sb.AppendLine("Content-Headers:");
      AppendHeaders(sb, resp.Content.Headers);
    }

    try
    {
      string bodyText = body ?? string.Empty;
      sb.AppendLine("Body:");
      sb.AppendLine(MaskSensitiveJson(bodyText));
    }
    catch (Exception ex)
    {
      sb.AppendLine("Body: <read failed>: " + ex.Message);
    }

    LogCustomLine(sb.ToString().TrimEnd());
  }

  private static void AppendHeaders(StringBuilder sb, HttpHeaders headers)
  {
    try
    {
      foreach (var h in headers)
      {
        string value = string.Join(", ", h.Value);
        sb.AppendLine($"  {h.Key}: {MaskHeaderValue(h.Key, value)}");
      }
      if (sb.Length == 0)
        sb.AppendLine("  <no headers>");
    }
    catch
    {
      sb.AppendLine("  <headers unavailable>");
    }
  }

  private static string MaskHeaderValue(string key, string value)
  {
    if (string.IsNullOrWhiteSpace(key))
      return value ?? string.Empty;

    string k = key.Trim();
    string v = value ?? string.Empty;

    // Never log secrets in full.
    if (k.Equals("X-Integration-Key", StringComparison.OrdinalIgnoreCase))
      return MaskToken(v);
    if (k.Equals("X-Integration-Signature", StringComparison.OrdinalIgnoreCase))
      return MaskToken(v);
    if (k.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
      return "<redacted>";

    return v;
  }

  private static string MaskToken(string token)
  {
    token = (token ?? string.Empty).Trim();
    if (token.Length == 0)
      return token;
    if (token.Length <= 8)
      return new string('*', token.Length);
    return token.Substring(0, 4) + "..." + token.Substring(token.Length - 4, 4);
  }

  private static string MaskSensitiveJson(string body)
  {
    if (string.IsNullOrWhiteSpace(body))
      return body ?? string.Empty;

    // Best-effort: if it's JSON, redact OTP/QR token fields.
    try
    {
      JToken tok = JToken.Parse(body);
      if (tok is JObject obj)
      {
        Redact(obj, "qrToken");
        Redact(obj, "qr_token");
        Redact(obj, "otpCode");
        Redact(obj, "otp_code");
        return obj.ToString(Formatting.None);
      }
    }
    catch
    {
      // Not JSON, leave as-is.
    }

    return body;
  }

  private static void Redact(JObject obj, string key)
  {
    try
    {
      if (obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out JToken? v) && v != null && v.Type == JTokenType.String)
      {
        string s = v.Value<string>() ?? string.Empty;
        obj[key] = MaskToken(s);
      }
    }
    catch
    {
    }
  }
}
