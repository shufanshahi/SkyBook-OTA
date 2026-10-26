namespace SkyBook.Application.Suppliers;

/// <summary>Ambient correlation id for the current async flow. Set by middleware, read by logging and the supplier handler.</summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? Id
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}
