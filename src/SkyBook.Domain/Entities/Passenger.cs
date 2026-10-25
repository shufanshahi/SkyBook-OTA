using SkyBook.Domain.Enums;

namespace SkyBook.Domain.Entities;

public class Passenger
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public PassengerType Type { get; set; }
    public string Title { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public string? PassportNo { get; set; }
}
