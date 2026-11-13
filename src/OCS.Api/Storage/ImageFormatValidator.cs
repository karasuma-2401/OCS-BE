using System.Buffers.Binary;
using System.IO.Compression;

namespace OCS.Api.Storage;

public static class ImageFormatValidator
{
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] RiffHeader = [0x52, 0x49, 0x46, 0x46];
    private static readonly byte[] WebpHeader = [0x57, 0x45, 0x42, 0x50];

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private const int MaxDimension = 6000;
    private const long MaxTotalPixels = 24_000_000;
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private static readonly uint[] CrcTable = InitializeCrcTable();

    private static uint[] InitializeCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
            {
                if ((c & 1) != 0)
                {
                    c = 0xEDB88320u ^ (c >> 1);
                }
                else
                {
                    c >>= 1;
                }
            }
            table[i] = c;
        }
        return table;
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            crc = CrcTable[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    public static bool IsAllowedExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && AllowedExtensions.Contains(ext);
    }

    public static bool TryDetectImageFormat(
        Stream stream,
        string fileName,
        out string detectedMimeType,
        out string? validationError)
    {
        detectedMimeType = string.Empty;
        validationError = null;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
        {
            validationError = $"Định dạng tệp '{fileName}' không được hỗ trợ. Chỉ chấp nhận: .jpg, .jpeg, .png, .webp.";
            return false;
        }

        if (stream.Length < 32)
        {
            validationError = $"Tệp '{fileName}' có dung lượng quá nhỏ ({stream.Length} bytes), không phải là tệp ảnh hợp lệ.";
            return false;
        }

        if (stream.Length > MaxUploadBytes)
        {
            validationError = $"Tệp '{fileName}' có dung lượng vượt quá giới hạn 10MB.";
            return false;
        }

        var originalPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var header = new byte[12];
            var read = stream.Read(header, 0, header.Length);
            if (read < 12)
            {
                validationError = $"Không thể đọc phần tiêu đề của tệp '{fileName}'.";
                return false;
            }

            if (header[0] == JpegHeader[0] && header[1] == JpegHeader[1] && header[2] == JpegHeader[2])
            {
                if (ext is not (".jpg" or ".jpeg"))
                {
                    validationError = $"Tệp '{fileName}' có nội dung là ảnh JPEG nhưng đuôi file là '{ext}'.";
                    return false;
                }

                if (!ValidateJpegStructure(stream, out var width, out var height, out var jpegError))
                {
                    validationError = $"Tệp ảnh JPEG '{fileName}' bị lỗi cấu trúc hoặc hỏng dữ liệu: {jpegError}";
                    return false;
                }

                detectedMimeType = "image/jpeg";
                return true;
            }

            if (header.AsSpan(0, 8).SequenceEqual(PngHeader))
            {
                if (ext is not ".png")
                {
                    validationError = $"Tệp '{fileName}' có nội dung là ảnh PNG nhưng đuôi file là '{ext}'.";
                    return false;
                }

                if (!ValidatePngStructure(stream, out var width, out var height, out var pngError))
                {
                    validationError = $"Tệp ảnh PNG '{fileName}' bị lỗi cấu trúc hoặc hỏng dữ liệu: {pngError}";
                    return false;
                }

                detectedMimeType = "image/png";
                return true;
            }

            if (header.AsSpan(0, 4).SequenceEqual(RiffHeader) && header.AsSpan(8, 4).SequenceEqual(WebpHeader))
            {
                if (ext is not ".webp")
                {
                    validationError = $"Tệp '{fileName}' có nội dung là ảnh WebP nhưng đuôi file là '{ext}'.";
                    return false;
                }

                if (!ValidateWebpStructure(stream, out var width, out var height, out var webpError))
                {
                    validationError = $"Tệp ảnh WebP '{fileName}' bị lỗi cấu trúc hoặc hỏng dữ liệu: {webpError}";
                    return false;
                }

                detectedMimeType = "image/webp";
                return true;
            }

            validationError = $"Tệp '{fileName}' không phải là định dạng hình ảnh hợp lệ (chữ ký tệp không khớp).";
            return false;
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }
        }
    }

    private static bool CheckDimensions(int width, int height, string format, out string? error)
    {
        error = null;
        if (width <= 0 || height <= 0)
        {
            error = $"Kích thước ảnh {format} không hợp lệ ({width}x{height}).";
            return false;
        }

        if (width > MaxDimension || height > MaxDimension)
        {
            error = $"Kích thước một chiều của ảnh {format} ({width}x{height}) vượt quá giới hạn tối đa cho phép ({MaxDimension}px).";
            return false;
        }

        var totalPixels = (long)width * height;
        if (totalPixels > MaxTotalPixels)
        {
            error = $"Tổng số điểm ảnh ({width}x{height} = {totalPixels:N0} pixels) vượt quá giới hạn an toàn tối đa cho phép ({MaxTotalPixels:N0} pixels).";
            return false;
        }

        return true;
    }

    private static bool ValidateJpegStructure(Stream stream, out int width, out int height, out string? error)
    {
        width = 0;
        height = 0;
        error = null;

        stream.Position = 2;
        var foundSof = false;
        var foundSos = false;
        var foundEoi = false;

        var buf2 = new byte[2];

        while (stream.Position < stream.Length - 1)
        {
            var b = stream.ReadByte();
            if (b == -1) break;

            if (b != 0xFF) continue;

            var marker = stream.ReadByte();
            while (marker == 0xFF)
            {
                marker = stream.ReadByte();
            }

            if (marker == -1) break;

            if (marker is 0x00 or 0xFF or >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (marker == 0xD8)
            {
                continue;
            }

            if (marker == 0xD9)
            {
                foundEoi = true;
                break;
            }

            if (stream.Read(buf2, 0, 2) < 2)
            {
                break;
            }

            var length = (buf2[0] << 8) | buf2[1];
            if (length < 2)
            {
                error = "Độ dài đoạn đánh dấu JPEG không hợp lệ.";
                return false;
            }

            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                if (length < 7)
                {
                    error = "Đoạn SOF của JPEG bị cắt ngắn.";
                    return false;
                }

                var sofData = new byte[6];
                if (stream.Read(sofData, 0, 6) < 6)
                {
                    error = "Không thể đọc dữ liệu kích thước JPEG SOF.";
                    return false;
                }

                height = (sofData[1] << 8) | sofData[2];
                width = (sofData[3] << 8) | sofData[4];

                if (!CheckDimensions(width, height, "JPEG", out error))
                {
                    return false;
                }

                foundSof = true;
                stream.Seek(length - 2 - 6, SeekOrigin.Current);
                continue;
            }

            if (marker == 0xDA)
            {
                foundSos = true;
                stream.Seek(length - 2, SeekOrigin.Current);

                while (stream.Position < stream.Length - 1)
                {
                    var sb = stream.ReadByte();
                    if (sb == -1) break;
                    if (sb == 0xFF)
                    {
                        var nextM = stream.ReadByte();
                        while (nextM == 0xFF) nextM = stream.ReadByte();
                        if (nextM == 0xD9)
                        {
                            foundEoi = true;
                            break;
                        }
                    }
                }
                break;
            }

            var toSkip = length - 2;
            if (stream.Position + toSkip > stream.Length)
            {
                error = "Đoạn marker JPEG vượt quá độ dài tệp tin.";
                return false;
            }

            stream.Seek(toSkip, SeekOrigin.Current);
        }

        if (!foundSof)
        {
            error = "Tệp ảnh JPEG không chứa phân đoạn SOF hợp lệ để xác định kích thước.";
            return false;
        }

        if (!foundSos)
        {
            error = "Tệp ảnh JPEG thiếu phân đoạn bắt đầu quét ảnh (SOS 0xFFDA).";
            return false;
        }

        if (!foundEoi)
        {
            error = "Tệp ảnh JPEG bị cắt ngắn hoặc thiếu marker kết thúc ảnh hợp lệ (EOI 0xFFD9).";
            return false;
        }

        return true;
    }

    private static bool ValidatePngStructure(Stream stream, out int width, out int height, out string? error)
    {
        width = 0;
        height = 0;
        error = null;

        stream.Position = 8;
        var hasIhdr = false;
        var hasIdat = false;
        var hasIend = false;

        var buf4 = new byte[4];
        var typeBuf = new byte[4];
        var crcBuf = new byte[4];

        using var idatCombinedStream = new MemoryStream();

        while (stream.Position + 8 <= stream.Length)
        {
            if (stream.Read(buf4, 0, 4) < 4) break;
            var chunkLen = (buf4[0] << 24) | (buf4[1] << 16) | (buf4[2] << 8) | buf4[3];
            var remainingStreamBytes = stream.Length - stream.Position;

            if (chunkLen < 0 || chunkLen > MaxUploadBytes || chunkLen + 4 > remainingStreamBytes)
            {
                error = "Kích thước chunk PNG không hợp lệ hoặc vượt quá dữ liệu còn lại của tệp.";
                return false;
            }

            if (stream.Read(typeBuf, 0, 4) < 4) break;
            var chunkType = System.Text.Encoding.ASCII.GetString(typeBuf);

            var chunkData = new byte[chunkLen];
            var bytesRead = 0;
            while (bytesRead < chunkLen)
            {
                var r = stream.Read(chunkData, bytesRead, chunkLen - bytesRead);
                if (r <= 0) break;
                bytesRead += r;
            }

            if (bytesRead < chunkLen)
            {
                error = $"Dữ liệu chunk {chunkType} bị cắt ngắn.";
                return false;
            }

            if (stream.Read(crcBuf, 0, 4) < 4)
            {
                error = $"Không thể đọc mã CRC của chunk {chunkType}.";
                return false;
            }

            var expectedCrc = (uint)((crcBuf[0] << 24) | (crcBuf[1] << 16) | (crcBuf[2] << 8) | crcBuf[3]);
            var computedCrc = UpdateCrc(0xFFFFFFFFu, typeBuf);
            computedCrc = UpdateCrc(computedCrc, chunkData) ^ 0xFFFFFFFFu;

            if (computedCrc != expectedCrc)
            {
                error = $"Mã kiểm tra CRC của chunk PNG {chunkType} không khớp (dữ liệu ảnh bị hỏng).";
                return false;
            }

            if (!hasIhdr)
            {
                if (chunkType != "IHDR" || chunkLen != 13)
                {
                    error = "Chunk đầu tiên của PNG phải là IHDR hợp lệ.";
                    return false;
                }

                width = (chunkData[0] << 24) | (chunkData[1] << 16) | (chunkData[2] << 8) | chunkData[3];
                height = (chunkData[4] << 24) | (chunkData[5] << 16) | (chunkData[6] << 8) | chunkData[7];

                if (!CheckDimensions(width, height, "PNG", out error))
                {
                    return false;
                }

                hasIhdr = true;
                continue;
            }

            if (chunkType == "IDAT")
            {
                if (idatCombinedStream.Length + chunkLen > MaxUploadBytes)
                {
                    error = "Tổng dung lượng IDAT của tệp PNG vượt quá giới hạn an toàn 10MB.";
                    return false;
                }

                hasIdat = true;
                idatCombinedStream.Write(chunkData, 0, chunkLen);
            }
            else if (chunkType == "IEND")
            {
                hasIend = true;
                break;
            }
        }

        if (!hasIhdr || !hasIdat || !hasIend)
        {
            error = "Tệp PNG bị thiếu IHDR, IDAT hoặc kết thúc IEND.";
            return false;
        }

        idatCombinedStream.Position = 0;
        try
        {
            using var zlib = new ZLibStream(idatCombinedStream, CompressionMode.Decompress);
            var verifyBuffer = new byte[8192];
            long totalDecompressed = 0;
            var maxDecompressedBytes = Math.Min((long)width * height * 4 + height + 65536, 100 * 1024 * 1024);
            int r;
            while ((r = zlib.Read(verifyBuffer, 0, verifyBuffer.Length)) > 0)
            {
                totalDecompressed += r;
                if (totalDecompressed > maxDecompressedBytes)
                {
                    error = "Dữ liệu giải nén của ảnh PNG vượt quá giới hạn an toàn tối đa cho phép.";
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            error = $"Dữ liệu nén IDAT của ảnh PNG không thể giải mã: {ex.Message}";
            return false;
        }

        return true;
    }

    private static bool ValidateWebpStructure(Stream stream, out int width, out int height, out string? error)
    {
        width = 0;
        height = 0;
        error = null;

        var streamLength = stream.Length;
        if (streamLength < 30)
        {
            error = "Tệp WebP quá ngắn.";
            return false;
        }

        stream.Position = 4;
        var buf4 = new byte[4];
        stream.Read(buf4, 0, 4);
        var riffSize = (long)BitConverter.ToUInt32(buf4, 0);

        if (riffSize + 8 > streamLength + 2)
        {
            error = "Kích thước khai báo trong RIFF vượt quá dung lượng tệp.";
            return false;
        }

        stream.Position = 12;
        var foundValidImageBitstream = false;

        while (stream.Position + 8 <= streamLength)
        {
            if (stream.Read(buf4, 0, 4) < 4) break;
            var chunkType = System.Text.Encoding.ASCII.GetString(buf4);

            if (stream.Read(buf4, 0, 4) < 4) break;
            var chunkLen = (long)BitConverter.ToUInt32(buf4, 0);

            if (chunkLen < 0 || stream.Position + chunkLen > streamLength)
            {
                error = $"Chunk {chunkType} trong WebP khai báo kích thước vượt quá dung lượng tệp.";
                return false;
            }

            var chunkDataStart = stream.Position;

            if (chunkType == "VP8 ")
            {
                var chunkBytes = new byte[chunkLen];
                var readTotal = 0;
                while (readTotal < chunkLen)
                {
                    var r = stream.Read(chunkBytes, readTotal, (int)(chunkLen - readTotal));
                    if (r <= 0) break;
                    readTotal += r;
                }

                if (ValidateVp8Bitstream(chunkBytes, out width, out height, out error))
                {
                    foundValidImageBitstream = true;
                }
                else
                {
                    return false;
                }
            }
            else if (chunkType == "VP8L")
            {
                var chunkBytes = new byte[chunkLen];
                var readTotal = 0;
                while (readTotal < chunkLen)
                {
                    var r = stream.Read(chunkBytes, readTotal, (int)(chunkLen - readTotal));
                    if (r <= 0) break;
                    readTotal += r;
                }

                if (ValidateVp8LBitstream(chunkBytes, out width, out height, out error))
                {
                    foundValidImageBitstream = true;
                }
                else
                {
                    return false;
                }
            }
            else if (chunkType == "VP8X")
            {
                if (chunkLen < 10)
                {
                    error = "Chunk VP8X quá ngắn.";
                    return false;
                }

                var b = new byte[10];
                if (stream.Read(b, 0, 10) == 10)
                {
                    width = 1 + (b[4] | (b[5] << 8) | (b[6] << 16));
                    height = 1 + (b[7] | (b[8] << 8) | (b[9] << 16));
                    if (!CheckDimensions(width, height, "WebP (VP8X)", out error))
                    {
                        return false;
                    }
                }
            }

            var nextPos = chunkDataStart + chunkLen + (chunkLen % 2 != 0 ? 1 : 0);
            if (nextPos > streamLength) break;
            stream.Position = nextPos;
        }

        if (!foundValidImageBitstream)
        {
            error = "Không tìm thấy dữ liệu ảnh giải mã được (VP8/VP8L) trong tệp WebP.";
            return false;
        }

        return true;
    }

    private static bool ValidateVp8Bitstream(byte[] data, out int width, out int height, out string? error)
    {
        width = 0;
        height = 0;
        error = null;

        if (data.Length < 10)
        {
            error = "Dữ liệu chunk VP8 quá ngắn.";
            return false;
        }

        var isKeyFrame = (data[0] & 0x01) == 0;
        if (!isKeyFrame)
        {
            error = "Ảnh WebP (VP8) phải là keyframe.";
            return false;
        }

        var showFrame = (data[0] & 0x10) != 0;
        if (!showFrame)
        {
            error = "Header VP8 không hợp lệ.";
            return false;
        }

        var firstPartSize = (data[0] >> 5) | (data[1] << 3) | (data[2] << 11);
        if (firstPartSize <= 0 || firstPartSize > data.Length - 10)
        {
            error = "Kích thước phân vùng đầu tiên của VP8 không hợp lệ.";
            return false;
        }

        if (data[3] != 0x9D || data[4] != 0x01 || data[5] != 0x2A)
        {
            error = "Mã nhận dạng VP8 không hợp lệ (thiếu start code 0x9D012A).";
            return false;
        }

        width = (data[6] | (data[7] << 8)) & 0x3FFF;
        height = (data[8] | (data[9] << 8)) & 0x3FFF;

        if (!CheckDimensions(width, height, "WebP (VP8)", out error))
        {
            return false;
        }

        var partData = new ReadOnlySpan<byte>(data, 10, firstPartSize);
        if (!DecodeVp8Header(partData, out error))
        {
            return false;
        }

        return true;
    }

    private static bool DecodeVp8Header(ReadOnlySpan<byte> partData, out string? error)
    {
        error = null;
        if (partData.Length < 3)
        {
            error = "Phân vùng đầu tiên của VP8 quá ngắn.";
            return false;
        }

        var dec = new Vp8BoolDecoder(partData);
        var colorSpace = dec.ReadBit(128);
        if (colorSpace != 0)
        {
            error = "Không gian màu VP8 không được hỗ trợ.";
            return false;
        }

        var clampingType = dec.ReadBit(128);
        if (clampingType != 0)
        {
            error = "Kiểu clamping VP8 không hợp lệ.";
            return false;
        }

        var segmentation = dec.ReadBit(128);
        if (segmentation == 1)
        {
            var updateMap = dec.ReadBit(128);
            var updateData = dec.ReadBit(128);
            if (updateData == 1)
            {
                dec.ReadBit(128);
                for (var i = 0; i < 4; i++)
                {
                    if (dec.ReadBit(128) == 1) dec.ReadLiteral(8);
                }
                for (var i = 0; i < 4; i++)
                {
                    if (dec.ReadBit(128) == 1) dec.ReadLiteral(7);
                }
            }
            if (updateMap == 1)
            {
                for (var i = 0; i < 3; i++)
                {
                    if (dec.ReadBit(128) == 1) dec.ReadLiteral(8);
                }
            }
        }

        var filterType = dec.ReadBit(128);
        var loopFilterLevel = dec.ReadLiteral(6);
        var sharpnessLevel = dec.ReadLiteral(3);

        var filterDeltaEnabled = dec.ReadBit(128);
        if (filterDeltaEnabled == 1)
        {
            if (dec.ReadBit(128) == 1)
            {
                for (var i = 0; i < 8; i++)
                {
                    if (dec.ReadBit(128) == 1) dec.ReadLiteral(7);
                }
            }
        }

        var numPartitions = 1 << (int)dec.ReadLiteral(2);
        var baseQindex = dec.ReadLiteral(7);

        if (dec.HasError)
        {
            error = "Không thể giải mã cú pháp bitstream VP8 (dữ liệu bị hỏng).";
            return false;
        }

        return true;
    }

    private static bool ValidateVp8LBitstream(byte[] data, out int width, out int height, out string? error)
    {
        width = 0;
        height = 0;
        error = null;

        if (data.Length < 5)
        {
            error = "Dữ liệu chunk VP8L quá ngắn.";
            return false;
        }

        if (data[0] != 0x2F)
        {
            error = "Chữ ký byte VP8L không hợp lệ (phải là 0x2F).";
            return false;
        }

        var reader = new Vp8LBitReader(new ReadOnlySpan<byte>(data, 1, data.Length - 1));
        width = (int)(1 + reader.ReadBits(14));
        height = (int)(1 + reader.ReadBits(14));
        var hasAlpha = reader.ReadBits(1);
        var version = reader.ReadBits(3);

        if (version != 0)
        {
            error = $"Phiên bản VP8L ({version}) không được hỗ trợ (chỉ hỗ trợ phiên bản 0).";
            return false;
        }

        if (!CheckDimensions(width, height, "WebP (VP8L)", out error))
        {
            return false;
        }

        while (reader.ReadBits(1) == 1)
        {
            var transformType = reader.ReadBits(2);
            switch (transformType)
            {
                case 0:
                case 1:
                    reader.ReadBits(3);
                    break;
                case 2:
                    break;
                case 3:
                    var numColors = reader.ReadBits(8) + 1;
                    if (numColors == 0 || numColors > 256)
                    {
                        error = "Bảng màu palette trong VP8L không hợp lệ.";
                        return false;
                    }
                    for (var i = 0; i < numColors; i++)
                    {
                        reader.ReadBits(32);
                    }
                    break;
            }
            if (reader.HasError)
            {
                error = "Lỗi khi đọc phần transform của bitstream VP8L.";
                return false;
            }
        }

        var useColorCache = reader.ReadBits(1);
        if (useColorCache == 1)
        {
            var cacheBits = reader.ReadBits(4);
            if (cacheBits is < 1 or > 11)
            {
                error = "Kích thước color cache trong VP8L không hợp lệ (phải từ 1 đến 11).";
                return false;
            }
        }

        if (reader.HasError)
        {
            error = "Dữ liệu bitstream VP8L bị cắt ngắn hoặc không thể giải mã.";
            return false;
        }

        return true;
    }

    private ref struct Vp8BoolDecoder(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _pos = 0;
        private uint _value = data.Length > 0 ? (uint)(data[0] << 8 | (data.Length > 1 ? data[1] : 0)) : 0;
        private uint _range = 255;
        private int _count = data.Length > 1 ? 0 : 8;

        public bool HasError { get; private set; }

        public uint ReadBit(int prob = 128)
        {
            var split = 1 + (((_range - 1) * (uint)prob) >> 8);
            var bigSplit = split << 8;
            uint bit;

            if (_value >= bigSplit)
            {
                _range -= split;
                _value -= bigSplit;
                bit = 1;
            }
            else
            {
                _range = split;
                bit = 0;
            }

            while (_range < 128)
            {
                _range <<= 1;
                _value <<= 1;
                if (++_count == 8)
                {
                    _count = 0;
                    _pos += (_pos == 0 ? 2 : 1);
                    if (_pos < _data.Length)
                    {
                        _value |= _data[_pos];
                    }
                    else if (_pos > _data.Length + 2)
                    {
                        HasError = true;
                        return 0;
                    }
                }
            }

            return bit;
        }

        public uint ReadLiteral(int bits)
        {
            uint v = 0;
            for (var i = 0; i < bits; i++)
            {
                v = (v << 1) | ReadBit(128);
            }
            return v;
        }
    }

    private ref struct Vp8LBitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _bitPos = 0;

        public bool HasError { get; private set; }

        public uint ReadBits(int count)
        {
            uint val = 0;
            for (var i = 0; i < count; i++)
            {
                var byteIdx = _bitPos >> 3;
                var bitIdx = _bitPos & 7;
                if (byteIdx >= _data.Length)
                {
                    HasError = true;
                    return 0;
                }
                var bit = (uint)((_data[byteIdx] >> bitIdx) & 1);
                val |= (bit << i);
                _bitPos++;
            }
            return val;
        }
    }
}
