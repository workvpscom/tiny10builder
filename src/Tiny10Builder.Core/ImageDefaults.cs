namespace Tiny10Builder.Core;

public static class ImageDefaults
{
    public const string NoProMessage = "No Windows 10 Pro image in this source.";

    public static bool IsProfessional(WindowsImage image) =>
        image.Edition.Equals("Professional", StringComparison.OrdinalIgnoreCase) ||
        image.Name.Equals("Windows 10 Pro", StringComparison.OrdinalIgnoreCase);

    public static void RequireProfessional(WindowsImage image)
    {
        if (!IsProfessional(image)) throw new InvalidDataException(NoProMessage);
    }

    public static int SelectPosition(IReadOnlyList<WindowsImage> images, int? restoredIndex = null)
    {
        if (images.Count == 0) throw new InvalidDataException("The source contains no Windows editions.");
        if (restoredIndex is int saved)
            for (var i = 0; i < images.Count; i++)
                if (images[i].Index == saved) return i;
        for (var i = 0; i < images.Count; i++)
            if (IsProfessional(images[i])) return i;
        throw new InvalidDataException(NoProMessage);
    }
}
