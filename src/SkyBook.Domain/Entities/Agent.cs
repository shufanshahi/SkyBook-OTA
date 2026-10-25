namespace SkyBook.Domain.Entities;

public class Agent
{
    public int Id { get; set; }
    public string AgencyName { get; set; } = "";
    public string Code { get; set; } = "";
    public decimal WalletBalance { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal MarkupPercent { get; set; }
    public string Status { get; set; } = "Active";   // Active | Suspended
    public byte[] RowVersion { get; set; } = [];      // optimistic concurrency token
}
