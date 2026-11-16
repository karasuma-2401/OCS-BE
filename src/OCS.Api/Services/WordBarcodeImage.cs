using System.Buffers.Binary;
using System.IO.Compression;
using ZXing.OneD;

namespace OCS.Api.Services;

/// <summary>Tạo ảnh PNG Code 128 đen trắng với khoảng trắng hai đầu.</summary>
public static class WordBarcodeImage
{
    public static byte[] Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || value.Any(c => c < 32 || c > 126))
            throw new ArgumentException("Barcode không hỗ trợ Code 128.", nameof(value));
        var bars = new Code128Writer().encode(value);
        const int scale = 3, quiet = 12, height = 150;
        var width = (bars.Length + quiet * 2) * scale;
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < width; x++)
            {
                var index = x / scale - quiet;
                raw.WriteByte(index >= 0 && index < bars.Length && bars[index] ? (byte)0 : (byte)255);
            }
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true)) zlib.Write(raw.ToArray());
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        WriteChunk(output, "IHDR"u8, header);
        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length); output.Write(number);
        output.Write(type); output.Write(data);
        uint crc = 0xffffffff;
        foreach (var value in type) crc = Update(crc, value);
        foreach (var value in data) crc = Update(crc, value);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
    }

    private static uint Update(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++) crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
        return crc;
    }
}
