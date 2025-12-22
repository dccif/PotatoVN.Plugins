using GalgameManager.Models;

namespace PotatoVN.App.PluginBase.Messages;

public record NavigateToDetailMessage(Galgame Game);
public record NavigateToHomeMessage();
public record PlayGameMessage(Galgame Game);
