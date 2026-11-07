using System;
using System.IO;

namespace Kavita.Services.Helpers;

/// <summary>
/// Reads the displayed pixel size of a JPEG XL file from its header without decoding any pixels. The server has no
/// JPEG XL decoder, the browser renders the pages and this only supplies the page dimensions to the reader.
/// </summary>
/// <remarks>Supports both a bare codestream (FF 0A) and the ISOBMFF container (ISO/IEC 18181-2)</remarks>
public static class JxlHeaderReader
{
    private static readonly byte[] CodestreamSignature = [0xFF, 0x0A];
    private static readonly byte[] ContainerSignature = [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    /// <summary>
    /// SizeHeader, Metadata (orientation) and the bits before them fit well within this
    /// </summary>
    private const int HeaderBytes = 32;
    private const int MaxBoxes = 64;

    /// <summary>
    /// Fixed aspect ratios of SizeHeader.ratio (1-7) as numerator/denominator of width/height
    /// </summary>
    private static readonly (int Numerator, int Denominator)[] Ratios =
        [(1, 1), (12, 10), (4, 3), (3, 2), (16, 9), (5, 4), (2, 1)];

    /// <summary>
    /// Width/Height as displayed, so with EXIF orientations 5-8 (transposed/rotated 90) they are swapped
    /// </summary>
    /// <returns>False if the file is missing, not JPEG XL, or the header is malformed</returns>
    public static bool TryGetDimensions(string path, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return TryGetDimensions(stream, out width, out height);
        }
        catch (Exception)
        {
            width = 0;
            height = 0;
            return false;
        }
    }

    /// <inheritdoc cref="TryGetDimensions(string,out int,out int)"/>
    /// <remarks>The stream must be seekable</remarks>
    public static bool TryGetDimensions(Stream stream, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            if (!stream.CanSeek) return false;

            var codestreamOffset = FindCodestream(stream);
            if (codestreamOffset < 0) return false;

            stream.Seek(codestreamOffset, SeekOrigin.Begin);
            var header = new byte[HeaderBytes];
            var read = stream.Read(header, 0, header.Length);

            return TryParseCodestream(header.AsSpan(0, read), out width, out height);
        }
        catch (Exception)
        {
            width = 0;
            height = 0;
            return false;
        }
    }

    /// <summary>
    /// Offset of the codestream signature (FF 0A) in the stream, -1 if this is not a JPEG XL file
    /// </summary>
    private static long FindCodestream(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        Span<byte> start = stackalloc byte[ContainerSignature.Length];
        var read = stream.Read(start);

        if (read >= CodestreamSignature.Length && start[..CodestreamSignature.Length].SequenceEqual(CodestreamSignature))
        {
            return 0;
        }

        if (read < ContainerSignature.Length || !start.SequenceEqual(ContainerSignature)) return -1;

        // Walk the boxes after the signature box until the codestream is found
        var position = (long) ContainerSignature.Length;
        Span<byte> boxHeader = stackalloc byte[8];
        Span<byte> largeSize = stackalloc byte[8];

        for (var i = 0; i < MaxBoxes && position < stream.Length; i++)
        {
            stream.Seek(position, SeekOrigin.Begin);
            if (stream.Read(boxHeader) < boxHeader.Length) return -1;

            ulong size = ReadBigEndianUInt32(boxHeader);
            var type = boxHeader.Slice(4, 4);
            var headerLength = 8;

            if (size == 1)
            {
                if (stream.Read(largeSize) < largeSize.Length) return -1;
                size = ReadBigEndianUInt64(largeSize);
                headerLength = 16;
            }
            else if (size == 0)
            {
                // Box extends to the end of the file
                size = (ulong) (stream.Length - position);
            }

            if (size < (ulong) headerLength) return -1;

            if (type.SequenceEqual("jxlc"u8)) return position + headerLength;

            // The first partial codestream starts after its 4 byte index
            if (type.SequenceEqual("jxlp"u8)) return position + headerLength + 4;

            if (size > (ulong) (stream.Length - position)) return -1;
            position += (long) size;
        }

        return -1;
    }

    private static bool TryParseCodestream(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (data.Length < CodestreamSignature.Length || !data[..CodestreamSignature.Length].SequenceEqual(CodestreamSignature))
        {
            return false;
        }

        var bits = new BitReader(data[CodestreamSignature.Length..]);

        // SizeHeader
        var div8 = bits.Read(1) == 1;
        long parsedHeight = div8 ? (bits.Read(5) + 1) * 8L : ReadSize(ref bits);
        var ratio = (int) bits.Read(3);
        long parsedWidth;
        if (ratio == 0)
        {
            parsedWidth = div8 ? (bits.Read(5) + 1) * 8L : ReadSize(ref bits);
        }
        else
        {
            var (numerator, denominator) = Ratios[ratio - 1];
            parsedWidth = parsedHeight * numerator / denominator;
        }

        // ImageMetadata: only the orientation is of interest
        var orientation = 1;
        if (bits.Read(1) == 0)
        {
            var extraFields = bits.Read(1) == 1;
            if (extraFields) orientation = (int) bits.Read(3) + 1;
        }

        if (bits.Overrun) return false;
        if (parsedWidth <= 0 || parsedHeight <= 0 || parsedWidth > int.MaxValue || parsedHeight > int.MaxValue) return false;

        // Orientations 5-8 transpose or rotate by 90 degrees
        if (orientation >= 5)
        {
            (parsedWidth, parsedHeight) = (parsedHeight, parsedWidth);
        }

        width = (int) parsedWidth;
        height = (int) parsedHeight;
        return true;
    }

    /// <summary>
    /// U32(Bits(9), Bits(13), Bits(18), Bits(30)) + 1
    /// </summary>
    private static long ReadSize(ref BitReader bits)
    {
        var selector = bits.Read(2);
        var count = selector switch
        {
            0 => 9,
            1 => 13,
            2 => 18,
            _ => 30
        };

        return bits.Read(count) + 1L;
    }

    private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> bytes) =>
        ((uint) bytes[0] << 24) | ((uint) bytes[1] << 16) | ((uint) bytes[2] << 8) | bytes[3];

    private static ulong ReadBigEndianUInt64(ReadOnlySpan<byte> bytes) =>
        ((ulong) ReadBigEndianUInt32(bytes) << 32) | ReadBigEndianUInt32(bytes[4..]);

    /// <summary>
    /// Reads bits LSB-first as the codestream is packed. Reading past the end sets <see cref="Overrun"/> instead of throwing.
    /// </summary>
    private ref struct BitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public bool Overrun { get; private set; }

        public uint Read(int count)
        {
            uint value = 0;
            for (var i = 0; i < count; i++)
            {
                var byteIndex = _position >> 3;
                if (byteIndex >= _data.Length)
                {
                    Overrun = true;
                    return 0;
                }

                value |= (uint) ((_data[byteIndex] >> (_position & 7)) & 1) << i;
                _position++;
            }

            return value;
        }
    }
}
