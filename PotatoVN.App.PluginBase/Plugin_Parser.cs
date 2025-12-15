using GalgameManager.Contracts.Phrase;

namespace PotatoVN.App.PluginBase;

public partial class Plugin
{
    private GetChuParser? _parser;
    
    public IGalInfoPhraser GetPhraser()
    {
        _parser ??= new GetChuParser();
        return _parser;
    }

    public string ParserName => "getchu";
}