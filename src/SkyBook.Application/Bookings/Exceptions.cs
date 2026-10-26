namespace SkyBook.Application.Bookings;

/// <summary>The supplier price moved since the agent last saw it; they must confirm the new total.</summary>
public class PriceChangedException(decimal newTotal)
    : Exception($"The price changed to {newTotal:N0}. Please review and confirm.")
{
    public decimal NewTotal { get; } = newTotal;
}

/// <summary>Booking rules were broken by the submitted data (e.g. passenger rules). Carries every message.</summary>
public class ValidationFailedException(IReadOnlyList<string> errors) : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
