using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DatingApp.Api;
using DatingApp.Api.Storage;

namespace DatingApp.Api.Tests;

/// Phone photos carry GPS coordinates in EXIF. These build files by hand with known metadata
/// and check exactly which bytes survive.
public class ImageMetadataTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public ImageMetadataTests(ApiFactory api) => _api = api;

    private static readonly byte[] Secret = "GPS 51.5074N 0.1278W serial#XYZ"u8.ToArray();

    private static byte[] Segment(byte marker, byte[] payload)
    {
        var length = payload.Length + 2;
        return [0xFF, marker, (byte)(length >> 8), (byte)length, .. payload];
    }

    /// Big-endian EXIF with an Orientation entry and an ASCII entry holding the secret.
    private static byte[] Exif(ushort orientation)
    {
        var ascii = Secret;
        var tiff = new List<byte> { (byte)'M', (byte)'M', 0, 0x2A, 0, 0, 0, 8, 0, 2 };
        tiff.AddRange(new byte[] { 0x01, 0x12, 0, 3, 0, 0, 0, 1, (byte)(orientation >> 8), (byte)orientation, 0, 0 });
        var asciiOffset = 8 + 2 + 24 + 4;
        tiff.AddRange(new byte[] { 0x01, 0x0F, 0, 2, 0, 0, 0, (byte)ascii.Length, 0, 0, 0, (byte)asciiOffset });
        tiff.AddRange(new byte[] { 0, 0, 0, 0 });
        tiff.AddRange(ascii);
        return [.. "Exif\0\0"u8, .. tiff];
    }

    private static readonly byte[] Jfif = Segment(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
    private static readonly byte[] Dqt = Segment(0xDB, [0, .. Enumerable.Repeat((byte)1, 64)]);
    private static readonly byte[] Sof = Segment(0xC0, [8, 0, 1, 0, 1, 1, 1, 0x11, 0]);
    private static readonly byte[] Sos = Segment(0xDA, [1, 1, 0, 0, 0x3F, 0]);
    // Scan data including a stuffed 0xFF00 and a restart marker, which must not end the scan.
    private static readonly byte[] Scan = [0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56];
    private static readonly byte[] Eoi = [0xFF, 0xD9];

    private static byte[] Jpeg(ushort orientation) =>
    [
        0xFF, 0xD8,
        .. Jfif,
        .. Segment(0xE1, Exif(orientation)),
        .. Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. Secret]),   // XMP
        .. Segment(0xFE, Secret),                                               // comment
        .. Dqt, .. Sof, .. Sos, .. Scan, .. Eoi,
        .. Secret,                                                              // trailer after EOI
    ];

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    [Fact]
    public void JpegLosesEveryMetadataBlockButKeepsImageDataByteForByte()
    {
        var clean = ImageMetadata.Strip(Jpeg(orientation: 1), "image/jpeg");

        byte[] expected = [0xFF, 0xD8, .. Jfif, .. Dqt, .. Sof, .. Sos, .. Scan, .. Eoi];
        Assert.Equal(expected, clean);
    }

    [Fact]
    public void JpegOrientationSurvivesSoPhonePhotosAreNotSideways()
    {
        var clean = ImageMetadata.Strip(Jpeg(orientation: 6), "image/jpeg");

        Assert.False(Contains(clean, Secret));
        // Re-emitted directly after JFIF as an EXIF block containing only tag 0x0112 = 6.
        var exif = clean.AsSpan(2 + Jfif.Length).ToArray();
        Assert.Equal(new byte[] { 0xFF, 0xE1 }, exif[..2]);
        Assert.True(Contains(exif, [0x01, 0x12, 0x00, 0x03, 0, 0, 0, 1, 0x00, 0x06]));
        Assert.True(clean.AsSpan().EndsWith([.. Scan, .. Eoi]));
    }

    [Fact]
    public void PngTextAndExifChunksAreRemoved()
    {
        static byte[] Chunk(string type, byte[] data)
        {
            var len = data.Length;
            return [(byte)(len >> 24), (byte)(len >> 16), (byte)(len >> 8), (byte)len,
                .. Encoding.ASCII.GetBytes(type), .. data, 0, 0, 0, 0];
        }

        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var ihdr = Chunk("IHDR", new byte[13]);
        var idat = Chunk("IDAT", [1, 2, 3]);
        var iend = Chunk("IEND", []);
        byte[] png = [.. sig, .. ihdr, .. Chunk("tEXt", Secret), .. Chunk("eXIf", Secret), .. idat, .. iend, .. Secret];

        Assert.Equal([.. sig, .. ihdr, .. idat, .. iend], ImageMetadata.Strip(png, "image/png"));
    }

    [Fact]
    public void WebpExifAndXmpChunksAreRemovedAndFlagsCleared()
    {
        static byte[] Chunk(string fourcc, byte[] data)
        {
            var len = data.Length;
            byte[] c = [.. Encoding.ASCII.GetBytes(fourcc), (byte)len, (byte)(len >> 8), (byte)(len >> 16), (byte)(len >> 24), .. data];
            return len % 2 == 1 ? [.. c, 0] : c;
        }

        var vp8x = Chunk("VP8X", [0x08 | 0x04 | 0x10, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var image = Chunk("VP8L", [0x2F, 0, 0, 0, 0]);
        byte[] body = [.. "WEBP"u8, .. vp8x, .. image, .. Chunk("EXIF", Secret), .. Chunk("XMP ", Secret)];
        byte[] webp = [.. "RIFF"u8, (byte)body.Length, (byte)(body.Length >> 8), 0, 0, .. body];

        var clean = ImageMetadata.Strip(webp, "image/webp");

        Assert.False(Contains(clean, Secret));
        Assert.Equal(0x10, clean[20]);   // VP8X flags: only the alpha bit remains
        Assert.Equal(clean.Length - 8, BitConverter.ToInt32(clean, 4));
        Assert.True(Contains(clean, image));
    }

    [Fact]
    public void AJpegMissingItsEndMarkerIsKeptAndClosed()
    {
        byte[] jpeg = [0xFF, 0xD8, .. Segment(0xE1, Exif(1)), .. Dqt, .. Sof, .. Sos, .. Scan];

        byte[] expected = [0xFF, 0xD8, .. Dqt, .. Sof, .. Sos, .. Scan, .. Eoi];
        Assert.Equal(expected, ImageMetadata.Strip(jpeg, "image/jpeg"));
    }

    [Fact]
    public void TruncatedImagesAreRejectedRatherThanStoredHalfParsed()
    {
        var jpeg = Jpeg(1);
        Assert.Throws<InvalidImageException>(() => ImageMetadata.Strip(jpeg[..20], "image/jpeg"));
    }

    [Fact]
    public async Task UploadedJpegIsServedBackWithoutItsGpsData()
    {
        var user = await _api.RegisterAsync($"u{Guid.NewGuid():N}@example.com", "Traveller");

        var content = new ByteArrayContent(Jpeg(orientation: 1));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        var upload = await user.Client.PostAsync("/me/photos", new MultipartFormDataContent { { content, "file", "beach.jpg" } });
        upload.EnsureSuccessStatusCode();

        var profile = await user.Client.GetFromJsonAsync<MyProfileResponse>("/me");
        var served = await user.Client.GetByteArrayAsync(profile!.PhotoUrls.Single());

        Assert.False(Contains(served, Secret));
        Assert.True(served.AsSpan().EndsWith([.. Scan, .. Eoi]));
    }
}
