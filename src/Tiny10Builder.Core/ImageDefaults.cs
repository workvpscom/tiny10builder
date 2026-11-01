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

    /// <summary>
    /// The error for a selected image that is not Windows 10 Pro. When another image of the source is Pro, the message names the
    /// selected image and the Pro index; when the source has no Pro at all it is exactly <see cref="NoProMessage"/>.
    /// </summary>
    public static string NotProfessionalMessage(WindowsImage selected, IReadOnlyList<WindowsImage> all)
    {
        int position;
        try { position = SelectPosition(all); }
        catch (InvalidDataException) { return NoProMessage; }
        var pro = all[position];
        return $"Image index {selected.Index} is {selected.Name}; Windows 10 Pro is index {pro.Index}. Set ImageIndex to {pro.Index}.";
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
