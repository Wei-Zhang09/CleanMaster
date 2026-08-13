using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace CleanMaster.Converters;

public class IconExtractor
{
    /// <summary>
    /// Extracts an icon from the given file. Handles references like:
    /// - "C:\Program Files\App\app.exe" (extracts associated icon)
    /// - "C:\Windows\System32\shell32.dll,-123" (extracts icon at specific index)
    /// - ".lnk" shortcut files (resolves target first)
    /// Returns null if no icon can be extracted.
    /// </summary>
    public static BitmapSource? GetIcon(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        try
        {
            // Handle icon index syntax: "path.dll,-123" or "path.exe,0"
            string actualPath = filePath;
            int iconIndex = 0;
            
            var commaIdx = filePath.LastIndexOf(',');
            if (commaIdx > 0)
            {
                var indexPart = filePath.Substring(commaIdx + 1);
                // Negative index means resource ID, skip for now
                if (int.TryParse(indexPart, out var idx) && idx >= 0)
                {
                    actualPath = filePath.Substring(0, commaIdx);
                    iconIndex = idx;
                }
            }

            // Expand environment variables
            actualPath = Environment.ExpandEnvironmentVariables(actualPath);

            if (!File.Exists(actualPath))
                return null;

            // Try extract icon at specific index first
            if (iconIndex > 0)
            {
                try
                {
                    using var icon = Icon.ExtractAssociatedIcon(actualPath);
                    if (icon != null)
                    {
                        using var bitmap = icon.ToBitmap();
                        var hBitmap = bitmap.GetHbitmap();
                        try
                        {
                            return CreateFrozenBitmap(hBitmap);
                        }
                        finally { DeleteObject(hBitmap); }
                    }
                }
                catch { /* fall through to default extraction */ }
            }

            using var defaultIcon = Icon.ExtractAssociatedIcon(actualPath);
            if (defaultIcon == null) return null;

            using var bitmap2 = defaultIcon.ToBitmap();
            var hBitmap2 = bitmap2.GetHbitmap();
            try
            {
                return CreateFrozenBitmap(hBitmap2);
            }
            finally { DeleteObject(hBitmap2); }
        }
        catch
        {
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// Creates a frozen (thread-safe) BitmapSource from an HBitmap.
    /// Freezing is required so the bitmap can be created on a background
    /// thread and bound on the UI thread (IsAsync bindings).
    /// </summary>
    private static BitmapSource CreateFrozenBitmap(IntPtr hBitmap)
    {
        var source = Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap, IntPtr.Zero, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}

public class IconPathToImageSourceConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, WeakReference<BitmapSource>> _cache = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var path = value as string;
        if (string.IsNullOrEmpty(path)) return null;

        // Check cache
        if (_cache.TryGetValue(path, out var weakRef) && weakRef.TryGetTarget(out var cached))
            return cached;

        try
        {
            var result = IconExtractor.GetIcon(path);
            if (result != null)
            {
                _cache[path] = new WeakReference<BitmapSource>(result);
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
