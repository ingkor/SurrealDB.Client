namespace SurrealDB.Client.Sample.Api.Models;

public class User
{
    public string? Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "user";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuthRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class AuthResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}
