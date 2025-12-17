using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;

namespace PotatoVN.App.PluginBase.Helper;

public static class HostFileHelper
{
    private static bool _isInitialized = false;
    private static MethodInfo? _getFolderMethod;
    private static object? _folderTypeImages; // Store the enum value

    private static void Initialize()
    {
        if (_isInitialized) return;

        try
        {
            var assembly = Assembly.Load("GalgameManager");
            var fileHelperType = assembly.GetType("GalgameManager.Helpers.FileHelper");

            if (fileHelperType != null)
            {
                _getFolderMethod = AccessTools.Method(fileHelperType, "GetFolderAsync");

                var folderTypeEnum = assembly.GetType("GalgameManager.Helpers.FileHelper+FolderType");
                if (folderTypeEnum != null)
                {
                    // FolderType.Images is the second value (index 1)
                    // Root=0, Images=1, Plugins=2
                    _folderTypeImages = Enum.ToObject(folderTypeEnum, 1);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HostFileHelper Initialize Error: {ex}");
        }
        finally
        {
            _isInitialized = true;
        }
    }

    public static async Task<string?> GetImageFolderPathAsync()
    {
        Initialize();

        if (_getFolderMethod == null || _folderTypeImages == null)
        {
            return null;
        }

        try
        {
            // Invoke GetFolderAsync(FolderType.Images)
            var task = (Task)_getFolderMethod.Invoke(null, new object[] { _folderTypeImages });
            await task.ConfigureAwait(false);

            // Get Result property from Task<StorageFolder>
            var resultProperty = task.GetType().GetProperty("Result");
            var storageFolder = resultProperty?.GetValue(task);

            if (storageFolder != null)
            {
                // Get Path property from StorageFolder
                var pathProperty = storageFolder.GetType().GetProperty("Path");
                return pathProperty?.GetValue(storageFolder) as string;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetImageFolderPathAsync Error: {ex}");
        }

        return null;
    }
}
