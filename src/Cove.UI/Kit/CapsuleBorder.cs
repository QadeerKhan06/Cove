using System.Windows;
using System.Windows.Controls;

namespace Cove.UI.Kit;

/// <summary>
/// A <see cref="Border"/> whose ends are always fully round: its corner radius is kept at half its
/// height, so it stays a capsule whatever height a pill state needs.
/// </summary>
public sealed class CapsuleBorder : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (sizeInfo.HeightChanged)
        {
            CornerRadius = new CornerRadius(sizeInfo.NewSize.Height / 2);
        }
    }
}
