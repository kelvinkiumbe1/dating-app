using System.Buffers.Binary;
using System.Text;

namespace DatingApp.Api.Storage;

public class InvalidImageException(string message) : Exception(message);

/// Removes metadata from uploaded photos without re-encoding them. Phone cameras embed GPS
/// coordinates, device serials, and timestamps in EXIF/XMP; on a dating app that can put a
/// stranger at someone's front door. Pixel data is copied byte-for-byte.
///
/// What survives: anything needed to display the image correctly (JPEG quantization and
/// Huffman tables, ICC colour profiles, the Adobe colour-transform marker, PNG palette and
/// gamma chunks) and the EXIF orientation, re-emitted on its own so portrait photos taken
/// on a phone don't render sideways. Bytes after the end-of-image marker are dropped too.
public static class ImageMetadata
{
    public static byte[] Strip(ReadOnlySpan<byte> data, string contentType) => contentType switch
    {
        "image/jpeg" => StripJpeg(data),
        "image/png" => StripPng(data),
        "image/webp" => StripWebp(data),
        _ => throw new InvalidImageException($"Unsupported image type {contentType}."),
    };

    // ── JPEG ────────────────────────────────────────────────────────────────

    private static byte[] StripJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            throw new InvalidImageException("Not a JPEG.");

        var header = new List<(byte Marker, byte[] Segment)>();
        ushort orientation = 1;
        var pos = 2;

        // Header segments, up to the first start-of-scan.
        while (true)
        {
            var (marker, start) = ReadMarker(data, ref pos);
            if (marker == 0xD9) throw new InvalidImageException("JPEG ended before any image data.");

            var length = ReadSegmentLength(data, pos);
            var segment = data.Slice(start, pos + length - start);
            var payload = data.Slice(pos + 2, length - 2);
            pos += length;

            if (marker == 0xDA)
            {
                header.Add((marker, segment.ToArray()));
                break;
            }

            if (marker == 0xE1 && payload.StartsWith("Exif\0\0"u8))
                orientation = ReadExifOrientation(payload[6..]);

            if (KeepJpegSegment(marker, payload))
                header.Add((marker, segment.ToArray()));
        }

        using var output = new MemoryStream(data.Length);
        output.Write([0xFF, 0xD8]);

        // Re-emit orientation after any leading JFIF APP0, where readers expect EXIF.
        var insertAt = header.TakeWhile(s => s.Marker == 0xE0).Count();
        for (var i = 0; i < header.Count; i++)
        {
            if (i == insertAt && orientation is >= 2 and <= 8) output.Write(OrientationOnlyExif(orientation));
            output.Write(header[i].Segment);
        }

        // Entropy-coded scan data. Inside it 0xFF is always followed by 0x00 (stuffing) or a
        // restart marker, so any other 0xFF xx is a real marker: more tables and scans in a
        // progressive JPEG, or the end of the image.
        while (pos < data.Length)
        {
            var next = FindMarker(data, pos);
            if (next < 0)
            {
                // No end-of-image marker. Browsers render these and some cameras write them,
                // so keep the scan data and close the file properly.
                output.Write(data[pos..]);
                output.Write([0xFF, 0xD9]);
                return output.ToArray();
            }
            output.Write(data[pos..next]);
            pos = next;

            var (marker, start) = ReadMarker(data, ref pos);
            if (marker == 0xD9)
            {
                output.Write([0xFF, 0xD9]);
                return output.ToArray();   // anything appended after EOI is discarded
            }

            var length = ReadSegmentLength(data, pos);
            var payload = data.Slice(pos + 2, length - 2);
            var segment = data.Slice(start, pos + length - start);
            pos += length;

            if (KeepJpegSegment(marker, payload)) output.Write(segment);
        }

