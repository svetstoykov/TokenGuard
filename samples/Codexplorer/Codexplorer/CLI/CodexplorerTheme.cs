using Spectre.Console;

namespace Codexplorer.CLI;

/// <summary>
/// Defines the shared visual palette for Codexplorer console rendering.
/// </summary>
/// <remarks>
/// This record is the single source of truth for renderer colors and text styles. Components consume it instead of
/// embedding local color choices so the entire console look can be retuned from one place.
/// </remarks>
internal sealed record CodexplorerTheme(
    Color Title,
    Color Accent,
    Color Muted,
    Color Success,
    Color Warn,
    Color Error)
{
    /// <summary>
    /// Gets default Codexplorer console theme.
    /// </summary>
    public static CodexplorerTheme Default { get; } = new(
        Color.DeepSkyBlue1,
        Color.Fuchsia,
        Color.Grey,
        Color.Green,
        Color.Yellow,
        Color.Red);

    /// <summary>
    /// Gets emphasized title style.
    /// </summary>
    public Style TitleStyle => new(this.Title, decoration: Decoration.Bold);

    /// <summary>
    /// Gets accent style for highlighted metrics.
    /// </summary>
    public Style AccentStyle => new(this.Accent, decoration: Decoration.Bold);

    /// <summary>
    /// Gets muted style for secondary details.
    /// </summary>
    public Style MutedStyle => new(this.Muted);

    /// <summary>
    /// Gets success style for positive terminal states.
    /// </summary>
    public Style SuccessStyle => new(this.Success, decoration: Decoration.Bold);

    /// <summary>
    /// Gets warning style for degraded terminal states.
    /// </summary>
    public Style WarnStyle => new(this.Warn, decoration: Decoration.Bold);

    /// <summary>
    /// Gets error style for failed terminal states.
    /// </summary>
    public Style ErrorStyle => new(this.Error, decoration: Decoration.Bold);
}
