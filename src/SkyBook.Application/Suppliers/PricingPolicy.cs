namespace SkyBook.Application.Suppliers;

public static class PricingPolicy
{
    /// <summary>Applies an agent's markup percentage. Taxes absorb the rounding so Base + Taxes == Total.</summary>
    public static FlightOffer ApplyMarkup(FlightOffer offer, decimal markupPercent)
    {
        if (markupPercent == 0) return offer;
        var factor = 1 + markupPercent / 100m;
        var total = Math.Round(offer.Total * factor, 2);
        var baseFare = Math.Round(offer.BaseFare * factor, 2);
        return offer with { BaseFare = baseFare, Taxes = total - baseFare, Total = total };
    }
}
