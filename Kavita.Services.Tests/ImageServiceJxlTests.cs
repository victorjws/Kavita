using Kavita.API.Services;
using Kavita.Models.Entities.Enums;
using Kavita.Services.Helpers;
using Microsoft.Extensions.Logging;
using NetVips;
using NSubstitute;
using Image = NetVips.Image;

namespace Kavita.Services.Tests;

public class ImageServiceJxlTests : IDisposable
{
    private static readonly string FixtureDirectory = Path.Join(Directory.GetCurrentDirectory(), "../../../Test Data/JxlHeaderReader");
    private readonly string _outputDirectory = Path.Join(Path.GetTempPath(), $"kavita-jxl-cover-{Guid.NewGuid():N}");
    private readonly ImageService _imageService;

    public ImageServiceJxlTests()
    {
        Directory.CreateDirectory(_outputDirectory);
        var directoryService = new DirectoryService(Substitute.For<ILogger<DirectoryService>>(), new System.IO.Abstractions.FileSystem());
        _imageService = new ImageService(Substitute.For<ILogger<ImageService>>(), directoryService);
    }

    public void Dispose()
    {
        Directory.Delete(_outputDirectory, true);
    }

    /// <summary>
    /// Like the stream of an archive entry: forward only
    /// </summary>
    private class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private void AssertIsCoverSized(string outputFile)
    {
        Assert.True(File.Exists(outputFile), $"{outputFile} was not created");
        using var thumbnail = Image.NewFromFile(outputFile);
        Assert.InRange(thumbnail.Width, 1, 320);
        Assert.InRange(thumbnail.Height, 1, 455);
        Assert.True(thumbnail.Width == 320 || thumbnail.Height == 455, $"Thumbnail {thumbnail.Width}x{thumbnail.Height} was not scaled to the cover size");
    }

    [Fact]
    public void GetCoverImage_JxlFile_WritesThumbnail()
    {
        if (!JxlDecoder.IsSupported) return;

        var result = _imageService.GetCoverImage(Path.Join(FixtureDirectory, "cover-800x1200.jxl"), "cover", _outputDirectory,
            EncodeFormat.WEBP, CoverImageSize.Default);

        Assert.Equal("cover.webp", result);
        AssertIsCoverSized(Path.Join(_outputDirectory, result));
    }

    [Fact]
    public void GetCoverImage_JxlFile_PngEncoding()
    {
        if (!JxlDecoder.IsSupported) return;

        var result = _imageService.GetCoverImage(Path.Join(FixtureDirectory, "cover-800x1200.jxl"), "cover", _outputDirectory,
            EncodeFormat.PNG, CoverImageSize.Default);

        Assert.Equal("cover.png", result);
        AssertIsCoverSized(Path.Join(_outputDirectory, result));
    }

    [Fact]
    public void GetCoverImage_CorruptJxlFile_ReturnsEmpty()
    {
        var corrupt = Path.Join(_outputDirectory, "corrupt.jxl");
        File.WriteAllBytes(corrupt, [0xFF, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8]);

        var result = _imageService.GetCoverImage(corrupt, "cover", _outputDirectory, EncodeFormat.WEBP, CoverImageSize.Default);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void WriteCoverThumbnail_ForwardOnlyJxlStream_WritesThumbnail()
    {
        if (!JxlDecoder.IsSupported) return;

        using var stream = new ForwardOnlyStream(File.OpenRead(Path.Join(FixtureDirectory, "cover-800x1200.jxl")));

        var result = _imageService.WriteCoverThumbnail(stream, "cover", _outputDirectory, EncodeFormat.WEBP);

        Assert.Equal("cover.webp", result);
        AssertIsCoverSized(Path.Join(_outputDirectory, result));
    }

    [Fact]
    public void WriteCoverThumbnail_SeekableJxlStream_WritesThumbnail()
    {
        if (!JxlDecoder.IsSupported) return;

        using var stream = File.OpenRead(Path.Join(FixtureDirectory, "container-100x75.jxl"));

        var result = _imageService.WriteCoverThumbnail(stream, "cover", _outputDirectory, EncodeFormat.PNG);

        Assert.Equal("cover.png", result);
        Assert.True(File.Exists(Path.Join(_outputDirectory, result)));
    }

    [Fact]
    public void WriteCoverThumbnail_ForwardOnlyPngStream_StillWorks()
    {
        // Buffering non-seekable streams must not change the behavior for regular images
        using var stream = new ForwardOnlyStream(File.OpenRead(Path.Join(FixtureDirectory, "page-16x16.png")));

        var result = _imageService.WriteCoverThumbnail(stream, "cover", _outputDirectory, EncodeFormat.WEBP);

        Assert.Equal("cover.webp", result);
        Assert.True(File.Exists(Path.Join(_outputDirectory, result)));
    }

    [Fact]
    public void WriteCoverThumbnail_WhenJxlIsNotSupported_Throws()
    {
        if (JxlDecoder.IsSupported) return;

        using var stream = File.OpenRead(Path.Join(FixtureDirectory, "container-100x75.jxl"));

        Assert.ThrowsAny<Exception>(() => _imageService.WriteCoverThumbnail(stream, "cover", _outputDirectory, EncodeFormat.WEBP));
    }
}
