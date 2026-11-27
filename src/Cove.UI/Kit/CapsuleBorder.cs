using System.Windows;
using System.Windows.Controls;

namespace Cove.UI.Kit;

/// <summary>
/// A <see cref="Border"/> whose ends are fully round: its corner radius is kept at half its height,
/// so it stays a capsule whatever height a pill state needs. <see cref="MaxCornerRadius"/> caps it,
/// so the same surface can become a panel with ordinary rounded corners.
/// </summary>
public sealed class CapsuleBorder : Border
{
    /// <summary>Identifies the <see cref="MaxCornerRadius"/> property.</summary>
    public static readonly DependencyProperty MaxCornerRadiusProperty = DependencyProperty.Register(
        nameof(MaxCornerRadius),
        typeof(double),
        typeof(CapsuleBorder),
        new PropertyMetadata(double.PositiveInfinity, (border, _) => ((CapsuleBorder)border).UpdateCornerRadius()));

    /// <summary>
    /// The largest corner radius allowed. Infinite (the default) keeps the ends fully round; a panel
    /// sets the design's panel radius (SPEC.md §4: 12).
    /// </summary>
    public double MaxCornerRadius
    {
        get => (double)GetValue(MaxCornerRadiusProperty);
        set => SetValue(MaxCornerRadiusProperty, value);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (sizeInfo.HeightChanged)
        {
            UpdateCornerRadius();
        }
    }

    private void UpdateCornerRadius() => CornerRadius = new CornerRadius(Math.Min(ActualHeight / 2, MaxCornerRadius));
}
