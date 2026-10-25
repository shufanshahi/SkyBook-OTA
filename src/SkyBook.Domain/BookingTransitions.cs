using SkyBook.Domain.Enums;

namespace SkyBook.Domain;

public static class BookingTransitions
{
    private static readonly Dictionary<BookingStatus, BookingStatus[]> Allowed = new()
    {
        [BookingStatus.Held]      = [BookingStatus.Ticketed, BookingStatus.Cancelled, BookingStatus.Expired],
        [BookingStatus.Ticketed]  = [BookingStatus.Cancelled],
        [BookingStatus.Cancelled] = [BookingStatus.Refunded],
    };

    public static bool CanMove(BookingStatus from, BookingStatus to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);
}
