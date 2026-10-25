using SkyBook.Domain.Enums;

namespace SkyBook.Domain.Entities;

public class WalletTransaction
{
    public long Id { get; set; }
    public int AgentId { get; set; }
    public decimal Amount { get; set; }               // negative = debit
    public WalletTransactionType Type { get; set; }
    public string Reference { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
