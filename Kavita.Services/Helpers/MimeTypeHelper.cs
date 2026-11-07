using System;
using MimeTypes;

namespace Kavita.Services.Helpers;

public static class MimeTypeHelper
{
    private const string Jxl = "image/jxl";

    /// <summary>
    /// <see cref="MimeTypeMap"/> has no entry for JPEG XL and no way to register one
    /// </summary>
    /// <param name="extension">Extension including the leading dot</param>
    public static string GetMimeType(string extension)
    {
        return extension.Equals(".jxl", StringComparison.OrdinalIgnoreCase) ? Jxl : MimeTypeMap.GetMimeType(extension);
    }
}
