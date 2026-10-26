using System.Text.RegularExpressions;

namespace SkyBook.Application.Suppliers;

/// <summary>Masks passport numbers and dates of birth before request/response bodies are stored in SupplierLogs.</summary>
public static partial class SensitiveDataMasker
{
    [GeneratedRegex(@"(<(?:\w+:)?(?:PassportNo|DateOfBirth)>)[^<]*(</)", RegexOptions.IgnoreCase)]
    private static partial Regex XmlFields();

    [GeneratedRegex("(\"(?:passportNo|dateOfBirth)\"\\s*:\\s*)\"[^\"]*\"", RegexOptions.IgnoreCase)]
    private static partial Regex JsonFields();

    public static string? Mask(string? body)
    {
        if (string.IsNullOrEmpty(body)) return body;
        body = XmlFields().Replace(body, "$1***$2");
        return JsonFields().Replace(body, "$1\"***\"");
    }
}
