using Newtonsoft.Json;
using System;

namespace RPlus.PartnerSdk.Models;

public sealed class OrderClosedRequest
{
  public Guid ScanId { get; set; }
  public string OrderId { get; set; } = string.Empty;
  public DateTimeOffset ClosedAt { get; set; }

  public string? Terminal { get; set; }

  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public bool? RecoveredAfterClose { get; set; }

  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public string? RecoveredFromOrderId { get; set; }

  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public string? RecoveryReason { get; set; }

  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public string? PersonFullName { get; set; }

  public decimal FinalOrderTotal { get; set; }
  public decimal? FinalOrderTotalBeforeDiscounts { get; set; }
  public decimal? BaseAmountWithoutSurcharge { get; set; }
  public decimal? SurchargeAmount { get; set; }
  public decimal? AmountWithSurchargeBeforeDiscounts { get; set; }

  public decimal? FinalUserDiscount { get; set; }
  public decimal? FinalPartnerDiscount { get; set; }
  public decimal? TotalDiscountPercent { get; set; }

  public decimal? UserDiscountAmount { get; set; }
  public decimal? PartnerDiscountAmount { get; set; }
  public decimal? TotalDiscountAmount { get; set; }

  public string? QrDiscountTypeId { get; set; }
  public decimal? QrDiscountPercent { get; set; }
  public decimal? QrDiscountSum { get; set; }

  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public ChequeInfo? Cheque { get; set; }

  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public OrderItem[]? Items { get; set; }

  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public OrderPayment[]? Payments { get; set; }

  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public OrderDiscount[]? Discounts { get; set; }
}

public sealed class ChequeInfo
{
  public string? ChequeNumber { get; set; }
  public string? FiscalId { get; set; }
}

public sealed class OrderItem
{
  public string? Name { get; set; }
  public string? Sku { get; set; }
  public decimal Quantity { get; set; }
  public decimal UnitPrice { get; set; }
  public decimal TotalPrice { get; set; }
  public string? Category { get; set; }
}

public sealed class OrderPayment
{
  public string? Method { get; set; }
  public decimal Amount { get; set; }
}

public sealed class OrderDiscount
{
  public string DiscountTypeId { get; set; } = string.Empty;
  public string? DiscountTypeName { get; set; }
  public decimal? Sum { get; set; }
  public decimal? Percent { get; set; }
}
