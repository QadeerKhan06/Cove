using System.Reflection;
using System.Windows.Automation.Peers;
using Cove.UI.Kit;

namespace Cove.Tests.UI.Kit;

public sealed class DecorativeGlyphTests
{
    // Checks the glyph's own answer, not what a screen reader ends up seeing: WPF also hides
    // anything that isn't on screen, which a test can't show. Narrator is checked by hand.
    [Fact]
    public void Peer_SaysTheGlyphIsNeitherAControlNorContent()
    {
        var answers = (IsControl: (bool?)null, IsContent: (bool?)null);

        RunOnStaThread(() =>
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(new DecorativeGlyph { Text = "" });
            answers = (CallCore(peer, "IsControlElementCore"), CallCore(peer, "IsContentElementCore"));
        });

        Assert.Equal((false, false), answers);
    }

    // The ...Core methods are protected, so the test calls them by reflection.
    private static bool CallCore(AutomationPeer peer, string methodName)
    {
        var method = peer.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(peer.GetType().Name, methodName);
        return method.Invoke(peer, null) is true;
    }

    // WPF elements can only be created on an STA thread, and xUnit runs tests on MTA threads.
    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }
}
