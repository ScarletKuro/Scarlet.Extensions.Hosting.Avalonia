namespace Scarlet.Extensions.Hosting.Avalonia;

/// <summary>
/// Options for <see cref="AvaloniaLifetime"/>.
/// </summary>
public sealed class AvaloniaLifetimeOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether host lifetime status messages (started, stopping, ...) are suppressed.
    /// </summary>
    public bool SuppressStatusMessages { get; set; }
}
