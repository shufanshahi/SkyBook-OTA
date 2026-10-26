using FluentAssertions;
using SkyBook.Application.Bookings;
using SkyBook.Application.Suppliers;

namespace SkyBook.UnitTests;

public class PassengerRulesTests
{
    private static readonly DateOnly Travel = new(2026, 11, 14);

    private static SupplierPassenger Adult(string? passport = null) => new("ADT", "Mr", "Karim", "Uddin", new DateOnly(1985, 1, 1), passport);
    private static SupplierPassenger Child() => new("CHD", "Ms", "Rina", "Uddin", new DateOnly(2018, 5, 1), null);
    private static SupplierPassenger Infant() => new("INF", "Ms", "Tia", "Uddin", new DateOnly(2026, 1, 1), null);

    [Fact]
    public void Valid_family_has_no_errors() =>
        PassengerRules.Validate([Adult(), Child(), Infant()], Travel, international: false).Should().BeEmpty();

    [Fact]
    public void Empty_list_is_rejected() =>
        PassengerRules.Validate([], Travel, false).Should().Contain("At least one passenger is required.");

    [Fact]
    public void More_than_nine_passengers_is_rejected() =>
        PassengerRules.Validate(Enumerable.Repeat(Adult(), 10).ToList(), Travel, false)
            .Should().Contain(e => e.Contains("at most 9"));

    [Fact]
    public void More_infants_than_adults_is_rejected() =>
        PassengerRules.Validate([Adult(), Infant(), Infant()], Travel, false)
            .Should().Contain("There cannot be more infants than adults.");

    [Fact]
    public void Booking_without_an_adult_is_rejected() =>
        PassengerRules.Validate([Child()], Travel, false).Should().Contain("At least one adult is required.");

    [Fact]
    public void International_travel_requires_a_passport_for_everyone() =>
        PassengerRules.Validate([Adult(), Child()], Travel, international: true)
            .Should().HaveCount(2).And.OnlyContain(e => e.Contains("passport"));

    [Fact]
    public void Passport_not_needed_domestically_and_accepted_internationally()
    {
        PassengerRules.Validate([Adult()], Travel, false).Should().BeEmpty();
        PassengerRules.Validate([Adult("A1234567")], Travel, true).Should().BeEmpty();
    }

    [Fact]
    public void Missing_names_are_reported() =>
        PassengerRules.Validate([Adult() with { FirstName = " " }], Travel, false)
            .Should().ContainSingle(e => e.Contains("first and last name"));

    [Theory]
    [InlineData("ADT", "2020-01-01")]   // adult aged 6
    [InlineData("CHD", "2010-01-01")]   // child aged 16
    [InlineData("CHD", "2026-06-01")]   // child aged 0
    [InlineData("INF", "2023-01-01")]   // infant aged 3
    public void Type_must_match_age_at_travel_date(string type, string dob)
    {
        var pax = new[] { Adult(), new SupplierPassenger(type, "Mx", "A", "B", DateOnly.Parse(dob), null) };
        PassengerRules.Validate(pax, Travel, false).Should().ContainSingle(e => e.StartsWith("Passenger 2"));
    }

    [Fact]
    public void Age_is_measured_on_the_travel_date_not_today()
    {
        // turns 12 the day before travel -> adult is fine; turns 12 the day after -> not
        PassengerRules.Validate([Adult() with { DateOfBirth = new DateOnly(2014, 11, 13) }], Travel, false).Should().BeEmpty();
        PassengerRules.Validate([Adult() with { DateOfBirth = new DateOnly(2014, 11, 15) }], Travel, false).Should().NotBeEmpty();
    }

    [Fact]
    public void Date_of_birth_after_travel_is_rejected() =>
        PassengerRules.Validate([Adult() with { DateOfBirth = Travel.AddDays(1) }], Travel, false)
            .Should().ContainSingle(e => e.Contains("before the travel date"));
}
