namespace SurrealDB.Client.Security;

using System;

/// <summary>
/// Marks a property as containing the creation timestamp.
/// Automatically set when entity is created.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class CreatedAtAttribute : Attribute
{
}

/// <summary>
/// Marks a property as containing the last update timestamp.
/// Automatically updated when entity is modified.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class UpdatedAtAttribute : Attribute
{
}

/// <summary>
/// Marks a property as containing the creator's user ID.
/// Automatically set when entity is created.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class CreatedByAttribute : Attribute
{
}

/// <summary>
/// Marks a property as containing the last updater's user ID.
/// Automatically set when entity is modified.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class UpdatedByAttribute : Attribute
{
}

/// <summary>
/// Marks a property as sensitive (e.g., password, API key).
/// Should be encrypted at rest.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SensitiveAttribute : Attribute
{
    /// <summary>
    /// Gets the encryption algorithm to use.
    /// </summary>
    public string EncryptionAlgorithm { get; set; } = "AES-256";
}

/// <summary>
/// Marks a property as subject to row-level security (RLS).
/// Only accessible based on user permissions.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class RowLevelSecurityAttribute : Attribute
{
    /// <summary>
    /// Gets the security filter expression.
    /// </summary>
    public string? Filter { get; set; }
}

/// <summary>
/// Marks a property as personally identifiable information (PII).
/// Should be handled with care for compliance (GDPR, CCPA, etc.).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class PersonalDataAttribute : Attribute
{
}
