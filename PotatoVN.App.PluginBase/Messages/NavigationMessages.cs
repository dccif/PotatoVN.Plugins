using GalgameManager.Models;

namespace PotatoVN.App.PluginBase.Messages;

public enum BigScreenRoute
{
    Home,
    Detail
}

public enum BigScreenNavMode
{
    Main,
    Overlay
}

public record BigScreenNavigateMessage(BigScreenRoute Route, object? Parameter = null, BigScreenNavMode Mode = BigScreenNavMode.Main);
public record BigScreenCloseOverlayMessage();
public record PlayGameMessage(Galgame Game);
