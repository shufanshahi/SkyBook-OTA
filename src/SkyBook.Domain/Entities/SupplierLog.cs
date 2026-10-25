namespace SkyBook.Domain.Entities;

public class SupplierLog
{
    public long Id { get; set; }
    public string CorrelationId { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string Operation { get; set; } = "";
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public int HttpStatus { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
