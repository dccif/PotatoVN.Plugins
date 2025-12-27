using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase.Helper;

public static class IconHelper
{
    // --- P/Invoke Definitions ---
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceNames(IntPtr hModule, IntPtr lpszType, EnumResNameDelegate lpEnumFunc,
        IntPtr lParam);

    private delegate bool EnumResNameDelegate(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PrivateExtractIconsW(string lpszFile, int nIconIndex, int cxIcon, int cyIcon,
        IntPtr[] phicon, int[]? piconid, int nIcons, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi,
        uint cbFileInfo, uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    private const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020;
    private const int RT_GROUP_ICON = 14;
    private const int RT_ICON = 3;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct GRPICONDIR
    {
        public ushort idReserved, idType, idCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct GRPICONDIRENTRY
    {
        public byte bWidth, bHeight, bColorCount, bReserved;
        public ushort wPlanes, wBitCount;
        public uint dwBytesInRes;
        public ushort nID;
    }

    /// <summary>
    /// Extract high-quality icon as PNG.
    /// Prioritizes Quality: TryExtractResource -> PrivateExtractIconsW (256) -> SHGetFileInfo
    /// </summary>
    public static async Task<bool> SaveBestIconAsPngAsync(string exePath, string outputPath)
    {
        return await Task.Run(() =>
        {
            // 1. Try raw resource (best for PNG icons inside EXE)
            if (TryExtractResource(exePath, outputPath, true)) return true;
            // 2. Fallback to GDI+ conversion (Quality Mode)
            return ExtractWithGdi(exePath, outputPath, true, false);
        });
    }

    /// <summary>
    /// Extract high-quality icon as ICO.
    /// Prioritizes Speed/Quality Balance: PrivateExtractIconsW (256) -> SHGetFileInfo (Cache)
    /// Skips TryExtractResource (LoadLibrary) to avoid parsing heavy EXEs for just a shortcut icon.
    /// </summary>
    public static async Task<bool> ExtractBestIconAsync(string exePath, string outputPath)
    {
        return await Task.Run(() =>
        {
            // 1. Try GDI with Quality priority (Try 256x256 first)
            // This fixes the "icon too small" issue while still being faster than TryExtractResource
            if (ExtractWithGdi(exePath, outputPath, false, false)) return true;

            // 2. Fallback to raw resource (only if GDI failed completely)
            return TryExtractResource(exePath, outputPath, false);
        });
    }

    private static bool TryExtractResource(string exePath, string outputPath, bool asPng)
    {
        // Optimization: Skip manual resource parsing for large files (> 100MB).
        // LoadLibraryEx maps the file into memory, which is extremely slow for large EXEs.
        try
        {
            var fileInfo = new FileInfo(exePath);
            if (fileInfo.Length > 100 * 1024 * 1024) return false;
        }
        catch
        {
            return false;
        }

        var hModule = IntPtr.Zero;
        try
        {
            hModule = LoadLibraryEx(exePath, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
            if (hModule == IntPtr.Zero) return false;

            var groupName = IntPtr.Zero;
            EnumResourceNames(hModule, (IntPtr)RT_GROUP_ICON, (h, t, n, p) =>
            {
                groupName = n;
                return false;
            }, IntPtr.Zero);
            if (groupName == IntPtr.Zero) return false;

            var hResInfo = FindResource(hModule, groupName, (IntPtr)RT_GROUP_ICON);
            if (hResInfo == IntPtr.Zero) return false;

            var pResData = LockResource(LoadResource(hModule, hResInfo));
            if (pResData == IntPtr.Zero) return false;

            var dir = Marshal.PtrToStructure<GRPICONDIR>(pResData);
            GRPICONDIRENTRY bestEntry = default;
            var bestScore = -1;
            var offset = Marshal.SizeOf<GRPICONDIR>();

            for (var i = 0; i < dir.idCount; i++)
            {
                var entry = Marshal.PtrToStructure<GRPICONDIRENTRY>(IntPtr.Add(pResData, offset));
                offset += Marshal.SizeOf<GRPICONDIRENTRY>();
                var width = entry.bWidth == 0 ? 256 : entry.bWidth;
                var bpp = entry.wBitCount == 0 ? 32 : entry.wBitCount;
                var score = width * 100 + bpp;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEntry = entry;
                }
            }

            if (bestScore <= 0) return false;

            var hIconInfo = FindResource(hModule, (IntPtr)bestEntry.nID, (IntPtr)RT_ICON);
            if (hIconInfo == IntPtr.Zero) return false;

            var iconSize = SizeofResource(hModule, hIconInfo);
            var pIconData = LockResource(LoadResource(hModule, hIconInfo));
            if (pIconData == IntPtr.Zero || iconSize == 0) return false;

            var header = new byte[8];
            Marshal.Copy(pIconData, header, 0, 8);
            var isPngResource = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;

            var data = new byte[iconSize];
            Marshal.Copy(pIconData, data, 0, (int)iconSize);

            if (asPng)
            {
                if (isPngResource)
                {
                    File.WriteAllBytes(outputPath, data);
                    return true;
                }

                return false;
            }
            else
            {
                using var fs = new FileStream(outputPath, FileMode.Create);
                using var writer = new BinaryWriter(fs);
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)1);
                writer.Write(bestEntry.bWidth);
                writer.Write(bestEntry.bHeight);
                writer.Write(bestEntry.bColorCount);
                writer.Write(bestEntry.bReserved);
                writer.Write(bestEntry.wPlanes);
                writer.Write(bestEntry.wBitCount);
                writer.Write(iconSize);
                writer.Write((uint)22);
                writer.Write(data);
                return true;
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hModule != IntPtr.Zero) FreeLibrary(hModule);
        }
    }

    private static bool ExtractWithGdi(string exePath, string outputPath, bool asPng, bool prioritizeSpeed)
    {
        var size = 256;
        var phicon = new IntPtr[1];
        var count = 0;
        var hIcon = IntPtr.Zero;
        var createdHandle = false;

        try
        {
            if (prioritizeSpeed)
            {
                // Path A: FASTEST (Shell Cache)
                var shinfo = new SHFILEINFO();
                if (SHGetFileInfo(exePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON) !=
                    IntPtr.Zero)
                {
                    hIcon = shinfo.hIcon;
                    createdHandle = true;
                    size = 48;
                }

                if (hIcon == IntPtr.Zero)
                {
                    count = PrivateExtractIconsW(exePath, 0, 256, 256, phicon, null, 1, 0);
                    if (count > 0 && phicon[0] != IntPtr.Zero)
                    {
                        hIcon = phicon[0];
                        createdHandle = true;
                        size = 256;
                    }
                }
            }
            else
            {
                // Path B: QUALITY (PrivateExtractIconsW 256px)
                count = PrivateExtractIconsW(exePath, 0, 256, 256, phicon, null, 1, 0);
                if (count > 0 && phicon[0] != IntPtr.Zero)
                {
                    hIcon = phicon[0];
                    createdHandle = true;
                    size = 256;
                }

                if (hIcon == IntPtr.Zero)
                {
                    var shinfo = new SHFILEINFO();
                    if (SHGetFileInfo(exePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo),
                            SHGFI_ICON | SHGFI_LARGEICON) != IntPtr.Zero)
                    {
                        hIcon = shinfo.hIcon;
                        createdHandle = true;
                        size = 48;
                    }
                }
            }

            if (hIcon != IntPtr.Zero)
                try
                {
                    using var icon = Icon.FromHandle(hIcon);
                    using var bitmap = icon.ToBitmap();

                    if (asPng)
                    {
                        bitmap.Save(outputPath, ImageFormat.Png);
                    }
                    else
                    {
                        using var fs = new FileStream(outputPath, FileMode.Create);
                        using var writer = new BinaryWriter(fs);
                        using var ms = new MemoryStream();
                        bitmap.Save(ms, ImageFormat.Png);
                        var pngData = ms.ToArray();

                        writer.Write((ushort)0);
                        writer.Write((ushort)1);
                        writer.Write((ushort)1);
                        var w = (byte)(size >= 256 ? 0 : size);
                        var h = (byte)(size >= 256 ? 0 : size);
                        writer.Write(w);
                        writer.Write(h);
                        writer.Write((byte)0);
                        writer.Write((byte)0);
                        writer.Write((ushort)1);
                        writer.Write((ushort)32);
                        writer.Write((uint)pngData.Length);
                        writer.Write((uint)22);
                        writer.Write(pngData);
                    }

                    return true;
                }
                finally
                {
                    if (createdHandle) DestroyIcon(hIcon);
                }
        }
        catch
        {
        }

        return false;
    }
}