using FluentAssertions;
using SkyBook.Domain;
using SkyBook.Domain.Entities;
using SkyBook.Domain.Enums;

namespace SkyBook.UnitTests;

public class BookingTransitionsTests
{
    [Theory]
    [InlineData(BookingStatus.Held, BookingStatus.Ticketed)]
    [InlineData(BookingStatus.Held, BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Held, BookingStatus.Expired)]
    [InlineData(BookingStatus.Ticketed, BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Cancelled, BookingStatus.Refunded)]
    public void Valid_transitions_are_allowed(BookingStatus from, BookingStatus to) =>
        BookingTransitions.CanMove(from, to).Should().BeTrue();

    [Fact]
    public void Every_other_transition_is_rejected()
    {
        var valid = new HashSet<(BookingStatus, BookingStatus)>
        {
            (BookingStatus.Held, BookingStatus.Ticketed),
            (BookingStatus.Held, BookingStatus.Cancelled),
            (BookingStatus.Held, BookingStatus.Expired),
            (BookingStatus.Ticketed, BookingStatus.Cancelled),
            (BookingStatus.Cancelled, BookingStatus.Refunded),
        };
        var all = Enum.GetValues<BookingStatus>();

        foreach (var from in all)
            foreach (var to in all)
                if (!valid.Contains((from, to)))
                    BookingTransitions.CanMove(from, to).Should().BeFalse($"{from} -> {to} must be invalid");
    }

    [Fact]
    public void MoveTo_updates_status_on_valid_transition()
    {
        var b = new Booking { Pnr = "ABC123", Status = BookingStatus.Held };
        b.MoveTo(BookingStatus.Ticketed);
        b.Status.Should().Be(BookingStatus.Ticketed);
    }

    [Fact]
    public void MoveTo_throws_DomainException_on_invalid_transition()
    {
        var b = new Booking { Pnr = "ABC123", Status = BookingStatus.Cancelled };
        var act = () => b.MoveTo(BookingStatus.Ticketed);
        act.Should().Throw<DomainException>().WithMessage("*ABC123*Cancelled*Ticketed*");
        b.Status.Should().Be(BookingStatus.Cancelled);
    }
}
