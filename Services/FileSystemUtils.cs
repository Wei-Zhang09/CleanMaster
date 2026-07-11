using System.Diagnostics;
using System.IO;

namespace CleanMaster.Services;

public static class FileSystemUtils
{
    public static long GetDirectorySize(string path, int maxDepth = -1)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = maxDepth == -1
            }))
            {
                try { size += new FileInfo(file).Length; } catch (Exception ex) { Debug.WriteLine($"GetDirectorySize: {ex.Message}"); }
            }
        }
        catch (Exception ex) { CleanMaster.App.LogError("GetDirectorySize", ex); }
        return size;
    }
}
