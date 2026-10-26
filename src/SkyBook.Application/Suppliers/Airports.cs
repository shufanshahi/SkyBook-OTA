namespace SkyBook.Application.Suppliers;

public static class Airports
{
    public static readonly IReadOnlyList<string> Domestic = ["DAC", "CGP", "CXB", "ZYL", "JSR", "SPD", "BZL"];
    public static readonly IReadOnlyList<string> International = ["CCU", "KUL", "BKK", "DXB", "SIN"];
    public static IEnumerable<string> All => Domestic.Concat(International);

    public static bool IsInternational(string origin, string destination) =>
        !Domestic.Contains(origin) || !Domestic.Contains(destination);
}
