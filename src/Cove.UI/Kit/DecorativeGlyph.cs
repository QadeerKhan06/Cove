using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Cove.UI.Kit;

/// <summary>
/// A text glyph that's only decoration, such as an alert's icon. Screen readers skip it, since the
/// text next to it already says what it shows. WPF has no AccessibilityView="Raw" (that's WinUI);
/// this is the WPF way to get the same result.
/// </summary>
public sealed class DecorativeGlyph : TextBlock
{
    protected override AutomationPeer OnCreateAutomationPeer() => new DecorativePeer(this);

    // Neither a control nor content, so it only appears in UI Automation's raw view, which screen
    // readers such as Narrator don't read out (see "UI Automation Tree Overview" in Microsoft's docs).
    private sealed class DecorativePeer(TextBlock owner) : TextBlockAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;

        protected override bool IsContentElementCore() => false;
    }
}
