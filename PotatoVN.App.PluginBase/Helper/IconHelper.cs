using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PotatoVN.App.PluginBase.Helper;

public static class IconHelper
{
    // P/Invoke
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
    private static extern bool EnumResourceNames(IntPtr hModule, IntPtr lpszType, EnumResNameDelegate lpEnumFunc, IntPtr lParam);
    private delegate bool EnumResNameDelegate(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PrivateExtractIconsW(string lpszFile, int nIconIndex, int cxIcon, int cyIcon, IntPtr[] phicon, int[]? piconid, int nIcons, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    private const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020;
    private const int RT_GROUP_ICON = 14;
    private const int RT_ICON = 3;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct GRPICONDIR { public ushort idReserved, idType, idCount; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct GRPICONDIRENTRY { public byte bWidth, bHeight, bColorCount, bReserved; public ushort wPlanes, wBitCount; public uint dwBytesInRes; public ushort nID; }

    /// <summary>
    /// Extract high-quality icon as PNG.
    /// </summary>
    public static async Task<bool> SaveBestIconAsPngAsync(string exePath, string outputPath)
    {
        return await Task.Run(() =>
        {
            // 1. Try raw resource (best for PNG icons inside EXE)
            if (TryExtractResource(exePath, outputPath, asPng: true)) return true;
            // 2. Fallback to GDI+ conversion
            return ExtractWithGdi(exePath, outputPath, asPng: true);
        });
    }

    /// <summary>
    /// Extract high-quality icon as ICO.
    /// </summary>
    public static async Task<bool> ExtractBestIconAsync(string exePath, string outputPath)
    {
        return await Task.Run(() =>
        {
            // 1. Try raw resource (preserves original format wrapped in ICO)
            if (TryExtractResource(exePath, outputPath, asPng: false)) return true;
            // 2. Fallback to GDI+ conversion (PNG-compressed ICO for better quality)
            return ExtractWithGdi(exePath, outputPath, asPng: false);
        });
    }

    private static bool TryExtractResource(string exePath, string outputPath, bool asPng)
    {
        IntPtr hModule = IntPtr.Zero;
        try
        {
            hModule = LoadLibraryEx(exePath, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
            if (hModule == IntPtr.Zero) return false;

            IntPtr groupName = IntPtr.Zero;
            EnumResourceNames(hModule, (IntPtr)RT_GROUP_ICON, (h, t, n, p) => { groupName = n; return false; }, IntPtr.Zero);
            if (groupName == IntPtr.Zero) return false;

            var hResInfo = FindResource(hModule, groupName, (IntPtr)RT_GROUP_ICON);
            if (hResInfo == IntPtr.Zero) return false;

            var pResData = LockResource(LoadResource(hModule, hResInfo));
            if (pResData == IntPtr.Zero) return false;

            var dir = Marshal.PtrToStructure<GRPICONDIR>(pResData);
            GRPICONDIRENTRY bestEntry = default;
            int bestScore = -1;
            int offset = Marshal.SizeOf<GRPICONDIR>();

            for (int i = 0; i < dir.idCount; i++)
            {
                var entry = Marshal.PtrToStructure<GRPICONDIRENTRY>(IntPtr.Add(pResData, offset));
                offset += Marshal.SizeOf<GRPICONDIRENTRY>();
                int width = entry.bWidth == 0 ? 256 : entry.bWidth;
                int bpp = entry.wBitCount == 0 ? 32 : entry.wBitCount;
                int score = width * 100 + bpp;
                if (score > bestScore) { bestScore = score; bestEntry = entry; }
            }

            if (bestScore <= 0) return false;

            var hIconInfo = FindResource(hModule, (IntPtr)bestEntry.nID, (IntPtr)RT_ICON);
            if (hIconInfo == IntPtr.Zero) return false;

            var iconSize = SizeofResource(hModule, hIconInfo);
            var pIconData = LockResource(LoadResource(hModule, hIconInfo));
            if (pIconData == IntPtr.Zero || iconSize == 0) return false;

            // Check for PNG signature
            byte[] header = new byte[8];
            Marshal.Copy(pIconData, header, 0, 8);
            bool isPngResource = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;

            byte[] data = new byte[iconSize];
            Marshal.Copy(pIconData, data, 0, (int)iconSize);

            if (asPng)
            {
                // Requesting PNG
                if (isPngResource)
                {
                    File.WriteAllBytes(outputPath, data);
                    return true;
                }
                // If it's BMP, we can't save as PNG directly from raw data easily without parsing BMP header
                // So return false to let GDI fallback handle BMP->PNG conversion
                return false;
            }
            else
            {
                // Requesting ICO
                // Whether it's PNG or BMP, we can wrap it in an ICO container
                using var fs = new FileStream(outputPath, FileMode.Create);
                using var writer = new BinaryWriter(fs);

                writer.Write((ushort)0); // Reserved
                writer.Write((ushort)1); // Type (1=Icon)
                writer.Write((ushort)1); // Count (1 image)

                writer.Write(bestEntry.bWidth);
                writer.Write(bestEntry.bHeight);
                writer.Write(bestEntry.bColorCount);
                writer.Write(bestEntry.bReserved);
                writer.Write(bestEntry.wPlanes);
                writer.Write(bestEntry.wBitCount);
                writer.Write(iconSize);
                writer.Write((uint)22); // Offset (6 header + 16 directory)

                writer.Write(data);
                return true;
            }
        }
        catch { return false; }
        finally { if (hModule != IntPtr.Zero) FreeLibrary(hModule); }
    }

    private static bool ExtractWithGdi(string exePath, string outputPath, bool asPng)
    {
        // Optimization: Removed the loop that scanned the file 5 times.
        // We request 256x256 once. Windows API typically finds the closest match (scaling if needed).
        // If the large icon extraction fails entirely, we fallback once to 48x48.
        
        int size = 256;
        var phicon = new IntPtr[1];
        int count = 0;

        try 
        {
            // Attempt 1: Jumbo Icon
            count = PrivateExtractIconsW(exePath, 0, size, size, phicon, null, 1, 0);
            
            // Attempt 2: Standard Icon (if Jumbo failed)
            if (count <= 0 || phicon[0] == IntPtr.Zero)
            {
                size = 48;
                count = PrivateExtractIconsW(exePath, 0, size, size, phicon, null, 1, 0);
            }

            if (count > 0 && phicon[0] != IntPtr.Zero)
            {
                try
                {
                    using var icon = Icon.FromHandle(phicon[0]);
                    using var bitmap = icon.ToBitmap();

                    if (asPng)
                    {
                        bitmap.Save(outputPath, ImageFormat.Png);
                    }
                    else
                    {
                        // Save as PNG-compressed ICO to preserve quality and transparency
                        using var fs = new FileStream(outputPath, FileMode.Create);
                        using var writer = new BinaryWriter(fs);
                        using var ms = new MemoryStream();

                        bitmap.Save(ms, ImageFormat.Png);
                        byte[] pngData = ms.ToArray();

                        writer.Write((ushort)0);
                        writer.Write((ushort)1);
                        writer.Write((ushort)1);

                        byte w = (byte)(size >= 256 ? 0 : size);
                        byte h = (byte)(size >= 256 ? 0 : size);

                        writer.Write(w);
                        writer.Write(h);
                        writer.Write((byte)0); // Color count
                        writer.Write((byte)0); // Reserved
                        writer.Write((ushort)1); // Planes
                        writer.Write((ushort)32); // Bit count
                        writer.Write((uint)pngData.Length);
                        writer.Write((uint)22); // Offset

                        writer.Write(pngData);
                    }
                    return true;
                }
                finally
                {
                    DestroyIcon(phicon[0]);
                }
            }
        }
        catch 
        {
            // Fallthrough to return false
        }
        
        return false;
    }
}