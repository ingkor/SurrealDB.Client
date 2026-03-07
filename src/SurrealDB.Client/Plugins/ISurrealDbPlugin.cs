namespace SurrealDB.Client.Plugins;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Session;

/// <summary>
/// Base interface for SurrealDB plugins.
/// Plugins extend framework functionality and lifecycle hooks.
/// </summary>
public interface ISurrealDbPlugin
{
    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the plugin version.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Called when the plugin is initialized.
    /// </summary>
    Task OnInitializeAsync(SurrealDbClient client, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a session is created.
    /// </summary>
    Task OnSessionCreatedAsync(ISurrealDbSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called before a session is disposed.
    /// </summary>
    Task OnSessionDisposingAsync(ISurrealDbSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the plugin configuration.
    /// </summary>
    object? GetConfiguration();

    /// <summary>
    /// Sets the plugin configuration.
    /// </summary>
    void SetConfiguration(object? configuration);
}

/// <summary>
/// Plugin manager for registering and executing plugins.
/// </summary>
public class PluginManager
{
    private readonly List<ISurrealDbPlugin> _plugins = new();
    private readonly object _lock = new();

    /// <summary>
    /// Registers a plugin.
    /// </summary>
    public void Register(ISurrealDbPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        lock (_lock)
        {
            _plugins.Add(plugin);
        }
    }

    /// <summary>
    /// Unregisters a plugin.
    /// </summary>
    public void Unregister(ISurrealDbPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        lock (_lock)
        {
            _plugins.Remove(plugin);
        }
    }

    /// <summary>
    /// Gets all registered plugins.
    /// </summary>
    public IEnumerable<ISurrealDbPlugin> GetPlugins()
    {
        lock (_lock)
        {
            return _plugins.ToList();
        }
    }

    /// <summary>
    /// Initializes all plugins.
    /// </summary>
    public async Task InitializeAllAsync(SurrealDbClient client, CancellationToken cancellationToken = default)
    {
        var plugins = GetPlugins();

        foreach (var plugin in plugins)
        {
            try
            {
                await plugin.OnInitializeAsync(client, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new PluginException($"Plugin '{plugin.Name}' initialization failed", ex);
            }
        }
    }

    /// <summary>
    /// Notifies all plugins that a session was created.
    /// </summary>
    public async Task NotifySessionCreatedAsync(ISurrealDbSession session, CancellationToken cancellationToken = default)
    {
        var plugins = GetPlugins();

        foreach (var plugin in plugins)
        {
            try
            {
                await plugin.OnSessionCreatedAsync(session, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new PluginException($"Plugin '{plugin.Name}' session creation hook failed", ex);
            }
        }
    }

    /// <summary>
    /// Notifies all plugins that a session is being disposed.
    /// </summary>
    public async Task NotifySessionDisposingAsync(ISurrealDbSession session, CancellationToken cancellationToken = default)
    {
        var plugins = GetPlugins();

        foreach (var plugin in plugins)
        {
            try
            {
                await plugin.OnSessionDisposingAsync(session, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new PluginException($"Plugin '{plugin.Name}' session disposing hook failed", ex);
            }
        }
    }
}

/// <summary>
/// Exception thrown by plugin operations.
/// </summary>
public class PluginException : Exception
{
    public PluginException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Abstract base class for implementing plugins.
/// </summary>
public abstract class PluginBase : ISurrealDbPlugin
{
    public abstract string Name { get; }
    public abstract string Version { get; }

    protected object? Configuration { get; set; }

    public virtual Task OnInitializeAsync(SurrealDbClient client, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task OnSessionCreatedAsync(ISurrealDbSession session, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task OnSessionDisposingAsync(ISurrealDbSession session, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual object? GetConfiguration() => Configuration;

    public virtual void SetConfiguration(object? configuration) => Configuration = configuration;
}
