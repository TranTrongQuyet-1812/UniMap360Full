using System;

namespace UniMap360.Models;

public partial class PaymentOrder
{
    public int PaymentOrderId { get; set; }

    public long OrderCode { get; set; }

    public int AccountId { get; set; }

    public string PlanCode { get; set; } = null!;

    public long Amount { get; set; }

    public string Currency { get; set; } = "VND";

    public string Status { get; set; } = "Pending"; // Pending, Completed, Failed, Cancelled, Expired

    public string Provider { get; set; } = "PayOS";

    public string? ProviderPaymentLinkId { get; set; }

    public string? CheckoutUrl { get; set; }

    public string? QrCode { get; set; }

    public string? RawWebhookJson { get; set; }

    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public virtual Account Account { get; set; } = null!;
}
