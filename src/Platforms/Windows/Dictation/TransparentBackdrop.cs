using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Rambl.Platforms.Windows.Dictation;

/// <summary>A window backdrop that shows what is behind the window, so rounded content and shadows can float.</summary>
internal sealed class TransparentBackdrop : SystemBackdrop
{
    /// <inheritdoc/>
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot) =>
        connectedTarget.SystemBackdrop = new global::Windows.UI.Composition.Compositor().CreateColorBrush(
            global::Windows.UI.Color.FromArgb(0, 0, 0, 0)
        );

    /// <inheritdoc/>
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget) =>
        disconnectedTarget.SystemBackdrop = null;
}
