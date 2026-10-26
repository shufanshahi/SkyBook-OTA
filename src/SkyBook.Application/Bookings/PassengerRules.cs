using SkyBook.Application.Suppliers;

namespace SkyBook.Application.Bookings;

/// <summary>Business rules for the traveller list. Plain static code so it is trivially unit-testable.</summary>
public static class PassengerRules
{
    public const int MaxPassengers = 9;

    public static IReadOnlyList<string> Validate(IReadOnlyList<SupplierPassenger> pax, DateOnly travelDate, bool international)
    {
        var errors = new List<string>();

        if (pax.Count == 0) errors.Add("At least one passenger is required.");
        if (pax.Count > MaxPassengers) errors.Add($"A booking can have at most {MaxPassengers} passengers.");

        var adults = pax.Count(p => p.Type == "ADT");
        var infants = pax.Count(p => p.Type == "INF");
        if (pax.Count > 0 && adults == 0) errors.Add("At least one adult is required.");
        if (infants > adults) errors.Add("There cannot be more infants than adults.");

        for (var i = 0; i < pax.Count; i++)
        {
            var p = pax[i];
            var who = $"Passenger {i + 1}";

            if (string.IsNullOrWhiteSpace(p.FirstName) || string.IsNullOrWhiteSpace(p.LastName))
                errors.Add($"{who}: first and last name are required.");

            if (p.Type is not ("ADT" or "CHD" or "INF"))
            {
                errors.Add($"{who}: unknown passenger type '{p.Type}'.");
                continue;
            }

            if (p.DateOfBirth >= travelDate)
            {
                errors.Add($"{who}: date of birth must be before the travel date.");
                continue;
            }

            var age = AgeOn(p.DateOfBirth, travelDate);
            switch (p.Type)
            {
                case "ADT" when age < 12: errors.Add($"{who}: an adult must be 12 or older on the travel date (is {age})."); break;
                case "CHD" when age is < 2 or > 11: errors.Add($"{who}: a child must be 2-11 on the travel date (is {age})."); break;
                case "INF" when age >= 2: errors.Add($"{who}: an infant must be under 2 on the travel date (is {age})."); break;
            }

            if (international && string.IsNullOrWhiteSpace(p.PassportNo))
                errors.Add($"{who}: a passport number is required for international travel.");
        }

        return errors;
    }

    public static int AgeOn(DateOnly dob, DateOnly date)
    {
        var age = date.Year - dob.Year;
        if (date < dob.AddYears(age)) age--;
        return age;
    }
}
