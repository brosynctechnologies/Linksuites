using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Saas.Infrastructure.Property;
using SkiaSharp;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PropertyMediaTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    /// <summary>A photo-like PNG: a gradient with shapes, different for each seed.</summary>
    public static byte[] Photo(int seed, int width = 1200, int height = 800)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var gradient = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
            [new SKColor((byte)(40 * seed), 120, 200), new SKColor(250, (byte)(30 * seed), 60)], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = gradient };
        canvas.DrawRect(0, 0, width, height, paint);
        var random = new Random(seed);
        using var shape = new SKPaint { Color = SKColors.White };
        for (var i = 0; i < 6; i++)
            canvas.DrawCircle(random.Next(width), random.Next(height), random.Next(40, 200), shape);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient agency, Guid listingId, byte[] bytes, string name = "photo.png")
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", name } };
        return agency.PostAsync($"/api/property/listings/{listingId}/media", form, Ct);
    }

    private async Task ProcessAllAsync()
    {
        using var scope = api.Services.CreateScope();
        var processor = ActivatorUtilities.CreateInstance<PhotoProcessor>(scope.ServiceProvider);
        while (await processor.ProcessNextAsync(Ct)) { }
    }

    [Fact]
    public async Task Photos_are_resized_and_hashed()
    {
        var agency = await kit.AgencyAsync("Photo Agency");
        var listingId = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync())).GetProperty("id").GetGuid();

        var uploaded = await Json(await UploadAsync(agency.Client, listingId, Photo(1)));
        Assert.Equal("Processing", uploaded.GetProperty("status").GetString());
        await ProcessAllAsync();

        var media = (await Json(await agency.Client.GetAsync($"/api/property/listings/{listingId}", Ct))).GetProperty("media")[0];
        Assert.Equal("Ready", media.GetProperty("status").GetString());
        var card = await api.CreateClient().GetAsync(new Uri(media.GetProperty("cardUrl").GetString()!).PathAndQuery, Ct);
        Assert.Equal("image/webp", card.Content.Headers.ContentType?.MediaType);
        using var decoded = SKBitmap.Decode(await card.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal((960, 640), (decoded.Width, decoded.Height));
        var thumb = await api.CreateClient().GetAsync(new Uri(media.GetProperty("thumbUrl").GetString()!).PathAndQuery, Ct);
        Assert.Equal(HttpStatusCode.OK, thumb.StatusCode);
    }

    [Fact]
    public async Task Small_photos_are_not_upscaled()
    {
        var agency = await kit.AgencyAsync("Small Photo Agency");
        var listingId = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync())).GetProperty("id").GetGuid();
        await UploadAsync(agency.Client, listingId, Photo(2, 600, 400));
        await ProcessAllAsync();

        var media = (await Json(await agency.Client.GetAsync($"/api/property/listings/{listingId}", Ct))).GetProperty("media")[0];
        var full = await api.CreateClient().GetAsync(new Uri(media.GetProperty("fullUrl").GetString()!).PathAndQuery, Ct);
        using var decoded = SKBitmap.Decode(await full.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(600, decoded.Width);
    }

    [Fact]
    public async Task Same_photo_from_another_agency_is_flagged_and_held_for_review()
    {
        var photo = Photo(3);
        var first = await kit.AgencyAsync("Original Agency", verified: true);
        var firstListing = (await kit.CreateListingAsync(first.Client, await kit.ListingBodyAsync())).GetProperty("id").GetGuid();
        await UploadAsync(first.Client, firstListing, photo);
        await ProcessAllAsync();

        var copier = await kit.AgencyAsync("Copier Agency", verified: true);
        var copied = (await kit.CreateListingAsync(copier.Client, await kit.ListingBodyAsync())).GetProperty("id").GetGuid();
        await UploadAsync(copier.Client, copied, photo);
        await ProcessAllAsync();

        var media = (await Json(await copier.Client.GetAsync($"/api/property/listings/{copied}", Ct))).GetProperty("media")[0];
        Assert.Equal(firstListing, media.GetProperty("duplicateOfListingId").GetGuid());
        Assert.Equal("Original Agency", media.GetProperty("duplicateOfAgency").GetString());
        var submitted = await Json(await kit.SubmitAsync(copier.Client, copied));
        Assert.Equal("Pending", submitted.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Same_photo_twice_in_one_agency_is_allowed()
    {
        var photo = Photo(4);
        var agency = await kit.AgencyAsync("Repeat Agency", verified: true);
        var a = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Unit A"))).GetProperty("id").GetGuid();
        var b = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Unit B"))).GetProperty("id").GetGuid();
        await UploadAsync(agency.Client, a, photo);
        await UploadAsync(agency.Client, b, photo);
        await ProcessAllAsync();

        var submitted = await Json(await kit.SubmitAsync(agency.Client, b));
        Assert.Equal("Active", submitted.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Non_images_and_oversized_files_are_refused()
    {
        var agency = await kit.AgencyAsync("Careless Agency");
        var listingId = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync())).GetProperty("id").GetGuid();

        var pdf = await UploadAsync(agency.Client, listingId, "%PDF-1.7 not a photo"u8.ToArray(), "house.jpg");
        Assert.Equal(HttpStatusCode.BadRequest, pdf.StatusCode);
        Assert.Contains("JPEG, PNG or WebP", await pdf.Content.ReadAsStringAsync(Ct));

        var heic = await UploadAsync(agency.Client, listingId, [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c'], "IMG_0001.HEIC");
        Assert.Equal(HttpStatusCode.BadRequest, heic.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(agency.Client, listingId, [])).StatusCode);

        var big = new byte[16 * 1024 * 1024];
        Photo(5).CopyTo(big, 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(agency.Client, listingId, big)).StatusCode);
    }

    [Fact]
    public async Task Photos_can_be_reordered_and_removed()
    {
        var agency = await kit.AgencyAsync("Tidy Agency");
        var listingId = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync())).GetProperty("id").GetGuid();
        var first = (await Json(await UploadAsync(agency.Client, listingId, Photo(6)))).GetProperty("id").GetGuid();
        var second = (await Json(await UploadAsync(agency.Client, listingId, Photo(7)))).GetProperty("id").GetGuid();

        (await agency.Client.PutAsJsonAsync($"/api/property/listings/{listingId}/media/order", new { ids = new[] { second, first } }, Ct)).EnsureSuccessStatusCode();
        var media = (await Json(await agency.Client.GetAsync($"/api/property/listings/{listingId}", Ct))).GetProperty("media");
        Assert.Equal(second, media[0].GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await agency.Client.DeleteAsync($"/api/property/listings/{listingId}/media/{first}", Ct)).StatusCode);
        Assert.Equal(1, (await Json(await agency.Client.GetAsync($"/api/property/listings/{listingId}", Ct))).GetProperty("media").GetArrayLength());
    }
}
