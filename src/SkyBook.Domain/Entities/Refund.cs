using SkyBook.Domain.Enums;

namespace SkyBook.Domain.Entities;

public class Refund
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal Penalty { get; set; }
    public RefundStatus Status { get; set; } = RefundStatus.Pending;
    public DateTime? ProcessedAt { get; set; }
}
