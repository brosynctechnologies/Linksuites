using System.Numerics;

namespace Saas.Application.Property;

/// <summary>
/// Difference hash for spotting the same photo uploaded twice, even after resizing or recompression.
/// The caller shrinks the image to 9x8 greyscale; each bit records whether a pixel is brighter than its right neighbour.
/// </summary>
public static class ImageHash
{
    public const int Width = 9;
    public const int Height = 8;
    /// <summary>Hashes this close (out of 64 bits) are treated as the same photo.</summary>
    public const int DuplicateThreshold = 6;

    public static ulong DHash(ReadOnlySpan<byte> gray9x8)
    {
        if (gray9x8.Length != Width * Height) throw new ArgumentException("Expected 9x8 greyscale pixels.", nameof(gray9x8));
        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width - 1; x++, bit++)
            if (gray9x8[y * Width + x] > gray9x8[y * Width + x + 1])
                hash |= 1UL << bit;
        return hash;
    }

    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    /// <summary>
    /// False for near-uniform images (few or almost all gradient bits set): plain walls, sky and line drawings hash alike even
    /// when they are different photos, so they aren't compared.
    /// </summary>
    public static bool IsDistinctive(ulong hash) => BitOperations.PopCount(hash) is > 8 and < 56;
}
