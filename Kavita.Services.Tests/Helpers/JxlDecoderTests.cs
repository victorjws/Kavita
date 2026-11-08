using System.Runtime.InteropServices;
using Kavita.Services.Helpers;
using NetVips;
using Image = NetVips.Image;

namespace Kavita.Services.Tests.Helpers;

public class JxlDecoderTests
{
    private static readonly string TestDirectory = Path.Join(Directory.GetCurrentDirectory(), "../../../Test Data/JxlHeaderReader");

    /// <summary>
    /// The Magick.NET native library is not available on every platform (e.g. linux-arm, osx-x64)
    /// </summary>
    private static bool Supported => JxlDecoder.IsSupported;

    /// <summary>
    /// Guards the other tests, which return early when JXL is unsupported, from silently doing nothing on a platform that should support it
    /// </summary>
    [Fact]
    public void IsSupported_OnPlatformsWithMagickNative()
    {
        var hasNative = OperatingSystem.IsWindows()
                        || (OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture is Architecture.X64 or Architecture.Arm64)
                        || (OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64);
        if (!hasNative) return;

        Assert.True(JxlDecoder.IsSupported);
    }

    [Theory]
    [InlineData("bare-64x48.jxl", 64, 48)]
    [InlineData("bare-37x91.jxl", 37, 91)]
    [InlineData("container-100x75.jxl", 100, 75)]
    [InlineData("cover-800x1200.jxl", 800, 1200)]
    public void DecodeToPng_FromPath_ReturnsPngWithSameSize(string file, int width, int height)
    {
        if (!Supported) return;

        var png = JxlDecoder.DecodeToPng(Path.Join(TestDirectory, file));

        Assert.Equal(new byte[] {0x89, 0x50, 0x4E, 0x47}, png.Take(4).ToArray());

        // NetVips reads the PNG back, which also proves the output is a valid PNG
        using var image = Image.NewFromBuffer(png);
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
    }

    [Fact]
    public void DecodeToPng_FromStream_ReturnsPng()
    {
        if (!Supported) return;

        using var stream = File.OpenRead(Path.Join(TestDirectory, "container-100x75.jxl"));

        var png = JxlDecoder.DecodeToPng(stream);

        using var image = Image.NewFromBuffer(png);
        Assert.Equal(100, image.Width);
        Assert.Equal(75, image.Height);
    }

    [Fact]
    public void DecodeToPng_Garbage_Throws()
    {
        if (!Supported) return;

        using var stream = new MemoryStream(new byte[] {0xFF, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8});

        Assert.ThrowsAny<Exception>(() => JxlDecoder.DecodeToPng(stream));
    }

    [Fact]
    public void DecodeToPng_WhenUnsupported_ThrowsInvalidOperation()
    {
        if (Supported) return;

        Assert.Throws<InvalidOperationException>(() => JxlDecoder.DecodeToPng(Path.Join(TestDirectory, "bare-64x48.jxl")));
    }
}
