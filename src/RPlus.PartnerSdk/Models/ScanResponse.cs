using System;

namespace RPlus.PartnerSdk.Models;

public sealed class ScanResponse
{
  public Guid ScanId { get; set; }

  public UserInfo? User { get; set; }

  // Percent values 0..100
  public decimal DiscountUser { get; set; }
  public decimal DiscountPartner { get; set; }
  public decimal DiscountTotal { get; set; }

  public string[]? Warnings { get; set; }

  // Raw server payload for audit/debug (optional).
  public string? RawJson { get; set; }
}

public sealed class UserInfo
{
  public string? FirstName { get; set; }
  public string? LastName { get; set; }
  public string? MiddleName { get; set; }
  public string? FullName { get; set; }
  public string? PositionTitle { get; set; }
  public string? AvatarUrl { get; set; }
}
