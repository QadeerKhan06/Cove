#if DEBUG
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Cove.Core.Contracts;

namespace Cove.App;

/// <summary>
/// A panel for checking the pill by hand before real panels exist (C-06). Debug builds only.
/// TODO(C-12): remove, with the debug shortcut, once the Menu opens real panels.
/// </summary>
/// <remarks>
/// Laid out the way the panel template (C-08) will be: a header and a footer that stay put, and a
/// list between them long enough to reach the 420 px cap and scroll.
/// </remarks>
internal sealed class TestPanel(Action close) : IPanel
{
    public const string PanelId = "test";

    // Enough rows to pass the 420 px cap.
    private const int RowCount = 20;

    // The design's panel header height (SPEC.md §9).
    private const double HeaderHeight = 40;

    public string Id => PanelId;

    public string Title => "Test panel";

    public char MenuLetter => 'X';

    // Like the Files drop zone, it waits for input: clicking another app leaves it on screen.
    public bool ClosesOnOutsideClick => false;

    public object CreateView()
    {
        var closeButton = new Button { Content = "✕", Width = 32, Height = 32 };
        AutomationProperties.SetName(closeButton, "Close");
        closeButton.Click += (_, _) => close();
        DockPanel.SetDock(closeButton, Dock.Right);

        var header = new DockPanel { Height = HeaderHeight };
        header.Children.Add(closeButton);
        header.Children.Add(new TextBlock
        {
            Text = Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // Focus starts here, so typing right after the shortcut shows the panel has keyboard focus.
        var input = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        AutomationProperties.SetName(input, "Test input");
        input.Loaded += (_, _) => input.Focus();

        var rows = new StackPanel();
        rows.Children.Add(new TextBlock { Text = "Type here; it shouldn't reach the app behind:" });
        rows.Children.Add(input);
        for (var row = 1; row <= RowCount; row++)
        {
            rows.Children.Add(new TextBlock { Text = $"Row {row}", Margin = new Thickness(0, 4, 0, 4) });
        }

        var doneButton = new Button { Content = "Done", Padding = new Thickness(12, 4, 12, 4) };
        doneButton.Click += (_, _) => close();
        DockPanel.SetDock(doneButton, Dock.Right);

        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        footer.Children.Add(doneButton);
        footer.Children.Add(new TextBlock
        {
            Text = "Esc, ✕ or Done closes it. Clicking another app doesn't.",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return layout;
    }

    public void OnOpened(object? parameter)
    {
    }

    public void OnClosed()
    {
    }
}
#endif
