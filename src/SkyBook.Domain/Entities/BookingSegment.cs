namespace SkyBook.Domain.Entities;

public class BookingSegment
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public string Carrier { get; set; } = "";
    public string FlightNo { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
    public DateTime DepartureUtc { get; set; }
    public DateTime ArrivalUtc { get; set; }
    public string CabinClass { get; set; } = "Economy";
}
