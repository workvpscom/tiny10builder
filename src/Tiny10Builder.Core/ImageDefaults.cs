namespace Tiny10Builder.Core;

public static class ImageDefaults
{
    public static int SelectPosition(IReadOnlyList<WindowsImage> images, int? restoredIndex = null)
    {
        if (images.Count == 0) throw new InvalidDataException("The source contains no Windows editions.");
        if (restoredIndex is int saved)
            for (var i = 0; i < images.Count; i++)
                if (images[i].Index == saved) return i;
        for (var i = 0; i < images.Count; i++)
            if (images[i].Edition.Equals("Professional", StringComparison.OrdinalIgnoreCase) ||
                images[i].Name.Equals("Windows 10 Pro", StringComparison.OrdinalIgnoreCase)) return i;
        throw new InvalidDataException("No Windows 10 Pro image in this source.");
    }
}
