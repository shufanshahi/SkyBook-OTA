namespace SkyBook.Domain.Entities;

public class Ticket
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public long PassengerId { get; set; }
    public string TicketNumber { get; set; } = "";
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
}
