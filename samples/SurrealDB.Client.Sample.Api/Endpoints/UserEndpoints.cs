namespace SurrealDB.Client.Sample.Api.Endpoints;

using Microsoft.AspNetCore.Mvc;
using Models;

/// <summary>
/// Demonstrates: AuthenticateAsync, LogoutAsync, CreateAsync for user records
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users — Authentication");

        // Feature: AuthenticateAsync(username, password)
        group.MapPost("/authenticate", async ([FromBody] AuthRequest req, ISurrealDbClient client, CancellationToken ct) =>
        {
            try
            {
                // Credentials are passed separately — never embedded in the connection string
                // (enforced by ValidateConnectionString in SurrealDbClientOptions)
                await client.AuthenticateAsync(req.Username, req.Password, ct);

                return Results.Ok(new AuthResponse
                {
                    Success = true,
                    Message = $"Authenticated as '{req.Username}'"
                });
            }
            catch (Exception ex)
            {
                return Results.Unauthorized();
            }
        })
        .WithName("Authenticate")
        .WithSummary("AuthenticateAsync(username, password)")
        .WithDescription(
            "Authenticates with SurrealDB using `client.AuthenticateAsync(username, password)`. " +
            "Credentials are always passed separately — the connection string validation " +
            "(P0-2 security fix) rejects any connection string containing `user:pass@host`.");

        // Feature: AuthenticateAsync(token)
        group.MapPost("/authenticate/token", async ([FromBody] string token, ISurrealDbClient client, CancellationToken ct) =>
        {
            try
            {
                await client.AuthenticateAsync(token, ct);
                return Results.Ok(new AuthResponse { Success = true, Message = "Token accepted" });
            }
            catch
            {
                return Results.Unauthorized();
            }
        })
        .WithName("AuthenticateToken")
        .WithSummary("AuthenticateAsync(token)")
        .WithDescription("Authenticates using a pre-issued JWT token via `client.AuthenticateAsync(token)`.");

        // Feature: LogoutAsync
        group.MapPost("/logout", async (ISurrealDbClient client, CancellationToken ct) =>
        {
            await client.LogoutAsync(ct);
            return Results.Ok(new AuthResponse { Success = true, Message = "Logged out — session cleared" });
        })
        .WithName("Logout")
        .WithSummary("LogoutAsync()")
        .WithDescription("Clears the authentication session via `client.LogoutAsync()`. The client remains connected but unauthenticated.");

        // Feature: CreateAsync used for a User record
        group.MapPost("/", async ([FromBody] User user, ISurrealDbClient client, CancellationToken ct) =>
        {
            user.CreatedAt = DateTime.UtcNow;
            var created = await client.CreateAsync("users", user, ct);
            return Results.Created($"/api/users/{created?.Id}", created);
        })
        .WithName("CreateUser")
        .WithSummary("CreateAsync<User>")
        .WithDescription("Creates a user record in the `users` table.");

        // Feature: SelectAsync — list all users
        group.MapGet("/", async (ISurrealDbClient client, CancellationToken ct) =>
        {
            var users = await client.SelectAsync<User>("users", cancellationToken: ct);
            return Results.Ok(users);
        })
        .WithName("GetAllUsers")
        .WithSummary("SelectAsync<User>")
        .WithDescription("Lists all users with `client.SelectAsync<User>(\"users\")`.");
    }
}
