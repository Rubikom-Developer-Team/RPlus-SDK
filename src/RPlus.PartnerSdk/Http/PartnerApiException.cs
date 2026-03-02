using System;
using System.Net;

namespace RPlus.PartnerSdk.Http;

public sealed class PartnerApiException : Exception
{
  public PartnerApiException(HttpStatusCode statusCode, string message, string? errorCode, string? responseBody)
    : base(message)
  {
    StatusCode = statusCode;
    ErrorCode = errorCode;
    ResponseBody = responseBody;
  }

  public HttpStatusCode StatusCode { get; }
  public string? ErrorCode { get; }
  public string? ResponseBody { get; }
}

