using SkyBook.Domain.Enums;

namespace SkyBook.Domain.Entities;

public class Booking
{
    public long Id { get; set; }
    public string Pnr { get; set; } = "";
    public int AgentId { get; set; }
    public Agent? Agent { get; set; }
    public string Supplier { get; set; } = "";        // SkyGDS | AeroNDC
    public string SupplierRef { get; set; } = "";
    public BookingStatus Status { get; set; } = BookingStatus.Held;
    public decimal TotalFare { get; set; }
    public string Currency { get; set; } = "BDT";
    public string OfferId { get; set; } = "";
    public DateTime? HoldExpiresAt { get; set; }
    public Guid IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<BookingSegment> Segments { get; set; } = [];
    public List<Passenger> Passengers { get; set; } = [];
    public List<Ticket> Tickets { get; set; } = [];

    public void MoveTo(BookingStatus next)
    {
        if (!BookingTransitions.CanMove(Status, next))
            throw new DomainException($"Cannot move booking {Pnr} from {Status} to {next}.");
        Status = next;
    }
}
