namespace SkyBook.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Agent";   // Admin | Agent
    public int? AgentId { get; set; }
    public Agent? Agent { get; set; }
    public bool IsActive { get; set; } = true;
}
