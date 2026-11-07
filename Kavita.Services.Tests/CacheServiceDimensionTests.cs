using Kavita.API.Database;
using Kavita.API.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Kavita.Services.Tests;

public class CacheServiceDimensionTests
{
    private static readonly string FixtureDirectory = Path.Join(Directory.GetCurrentDirectory(), "../../../Test Data/JxlHeaderReader");

    private static CacheService CreateService(IDirectoryService directoryService)
    {
        return new CacheService(Substitute.For<ILogger<CacheService>>(), Substitute.For<IUnitOfWork>(), directoryService,
            Substitute.For<IReadingItemService>(), Substitute.For<IBookmarkService>(), Substitute.For<ILocalizationService>());
    }

    [Fact]
    public void GetCachedFileDimensions_MixedJxlAndPng_ReturnsEveryPage()
    {
        var cachePath = Path.Join(Path.GetTempPath(), $"kavita-jxl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cachePath);
        try
        {
            File.Copy(Path.Join(FixtureDirectory, "page-16x16.png"), Path.Join(cachePath, "1.png"));
            File.Copy(Path.Join(FixtureDirectory, "container-100x75.jxl"), Path.Join(cachePath, "2.jxl"));
            File.Copy(Path.Join(FixtureDirectory, "bare-37x91.jxl"), Path.Join(cachePath, "3.jxl"));
            File.Copy(Path.Join(FixtureDirectory, "page-16x16.png"), Path.Join(cachePath, "4.png"));

            var directoryService = Substitute.For<IDirectoryService>();
            directoryService.GetFilesWithExtension(cachePath, Arg.Any<string>())
                .Returns(Directory.GetFiles(cachePath));

            var dimensions = CreateService(directoryService).GetCachedFileDimensions(cachePath).ToList();

            Assert.Equal(4, dimensions.Count);
            Assert.Equal(new[] {0, 1, 2, 3}, dimensions.Select(d => d.PageNumber).ToArray());
            Assert.Equal(new[] {(16, 16), (100, 75), (37, 91), (16, 16)}, dimensions.Select(d => (d.Width, d.Height)).ToArray());
            Assert.Equal(new[] {false, true, false, false}, dimensions.Select(d => d.IsWide).ToArray());
        }
        finally
        {
            Directory.Delete(cachePath, true);
        }
    }

    [Fact]
    public void GetCachedFileDimensions_CorruptJxl_DoesNotDropOtherPages()
    {
        var cachePath = Path.Join(Path.GetTempPath(), $"kavita-jxl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cachePath);
        try
        {
            File.Copy(Path.Join(FixtureDirectory, "page-16x16.png"), Path.Join(cachePath, "1.png"));
            File.WriteAllBytes(Path.Join(cachePath, "2.jxl"), [1, 2, 3]);
            File.Copy(Path.Join(FixtureDirectory, "bare-37x91.jxl"), Path.Join(cachePath, "3.jxl"));

            var directoryService = Substitute.For<IDirectoryService>();
            directoryService.GetFilesWithExtension(cachePath, Arg.Any<string>())
                .Returns(Directory.GetFiles(cachePath));

            var dimensions = CreateService(directoryService).GetCachedFileDimensions(cachePath).ToList();

            // The unreadable page is skipped but the pages around it keep their own page numbers
            Assert.Equal(new[] {0, 2}, dimensions.Select(d => d.PageNumber).ToArray());
            Assert.Equal(new[] {(16, 16), (37, 91)}, dimensions.Select(d => (d.Width, d.Height)).ToArray());
        }
        finally
        {
            Directory.Delete(cachePath, true);
        }
    }
}
