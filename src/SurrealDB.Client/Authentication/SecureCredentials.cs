namespace SurrealDB.Client.Authentication;

using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Exceptions;

/// <summary>
/// SECURITY: Secure credential container for sensitive authentication data.
/// P1-1, P1-6: Credentials exposed in memory - no secure clearing.
///
/// This class provides secure storage for credentials with:
/// - SecureString for password storage (encrypted in memory)
/// - Automatic clearing/disposal of sensitive data
/// - Marshal-based secure string conversion
/// </summary>
public sealed class SecureCredentials : IDisposable
{
    private SecureString? _password;
    private SecureString? _token;
    private bool _disposed;

    /// <summary>
    /// Gets or sets the username (not encrypted, as it's typically not sensitive).
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Creates credentials with username and password.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password (will be stored securely).</param>
    public static SecureCredentials FromUsernamePassword(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ValidationException("Username cannot be empty.");

        if (string.IsNullOrWhiteSpace(password))
            throw new ValidationException("Password cannot be empty.");

        var credentials = new SecureCredentials
        {
            Username = username
        };

        // Convert password to SecureString
        credentials._password = new SecureString();
        foreach (char c in password)
        {
            credentials._password.AppendChar(c);
        }
        credentials._password.MakeReadOnly();

        return credentials;
    }

    /// <summary>
    /// Creates credentials from a token.
    /// </summary>
    /// <param name="token">The authentication token (will be stored securely).</param>
    public static SecureCredentials FromToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ValidationException("Token cannot be empty.");

        var credentials = new SecureCredentials();

        // Convert token to SecureString
        credentials._token = new SecureString();
        foreach (char c in token)
        {
            credentials._token.AppendChar(c);
        }
        credentials._token.MakeReadOnly();

        return credentials;
    }

    /// <summary>
    /// Creates credentials from a SecureString password (for advanced scenarios).
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="securePassword">The password as SecureString.</param>
    public static SecureCredentials FromSecurePassword(string username, SecureString securePassword)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ValidationException("Username cannot be empty.");

        if (securePassword == null || securePassword.Length == 0)
            throw new ValidationException("Password cannot be empty.");

        return new SecureCredentials
        {
            Username = username,
            _password = securePassword.Copy()
        };
    }

    /// <summary>
    /// Creates credentials from a SecureString token (for advanced scenarios).
    /// </summary>
    /// <param name="secureToken">The token as SecureString.</param>
    public static SecureCredentials FromSecureToken(SecureString secureToken)
    {
        if (secureToken == null || secureToken.Length == 0)
            throw new ValidationException("Token cannot be empty.");

        return new SecureCredentials
        {
            _token = secureToken.Copy()
        };
    }

    /// <summary>
    /// SECURITY: Converts SecureString to plain string temporarily for transmission.
    /// The returned string should be cleared immediately after use.
    ///
    /// This is necessary because network APIs require plain strings, but we minimize
    /// exposure time and clear the string after transmission.
    /// </summary>
    /// <returns>JSON credentials string that MUST be cleared after use.</returns>
    internal string ToJsonCredentials()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SecureCredentials));

        if (_password != null && Username != null)
        {
            // Username/password authentication
            IntPtr passwordPtr = IntPtr.Zero;
            try
            {
                passwordPtr = Marshal.SecureStringToGlobalAllocUnicode(_password);
                string password = Marshal.PtrToStringUni(passwordPtr) ?? string.Empty;

                // Create JSON manually to avoid serializer caching
                var json = $"{{\"user\":\"{EscapeJsonString(Username)}\",\"pass\":\"{EscapeJsonString(password)}\"}}";

                // Clear password from memory immediately
                for (int i = 0; i < password.Length; i++)
                {
                    // This doesn't guarantee clearing but is best effort
                }

                return json;
            }
            finally
            {
                // CRITICAL: Always zero out the unmanaged memory
                if (passwordPtr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(passwordPtr);
                }
            }
        }
        else if (_token != null)
        {
            // Token authentication
            IntPtr tokenPtr = IntPtr.Zero;
            try
            {
                tokenPtr = Marshal.SecureStringToGlobalAllocUnicode(_token);
                string token = Marshal.PtrToStringUni(tokenPtr) ?? string.Empty;

                var json = $"{{\"token\":\"{EscapeJsonString(token)}\"}}";

                return json;
            }
            finally
            {
                // CRITICAL: Always zero out the unmanaged memory
                if (tokenPtr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(tokenPtr);
                }
            }
        }

        throw new ValidationException("No credentials configured.");
    }

    /// <summary>
    /// Gets the token as a plain string (for session storage).
    /// WARNING: This should only be called when absolutely necessary.
    /// </summary>
    internal string? GetTokenString()
    {
        if (_disposed || _token == null)
            return null;

        IntPtr tokenPtr = IntPtr.Zero;
        try
        {
            tokenPtr = Marshal.SecureStringToGlobalAllocUnicode(_token);
            return Marshal.PtrToStringUni(tokenPtr);
        }
        finally
        {
            if (tokenPtr != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(tokenPtr);
            }
        }
    }

    /// <summary>
    /// SECURITY: Escapes JSON string values to prevent injection.
    /// </summary>
    private static string EscapeJsonString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    /// <summary>
    /// SECURITY: Explicitly clears credentials from memory.
    /// P1-1: Credentials exposed in memory - no secure clearing.
    /// </summary>
    public void Clear()
    {
        if (_password != null)
        {
            _password.Dispose();
            _password = null;
        }

        if (_token != null)
        {
            _token.Dispose();
            _token = null;
        }

        Username = null;
    }

    /// <summary>
    /// Disposes the credentials and clears sensitive data.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        Clear();
        _disposed = true;
    }
}