        output.Write([0xFF, 0xD9]);
        return output.ToArray();
    }

    private static bool KeepJpegSegment(byte marker, ReadOnlySpan<byte> payload) => marker switch
    {
        0xE0 => true,                                         // JFIF / JFXX
        0xE2 => payload.StartsWith("ICC_PROFILE\0"u8),        // colour profile; drops FlashPix/MPF
        0xEE => true,                                         // Adobe: governs colour transform
        >= 0xE1 and <= 0xEF => false,                         // EXIF, XMP, IPTC, vendor blobs
        0xFE => false,                                        // comments
        _ => true,                                            // tables, frame, scan, restart interval
    };

    /// Reads "FF xx" (skipping fill bytes) and returns the marker and where it began.
    private static (byte Marker, int Start) ReadMarker(ReadOnlySpan<byte> data, ref int pos)
    {
        if (pos >= data.Length || data[pos] != 0xFF) throw new InvalidImageException("Malformed JPEG marker.");
        var start = pos;
        while (pos < data.Length && data[pos] == 0xFF) pos++;
        if (pos >= data.Length) throw new InvalidImageException("JPEG is truncated.");
        return (data[pos++], start);
    }

    private static int ReadSegmentLength(ReadOnlySpan<byte> data, int pos)
    {
        if (pos + 2 > data.Length) throw new InvalidImageException("JPEG is truncated.");
        int length = BinaryPrimitives.ReadUInt16BigEndian(data[pos..]);
        if (length < 2 || pos + length > data.Length) throw new InvalidImageException("Malformed JPEG segment.");
        return length;
    }

    private static int FindMarker(ReadOnlySpan<byte> data, int from)
    {
        for (var i = from; i + 1 < data.Length; i++)
        {
            if (data[i] != 0xFF) continue;
            var next = data[i + 1];
            if (next == 0x00 || next is >= 0xD0 and <= 0xD7 || next == 0xFF) continue;
            return i;
        }
        return -1;
    }

    /// Orientation (tag 0x0112) from IFD0 of a TIFF-structured EXIF block. Any malformation
    /// yields 1, "as stored", since a wrong guess is worse than no rotation.
    private static ushort ReadExifOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8) return 1;
        bool little;
        if (tiff.StartsWith("II"u8)) little = true;
        else if (tiff.StartsWith("MM"u8)) little = false;
        else return 1;

        var ifd = U32(tiff, 4, little);
        if (ifd > (uint)tiff.Length - 2) return 1;
        var count = U16(tiff, (int)ifd, little);
        for (var i = 0; i < count; i++)
        {
            var entry = (int)ifd + 2 + i * 12;
            if (entry + 12 > tiff.Length) return 1;
            if (U16(tiff, entry, little) == 0x0112 && U16(tiff, entry + 2, little) == 3)
            {
                var value = U16(tiff, entry + 8, little);
                return value is >= 1 and <= 8 ? value : (ushort)1;
            }
        }
        return 1;
    }

    private static ushort U16(ReadOnlySpan<byte> b, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(b[at..]) : BinaryPrimitives.ReadUInt16BigEndian(b[at..]);

    private static uint U32(ReadOnlySpan<byte> b, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(b[at..]) : BinaryPrimitives.ReadUInt32BigEndian(b[at..]);

    /// An APP1 segment whose only content is the orientation tag.
    private static byte[] OrientationOnlyExif(ushort orientation)
    {
        var tiff = new byte[]
        {
            (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,   // big-endian, IFD0 at 8
            0x00, 0x01,                                                // one entry
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01,            // Orientation, SHORT, count 1
            (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,                                    // no next IFD
        };
        var length = 2 + 6 + tiff.Length;
        return [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. "Exif\0\0"u8, .. tiff];
    }

    // ── PNG ─────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> PngMetadataChunks = ["eXIf", "tEXt", "zTXt", "iTXt", "tIME"];

    private static byte[] StripPng(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (!data.StartsWith(signature)) throw new InvalidImageException("Not a PNG.");

        using var output = new MemoryStream(data.Length);
        output.Write(signature);

        var pos = signature.Length;
        while (pos + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[pos..]);
            if (length > int.MaxValue || pos + 12 + (long)length > data.Length)
                throw new InvalidImageException("Malformed PNG chunk.");

            var type = Encoding.ASCII.GetString(data.Slice(pos + 4, 4));
            var total = 12 + (int)length;   // length + type + data + CRC

            if (!PngMetadataChunks.Contains(type)) output.Write(data.Slice(pos, total));
            pos += total;

            if (type == "IEND") return output.ToArray();
        }

        throw new InvalidImageException("PNG is truncated.");
    }

    // ── WebP ────────────────────────────────────────────────────────────────

    private static byte[] StripWebp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data.StartsWith("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WEBP"u8))
            throw new InvalidImageException("Not a WebP image.");

        var riffEnd = 8 + (long)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if (riffEnd > data.Length) throw new InvalidImageException("WebP is truncated.");

        using var body = new MemoryStream(data.Length);
        body.Write("WEBP"u8);

        var pos = 12;
        while (pos + 8 <= riffEnd)
        {
            var fourcc = Encoding.ASCII.GetString(data.Slice(pos, 4));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 4)..]);
            var padded = 8 + (long)size + (size & 1);
            if (pos + 8 + (long)size > riffEnd) throw new InvalidImageException("Malformed WebP chunk.");
            padded = Math.Min(padded, riffEnd - pos);

            var chunk = data.Slice(pos, (int)padded).ToArray();
            pos += (int)padded;

            if (fourcc is "EXIF" or "XMP ") continue;
            if (fourcc == "VP8X" && size >= 1)
                chunk[8] &= unchecked((byte)~(0x08 | 0x04));   // clear the EXIF and XMP presence flags

            body.Write(chunk);
        }

        var payload = body.ToArray();
        var output = new byte[8 + payload.Length];
        "RIFF"u8.CopyTo(output);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(output, 8);
        return output;
    }
}
