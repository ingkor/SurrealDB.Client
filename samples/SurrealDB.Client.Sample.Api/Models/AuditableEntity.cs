namespace SurrealDB.Client.Sample.Api.Models;

using SurrealDB.Client.Security;

public class AuditableEntity
{
    public string? Id { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";

    [CreatedAt]
    public DateTime? CreatedAt { get; set; }

    [UpdatedAt]
    public DateTime? UpdatedAt { get; set; }

    [CreatedBy]
    public string? CreatedBy { get; set; }

    [UpdatedBy]
    public string? UpdatedBy { get; set; }
}

public class CreateAuditableEntityRequest
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? UserId { get; set; }
}
