namespace Cove.UI.Pill;

/// <summary>The icon at the start of an alert: a glyph in a theme colour, or the design's filled dot.</summary>
/// <param name="Glyph">
/// A character from Segoe Fluent Icons (Segoe MDL2 Assets on Windows 10), or null for a filled dot,
/// which the design uses for "mic live" (SPEC.md §8).
/// </param>
/// <param name="BrushKey">The theme brush it's drawn in, e.g. "SystemFillColorSuccessBrush".</param>
public sealed record AlertIcon(string? Glyph, string BrushKey);
