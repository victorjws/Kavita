using System.Text;
using Kavita.Services.Helpers;

namespace Kavita.Services.Tests.Helpers;

public class JxlHeaderReaderTests
{
    private static readonly string TestDirectory = Path.Join(Directory.GetCurrentDirectory(), "../../../Test Data/JxlHeaderReader");

    /// <summary>
    /// Writes bits LSB-first, the way the JPEG XL codestream is packed
    /// </summary>
    private class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _bitCount;

        public BitWriter Write(uint value, int bits)
        {
            for (var i = 0; i < bits; i++)
            {
                if (_bitCount % 8 == 0) _bytes.Add(0);
                if (((value >> i) & 1) == 1) _bytes[^1] |= (byte) (1 << (_bitCount % 8));
                _bitCount++;
            }
            return this;
        }

        public byte[] ToCodestream(bool trailingPadding = true)
        {
            var result = new List<byte> {0xFF, 0x0A};
            result.AddRange(_bytes);
            // Real files always have more data after the header; pad so truncation tests are about the header
            if (trailingPadding) result.AddRange(new byte[8]);
            return result.ToArray();
        }
    }

    private static readonly uint[] SelectorBits = [9, 13, 18, 30];

    private static BitWriter WriteU32Size(BitWriter w, uint sizeMinusOne)
    {
        var selector = SelectorBits.ToList().FindIndex(b => sizeMinusOne < (1u << (int) b));
        return w.Write((uint) selector, 2).Write(sizeMinusOne, (int) SelectorBits[selector]);
    }

    /// <summary>
    /// A codestream with an explicit width/height (ratio = 0) and default metadata
    /// </summary>
    private static byte[] Codestream(uint width, uint height, int orientation = 1)
    {
        var w = new BitWriter();
        w.Write(0, 1); // div8
        WriteU32Size(w, height - 1);
        w.Write(0, 3); // ratio
        WriteU32Size(w, width - 1);
        WriteMetadata(w, orientation);
        return w.ToCodestream();
    }

    private static void WriteMetadata(BitWriter w, int orientation)
    {
        if (orientation == 1)
        {
            w.Write(1, 1); // all_default
            return;
        }
        w.Write(0, 1); // all_default
        w.Write(1, 1); // extra_fields
        w.Write((uint) (orientation - 1), 3);
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var size = 8 + payload.Length;
        var result = new List<byte>();
        result.AddRange(Enumerable.Reverse(BitConverter.GetBytes(size)));
        result.AddRange(Encoding.ASCII.GetBytes(type));
        result.AddRange(payload);
        return result.ToArray();
    }

    private static readonly byte[] SignatureBox = [0, 0, 0, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    private static (bool ok, int width, int height) Read(byte[] data)
    {
        using var stream = new MemoryStream(data);
        var ok = JxlHeaderReader.TryGetDimensions(stream, out var width, out var height);
        return (ok, width, height);
    }

    #region Fixtures

    [Theory]
    [InlineData("bare-64x48.jxl", 64, 48)] // div8 + 4:3 ratio
    [InlineData("bare-37x91.jxl", 37, 91)] // not div8, explicit width
    [InlineData("container-100x75.jxl", 100, 75)] // ISOBMFF container
    [InlineData("page-16x16.jxl", 16, 16)]
    public void TryGetDimensions_Fixtures(string file, int expectedWidth, int expectedHeight)
    {
        var path = Path.Join(TestDirectory, file);

        Assert.True(JxlHeaderReader.TryGetDimensions(path, out var width, out var height));
        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);

        var (ok, streamWidth, streamHeight) = Read(File.ReadAllBytes(path));
        Assert.True(ok);
        Assert.Equal(expectedWidth, streamWidth);
        Assert.Equal(expectedHeight, streamHeight);
    }

    #endregion

    #region Crafted size headers

    [Fact]
    public void TryGetDimensions_Div8_ExplicitWidth()
    {
        var w = new BitWriter();
        w.Write(1, 1); // div8
        w.Write(4, 5); // height = (4 + 1) * 8
        w.Write(0, 3); // ratio
        w.Write(9, 5); // width = (9 + 1) * 8
        WriteMetadata(w, 1);

        var (ok, width, height) = Read(w.ToCodestream());

        Assert.True(ok);
        Assert.Equal(80, width);
        Assert.Equal(40, height);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 120)]
    [InlineData(3, 133)]
    [InlineData(4, 150)]
    [InlineData(5, 177)]
    [InlineData(6, 125)]
    [InlineData(7, 200)]
    public void TryGetDimensions_FixedRatios(uint ratio, int expectedWidth)
    {
        var w = new BitWriter();
        w.Write(0, 1); // div8
        WriteU32Size(w, 99); // height 100
        w.Write(ratio, 3);
        WriteMetadata(w, 1);

        var (ok, width, height) = Read(w.ToCodestream());

        Assert.True(ok);
        Assert.Equal(expectedWidth, width);
        Assert.Equal(100, height);
    }

    [Theory]
    [InlineData(300, 400)] // 9 bit selector
    [InlineData(5000, 3000)] // 13 bit selector
    [InlineData(100000, 70000)] // 18 bit selector
    public void TryGetDimensions_LargeSizes(uint width, uint height)
    {
        var (ok, w, h) = Read(Codestream(width, height));

        Assert.True(ok);
        Assert.Equal((int) width, w);
        Assert.Equal((int) height, h);
    }

    #endregion

    #region Orientation

    [Theory]
    [InlineData(1, 300, 200)]
    [InlineData(2, 300, 200)]
    [InlineData(3, 300, 200)]
    [InlineData(4, 300, 200)]
    [InlineData(5, 200, 300)]
    [InlineData(6, 200, 300)]
    [InlineData(7, 200, 300)]
    [InlineData(8, 200, 300)]
    public void TryGetDimensions_OrientationSwapsDimensions(int orientation, int expectedWidth, int expectedHeight)
    {
        var (ok, width, height) = Read(Codestream(300, 200, orientation));

        Assert.True(ok);
        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    [Fact]
    public void TryGetDimensions_NonDefaultMetadataWithoutExtraFields_IsNotRotated()
    {
        var w = new BitWriter();
        w.Write(0, 1);
        WriteU32Size(w, 199);
        w.Write(0, 3);
        WriteU32Size(w, 299);
        w.Write(0, 1); // all_default = false
        w.Write(0, 1); // extra_fields = false

        var (ok, width, height) = Read(w.ToCodestream());

        Assert.True(ok);
        Assert.Equal(300, width);
        Assert.Equal(200, height);
    }

    #endregion

    #region Container

    [Fact]
    public void TryGetDimensions_Container_PartialCodestreamBoxes()
    {
        var codestream = Codestream(123, 45);
        var jxlp = new List<byte> {0, 0, 0, 0}; // partial index
        jxlp.AddRange(codestream);

        var data = new List<byte>();
        data.AddRange(SignatureBox);
        data.AddRange(Box("ftyp", Encoding.ASCII.GetBytes("jxl \0\0\0\0jxl ")));
        data.AddRange(Box("jxlp", jxlp.ToArray()));

        var (ok, width, height) = Read(data.ToArray());

        Assert.True(ok);
        Assert.Equal(123, width);
        Assert.Equal(45, height);
    }

    [Fact]
    public void TryGetDimensions_Container_SkipsUnrelatedBoxes()
    {
        var data = new List<byte>();
        data.AddRange(SignatureBox);
        data.AddRange(Box("Exif", new byte[32]));
        data.AddRange(Box("jxlc", Codestream(64, 32)));

        var (ok, width, height) = Read(data.ToArray());

        Assert.True(ok);
        Assert.Equal(64, width);
        Assert.Equal(32, height);
    }

    [Fact]
    public void TryGetDimensions_Container_SizeZeroBoxExtendsToEndOfFile()
    {
        var data = new List<byte>();
        data.AddRange(SignatureBox);
        data.AddRange([0, 0, 0, 0]); // size 0 = rest of file
        data.AddRange(Encoding.ASCII.GetBytes("jxlc"));
        data.AddRange(Codestream(64, 32));

        var (ok, width, height) = Read(data.ToArray());

        Assert.True(ok);
        Assert.Equal(64, width);
        Assert.Equal(32, height);
    }

    [Fact]
    public void TryGetDimensions_Container_WithoutCodestreamBox_ReturnsFalse()
    {
        var data = new List<byte>();
        data.AddRange(SignatureBox);
        data.AddRange(Box("ftyp", Encoding.ASCII.GetBytes("jxl \0\0\0\0jxl ")));
        data.AddRange(Box("Exif", new byte[32]));

        Assert.False(Read(data.ToArray()).ok);
    }

    #endregion

    #region Invalid input

    [Fact]
    public void TryGetDimensions_Empty_ReturnsFalse()
    {
        Assert.False(Read([]).ok);
    }

    [Fact]
    public void TryGetDimensions_Garbage_ReturnsFalse()
    {
        Assert.False(Read(Encoding.ASCII.GetBytes("this is definitely not a jpeg xl file")).ok);
    }

    [Fact]
    public void TryGetDimensions_Png_ReturnsFalse()
    {
        Assert.False(JxlHeaderReader.TryGetDimensions(Path.Join(TestDirectory, "page-16x16.png"), out _, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)] // signature only
    [InlineData(3)]
    [InlineData(4)]
    public void TryGetDimensions_TruncatedCodestream_ReturnsFalse(int length)
    {
        var data = File.ReadAllBytes(Path.Join(TestDirectory, "bare-37x91.jxl"));

        Assert.False(Read(data.Take(length).ToArray()).ok);
    }

    [Fact]
    public void TryGetDimensions_TruncatedContainer_ReturnsFalse()
    {
        var data = File.ReadAllBytes(Path.Join(TestDirectory, "container-100x75.jxl"));

        Assert.False(Read(data.Take(20).ToArray()).ok);
    }

    [Fact]
    public void TryGetDimensions_MissingFile_ReturnsFalse()
    {
        Assert.False(JxlHeaderReader.TryGetDimensions(Path.Join(TestDirectory, "does-not-exist.jxl"), out var width, out var height));
        Assert.Equal(0, width);
        Assert.Equal(0, height);
    }

    #endregion
}
