using Microsoft.Maui.Handlers;
using Microsoft.UI.Input;

namespace Rambl.Platforms.Windows.Handlers;

/// <summary>Gives clickable sidebar items a hand cursor on Windows.</summary>
public sealed class SidebarItemHandler : ContentViewHandler
{
    /// <summary>Creates the native view with its hand cursor.</summary>
    /// <returns>A content view that displays a hand cursor over the item and its children.</returns>
    protected override Microsoft.Maui.Platform.ContentPanel CreatePlatformView() => new SidebarContentPanel
    {
        CrossPlatformLayout = VirtualView,
        IsHitTestVisible = true
    };

    private sealed class SidebarContentPanel : Microsoft.Maui.Platform.ContentPanel
    {
        /// <summary>Initializes a new instance of the <see cref="SidebarContentPanel"/> class.</summary>
        public SidebarContentPanel() =>
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }
}
