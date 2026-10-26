using FluentAssertions;
using SkyBook.Application.Suppliers;

namespace SkyBook.UnitTests.Suppliers;

public class PricingAndMaskingTests
{
    [Fact]
    public void Markup_keeps_base_plus_taxes_equal_to_total()
    {
        var o = PricingPolicy.ApplyMarkup(TestData.Offer(3333.33m), 3.5m);
        (o.BaseFare + o.Taxes).Should().Be(o.Total);
        o.Total.Should().Be(3450.00m);   // 3333.33 * 1.035 = 3449.9966 -> rounds up
    }

    [Fact]
    public void Zero_markup_returns_the_same_offer()
    {
        var o = TestData.Offer();
        PricingPolicy.ApplyMarkup(o, 0m).Should().BeSameAs(o);
    }

    [Fact]
    public void Masks_passport_and_dob_in_xml()
    {
        var xml = "<gds:Passenger><gds:FirstName>Ayesha</gds:FirstName><gds:DateOfBirth>1990-04-02</gds:DateOfBirth><gds:PassportNo>A1234567</gds:PassportNo></gds:Passenger>";
        var masked = SensitiveDataMasker.Mask(xml)!;
        masked.Should().Contain("<gds:DateOfBirth>***</gds:DateOfBirth>").And.Contain("<gds:PassportNo>***</gds:PassportNo>");
        masked.Should().NotContain("A1234567").And.NotContain("1990-04-02").And.Contain("Ayesha");
    }

    [Fact]
    public void Masks_passport_and_dob_in_json()
    {
        var json = """{"firstName":"Ayesha","dateOfBirth":"1990-04-02","passportNo": "A1234567"}""";
        var masked = SensitiveDataMasker.Mask(json)!;
        masked.Should().NotContain("A1234567").And.NotContain("1990-04-02").And.Contain("Ayesha");
        masked.Should().Contain("\"passportNo\": \"***\"");
    }

    [Fact]
    public void Mask_handles_null_and_empty()
    {
        SensitiveDataMasker.Mask(null).Should().BeNull();
        SensitiveDataMasker.Mask("").Should().BeEmpty();
    }
}
