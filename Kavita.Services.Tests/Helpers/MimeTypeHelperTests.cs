using Kavita.Services.Helpers;

namespace Kavita.Services.Tests.Helpers;

public class MimeTypeHelperTests
{
    [Theory]
    [InlineData(".jxl", "image/jxl")]
    [InlineData(".JXL", "image/jxl")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".avif", "image/avif")]
    [InlineData(".unknownext", "application/octet-stream")]
    public void GetMimeType(string extension, string expected)
    {
        Assert.Equal(expected, MimeTypeHelper.GetMimeType(extension));
    }
}
