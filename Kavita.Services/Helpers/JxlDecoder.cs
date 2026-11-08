using System;
using System.IO;
using System.Linq;
using ImageMagick;

namespace Kavita.Services.Helpers;

/// <summary>
/// Decodes JPEG XL for thumbnail generation. The libvips bundled with NetVips is built without libjxl, so JPEG XL is
/// decoded to PNG here and then handed to the regular NetVips pipeline.
/// </summary>
/// <remarks>Magick.NET has no native library on every platform (e.g. linux-arm, osx-x64), check <see cref="IsSupported"/></remarks>
public static class JxlDecoder
{
    private static readonly Lazy<bool> Supported = new(CheckSupport);

    /// <summary>
    /// Whether JPEG XL can be decoded on this platform
    /// </summary>
    public static bool IsSupported => Supported.Value;

    private static bool CheckSupport()
    {
        try
        {
            return MagickNET.SupportedFormats.Any(format => format.Format == MagickFormat.Jxl && format.SupportsReading);
        }
        catch (Exception)
        {
            // The native library is missing for this platform (DllNotFound, TypeInitialization, PlatformNotSupported)
            return false;
        }
    }

    /// <summary>
    /// Decodes a JPEG XL file to PNG
    /// </summary>
    /// <exception cref="InvalidOperationException">JPEG XL decoding is not supported on this platform</exception>
    public static byte[] DecodeToPng(string path)
    {
        EnsureSupported();
        using var image = new MagickImage(path, ReadSettings());
        return ToPng(image);
    }

    /// <inheritdoc cref="DecodeToPng(string)"/>
    public static byte[] DecodeToPng(Stream stream)
    {
        EnsureSupported();
        using var image = new MagickImage(stream, ReadSettings());
        return ToPng(image);
    }

    private static MagickReadSettings ReadSettings() => new() { Format = MagickFormat.Jxl };

    private static byte[] ToPng(MagickImage image)
    {
        // Apply the stored orientation so the thumbnail matches what a browser displays
        image.AutoOrient();
        image.Format = MagickFormat.Png;
        return image.ToByteArray();
    }

    private static void EnsureSupported()
    {
        if (!IsSupported) throw new InvalidOperationException("JPEG XL decoding is not supported on this platform");
    }
}
