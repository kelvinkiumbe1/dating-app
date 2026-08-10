using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DatingApp.Api;

namespace DatingApp.Api.Tests;

public class PhotoTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public PhotoTests(ApiFactory api) => _api = api;

    private static string Email() => $"u{Guid.NewGuid():N}@example.com";

    private static readonly byte[] OnePixelPng =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    };

    private static MultipartFormDataContent Upload(byte[] bytes, string contentType, string name)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new MultipartFormDataContent { { content, "file", name } };
    }

    [Fact]
    public async Task UploadedPhotoCanBeFetchedBackAndAppearsOnTheProfile()
    {
        var user = await _api.RegisterAsync(Email(), "Snapper");

        var upload = await user.Client.PostAsync("/me/photos", Upload(OnePixelPng, "image/png", "me.png"));
        upload.EnsureSuccessStatusCode();

        var profile = await user.Client.GetFromJsonAsync<MyProfileResponse>("/me");
        var url = Assert.Single(profile!.PhotoUrls);

        var fetched = await user.Client.GetAsync(url);
        fetched.EnsureSuccessStatusCode();
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        Assert.Equal(OnePixelPng, await fetched.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AFileLyingAboutItsTypeIsRejected()
    {
        var user = await _api.RegisterAsync(Email(), "Liar");

        // Declares image/png but the bytes are plain text — Content-Type alone is
        // attacker-controlled, so the magic bytes have to agree.
        var response = await user.Client.PostAsync("/me/photos",
            Upload("<?php echo 'not a png'; ?>"u8.ToArray(), "image/png", "payload.png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("do not match", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DisallowedContentTypesAreRejected()
    {
        var user = await _api.RegisterAsync(Email(), "Giffer");
        var response = await user.Client.PostAsync("/me/photos",
            Upload(OnePixelPng, "image/gif", "me.gif"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeletingAPhotoRemovesItFromTheProfileAndStorage()
    {
        var user = await _api.RegisterAsync(Email(), "Deleter");

        var upload = await user.Client.PostAsync("/me/photos", Upload(OnePixelPng, "image/png", "me.png"));
        var created = await upload.Content.ReadFromJsonAsync<UploadedPhoto>();

        var deleted = await user.Client.DeleteAsync($"/me/photos/{created!.Id}");
        deleted.EnsureSuccessStatusCode();

        Assert.Empty((await user.Client.GetFromJsonAsync<MyProfileResponse>("/me"))!.PhotoUrls);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/photos/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task PhotosAppearOnCandidateCardsInTheDeck()
    {
        var me = await _api.RegisterAsync(Email(), "Looker", gender: "f", seeking: "m");
        var them = await _api.RegisterAsync(Email(), "Looked", gender: "m", seeking: "f");

        await them.Client.PostAsync("/me/photos", Upload(OnePixelPng, "image/png", "them.png"));

        var deck = await me.Client.GetFromJsonAsync<List<CandidateResponse>>("/discover");
        var candidate = deck!.Single(c => c.Id == them.Id);
        Assert.Single(candidate.PhotoUrls);
    }

    private record UploadedPhoto(Guid Id, string Url, bool IsPrimary);
}
