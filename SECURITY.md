# Security: RLS, Encryption & Audit Trails

> Enterprise-grade security with row-level security, field encryption, audit trails, and compliance frameworks.

## Row-Level Security (RLS)

### Define RLS Policies

```csharp
[RLSPolicy("owner_id = $current_user_id")]
public class Document
{
    public string Id { get; set; }
    public string OwnerId { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
}

// Users can only see/modify their own documents
var myDocs = await session.Set<Document>().ToListAsync();
// Automatically filtered by RLS policy
```

### RLS with Roles

```csharp
[RLSPolicy(Role = "admin", Policy = "1=1")]  // Admins see all
[RLSPolicy(Role = "user", Policy = "owner_id = $current_user_id")]
[RLSPolicy(Role = "viewer", Policy = "is_public = true")]
public class Document
{
    public string Id { get; set; }
    public bool IsPublic { get; set; }
}
```

## Field-Level Encryption

### Encrypt Sensitive Fields

```csharp
public class User
{
    public string Id { get; set; }

    [Encrypted(Algorithm = EncryptionAlgorithm.AES256)]
    public string SocialSecurityNumber { get; set; }

    [Encrypted]
    public string CreditCardNumber { get; set; }

    [Masked]  // In logs: ***
    public string Email { get; set; }
}

// Auto-encrypted on save, decrypted on load
var user = new User { SocialSecurityNumber = "123-45-6789" };
await session.SaveChangesAsync();
// Stored encrypted in database
```

## Audit Trails

### Enable Auditing

```csharp
[Auditable]
public class Account
{
    public string Id { get; set; }
    public decimal Balance { get; set; }
    public string Status { get; set; }
}

// Changes automatically tracked
var account = await session.FindAsync<Account>("account:1");
account.Balance -= 100;
await session.SaveChangesAsync();
// Audit log created automatically
```

### Access Audit History

```csharp
var history = await client.GetAuditHistoryAsync<Account>("account:1");

foreach (var entry in history)
{
    Console.WriteLine($"Changed at: {entry.ChangedAt}");
    Console.WriteLine($"Changed by: {entry.UserId}");
    Console.WriteLine($"Field: {entry.Field}");
    Console.WriteLine($"Old: {entry.OldValue}");
    Console.WriteLine($"New: {entry.NewValue}");
}
```

## Data Masking

### Mask Sensitive Data

```csharp
public class Customer
{
    [Masked(Pattern = "***-**-****")]  // SSN mask
    public string SocialSecurityNumber { get; set; }

    [Masked(Pattern = "****-****-****-####")]  // Credit card mask
    public string CardNumber { get; set; }

    [Masked]  // Default: ***
    public string Password { get; set; }
}
```

## GDPR Compliance

### Right to be Forgotten

```csharp
// Delete all user data
await client.DeleteUserDataAsync("user:123");
// Deletes all records owned by user, logs deletion

// Export user data
var userData = await client.ExportUserDataAsync("user:123");
// Returns JSON/CSV of all user data
```

### Consent Management

```csharp
[ConsentRequired("marketing")]
public class Customer
{
    public string Email { get; set; }
}

// Check consent before sending marketing email
if (await client.HasConsentAsync("user:123", "marketing"))
{
    await SendMarketingEmail(customer.Email);
}
```

## API Key Management

```csharp
var apiKey = await client.GenerateApiKeyAsync(
    name: "Mobile App",
    expiresAt: DateTime.UtcNow.AddMonths(6),
    permissions: new[] { "read", "write" },
    rateLimit: 1000);  // Requests per minute

Console.WriteLine($"Key: {apiKey.Key}");
Console.WriteLine($"Secret: {apiKey.Secret}");

// Later: revoke
await client.RevokeApiKeyAsync(apiKey.Id);
```

## See full SECURITY.md in repository
