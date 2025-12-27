using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase.Helper;

public static class FileHelper
{
    private static string? _pluginPath;

    public static void Init(string pluginPath)
    {
        _pluginPath = pluginPath;
    }

    public static async Task<string?> GetImageFolderPathAsync()
    {
        if (string.IsNullOrEmpty(_pluginPath))
        {
            return null;
        }

        try
        {
            var imageDir = Path.Combine(_pluginPath, "Images");
            if (!Directory.Exists(imageDir))
            {
                Directory.CreateDirectory(imageDir);
            }
            return await Task.FromResult(imageDir);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FileHelper Error: {ex}");
            return null;
        }
    }
}
