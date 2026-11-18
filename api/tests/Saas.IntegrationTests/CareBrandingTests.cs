using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Common;
using SkiaSharp;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 1, CareLink item 1: the clinic logo (leadership) and each staff member's own signature, stored privately (re-encoded WebP under
/// <c>private/care/{tenant}/</c>), the old file deleted on replace or remove, and printed on prescriptions, discharge summaries and receipts.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareBrandingTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static MultipartFormDataContent ImageForm(byte[] bytes, string contentType = "image/png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", "image" } };
    }

    private static byte[] PngOf(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private Task<bool> StoredAsync(string key) => Task.Run(async () =>
    {
        var storage = api.Services.GetRequiredService<IFileStorage>();
        await using var stream = await storage.OpenReadAsync(key, Ct);
        return stream is not null;
    });

    private Task<string?> LogoKeyAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.ClinicProfiles.IgnoreQueryFilters().Where(c => c.TenantId == tenantId).Select(c => c.LogoKey).FirstAsync(Ct));

    private Task<string?> SignatureKeyAsync(Guid tenantId, Guid userId) =>
        api.WithDbAsync(db => db.CareStaffProfiles.IgnoreQueryFilters().Where(p => p.TenantId == tenantId && p.UserId == userId)
            .Select(p => p.SignatureKey).FirstOrDefaultAsync(Ct));

    private static int Images(byte[] pdf) => Encoding.Latin1.GetString(pdf).Split("/Subtype /Image").Length - 1;

    [Fact]
    public async Task Leadership_uploads_replaces_and_removes_the_clinic_logo()
    {
        var clinic = await kit.ClinicAsync("Logo Clinic");
        var (admin, _) = await kit.StaffAsync(clinic, "Admin");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");

        Assert.Equal(HttpStatusCode.NotFound, (await desk.GetAsync("/api/care/settings/logo", Ct)).StatusCode);
        var saved = await Json(await clinic.Owner.PutAsync("/api/care/settings/logo", ImageForm(PngOf(1600, 800, SKColors.SteelBlue)), Ct));
        Assert.True(saved.GetProperty("hasLogo").GetBoolean());
        var first = await LogoKeyAsync(clinic.TenantId);
        Assert.StartsWith($"private/care/{clinic.TenantId}/", first);
        Assert.EndsWith(".webp", first);
        Assert.True(await StoredAsync(first!));
        Assert.True((await Json(await desk.GetAsync("/api/care/setup", Ct))).GetProperty("clinic").GetProperty("hasLogo").GetBoolean());

        // Every member sees it (the app header); it is resized to fit 600 × 600.
        var image = await desk.GetAsync("/api/care/settings/logo", Ct);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/webp", image.Content.Headers.ContentType?.MediaType);
        using (var decoded = SKBitmap.Decode(await image.Content.ReadAsByteArrayAsync(Ct)))
        {
            Assert.Equal(600, decoded.Width);
            Assert.Equal(300, decoded.Height);
        }

        // An admin replaces it: a new key, the old file deleted.
        await Json(await admin.PutAsync("/api/care/settings/logo", ImageForm(PngOf(200, 200, SKColors.OrangeRed)), Ct));
        var second = await LogoKeyAsync(clinic.TenantId);
        Assert.NotEqual(first, second);
        Assert.False(await StoredAsync(first!));
        Assert.True(await StoredAsync(second!));

        // Not an image: 400 on the file. Any other role: 403 and nothing changes.
        var bad = await clinic.Owner.PutAsync("/api/care/settings/logo", ImageForm("not an image"u8.ToArray()), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("file", await bad.Content.ReadAsStringAsync(Ct));
        foreach (var client in new[] { desk, doctor, cashier })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync("/api/care/settings/logo", ImageForm(PngOf(20, 20, SKColors.Black)), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/api/care/settings/logo", Ct)).StatusCode);
        }
        Assert.Equal(second, await LogoKeyAsync(clinic.TenantId));

        // Removing it deletes the file.
        Assert.Equal(HttpStatusCode.NoContent, (await clinic.Owner.DeleteAsync("/api/care/settings/logo", Ct)).StatusCode);
        Assert.Null(await LogoKeyAsync(clinic.TenantId));
        Assert.False(await StoredAsync(second!));
        Assert.Equal(HttpStatusCode.NotFound, (await desk.GetAsync("/api/care/settings/logo", Ct)).StatusCode);
    }

    [Fact]
    public async Task Each_staff_member_sets_their_own_signature()
    {
        var clinic = await kit.ClinicAsync("Signature Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (cashier, cashierId) = await kit.StaffAsync(clinic, "Cashier");

        Assert.Equal(HttpStatusCode.NotFound, (await doctor.GetAsync("/api/care/staff/me/signature", Ct)).StatusCode);
        var saved = await Json(await doctor.PutAsync("/api/care/staff/me/signature", ImageForm(PngOf(1500, 900, SKColors.Navy)), Ct));
        Assert.True(saved.GetProperty("hasSignature").GetBoolean());
        var key = await SignatureKeyAsync(clinic.TenantId, doctorId);
        Assert.StartsWith($"private/care/{clinic.TenantId}/", key);
        Assert.True(await StoredAsync(key!));
        Assert.True((await Json(await doctor.GetAsync("/api/care/staff/me", Ct))).GetProperty("hasSignature").GetBoolean());

        // Read back as the owner's image only (600 × 200 box).
        var image = await doctor.GetAsync("/api/care/staff/me/signature", Ct);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        using (var decoded = SKBitmap.Decode(await image.Content.ReadAsByteArrayAsync(Ct)))
            Assert.True(decoded.Width <= 600 && decoded.Height <= 200, $"{decoded.Width}×{decoded.Height}");
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync("/api/care/staff/me/signature", Ct)).StatusCode);

        // Leadership sees who has one on the staff list.
        var staff = await Json(await clinic.Owner.GetAsync("/api/care/staff", Ct));
        Assert.True(staff.EnumerateArray().Single(s => s.GetProperty("userId").GetGuid() == doctorId).GetProperty("hasSignature").GetBoolean());
        Assert.False(staff.EnumerateArray().Single(s => s.GetProperty("userId").GetGuid() == cashierId).GetProperty("hasSignature").GetBoolean());

        // The cashier (who signs receipts) has one too; replacing deletes the old file; removing deletes the file.
        await Json(await cashier.PutAsync("/api/care/staff/me/signature", ImageForm(PngOf(300, 100, SKColors.DarkGreen)), Ct));
        await Json(await doctor.PutAsync("/api/care/staff/me/signature", ImageForm(PngOf(300, 100, SKColors.Black)), Ct));
        var replaced = await SignatureKeyAsync(clinic.TenantId, doctorId);
        Assert.NotEqual(key, replaced);
        Assert.False(await StoredAsync(key!));
        Assert.Equal(HttpStatusCode.BadRequest, (await doctor.PutAsync("/api/care/staff/me/signature", ImageForm("nope"u8.ToArray()), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await doctor.DeleteAsync("/api/care/staff/me/signature", Ct)).StatusCode);
        Assert.Null(await SignatureKeyAsync(clinic.TenantId, doctorId));
        Assert.False(await StoredAsync(replaced!));
        Assert.NotNull(await SignatureKeyAsync(clinic.TenantId, cashierId)); // someone else's is untouched
    }

    [Fact]
    public async Task Signatures_print_on_prescriptions_discharge_summaries_and_receipts()
    {
        var clinic = await kit.ClinicAsync("Signed Papers Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        await Json(await clinic.Owner.PutAsync("/api/care/settings/logo", ImageForm(PngOf(100, 100, SKColors.SteelBlue)), Ct));

        // A prescription from the doctor's own consultation.
        var patientId = (await kit.PatientAsync(clinic, "Signed", "Patient")).GetProperty("id").GetGuid();
        var visitId = (await kit.CheckInAsync(desk, patientId)).GetProperty("id").GetGuid();
        await Json(await nurse.PostAsJsonAsync($"/api/care/visits/{visitId}/triage",
            new { vitals = new { temperatureC = 37.0 }, urgency = "Routine", doctorUserId = doctorId }, Ct));
        var consultationId = (await Json(await doctor.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct))).GetProperty("id").GetGuid();
        var rx = (await Json(await doctor.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId, consultationId, allergyAcknowledged = false,
            items = new[] { new { drug = "Paracetamol", dose = "1 tablet", frequency = "TDS", durationDays = 3 } },
        }, Ct))).GetProperty("id").GetGuid();
        async Task<byte[]> PrescriptionPdfAsync() => await (await doctor.GetAsync($"/api/care/prescriptions/{rx}/pdf", Ct)).Content.ReadAsByteArrayAsync(Ct);
        var unsigned = Images(await PrescriptionPdfAsync());
        await Json(await doctor.PutAsync("/api/care/staff/me/signature", ImageForm(PngOf(300, 100, SKColors.Navy)), Ct));
        // While the consultation is a draft, the prescription prints without the doctor's signature (it must not look authorised).
        Assert.Equal(unsigned, Images(await PrescriptionPdfAsync()));
        await Json(await doctor.PutAsJsonAsync($"/api/care/consultations/{consultationId}", new
        {
            complaint = "Headache", history = "Two days", examination = "Normal", plan = "Paracetamol",
            diagnoses = new object[] { new { text = "Tension headache", code = (string?)null, primary = true } },
        }, Ct));
        await Json(await doctor.PostAsync($"/api/care/consultations/{consultationId}/sign", null, Ct));
        Assert.True(Images(await PrescriptionPdfAsync()) > unsigned, "the prescriber's signature prints once the consultation is signed");

        // A discharge summary signed by the same doctor.
        var wardId = await kit.WardAsync(clinic, "Signed ward");
        var bedId = await kit.BedAsync(clinic, wardId, "S1");
        var admissionId = await kit.AdmitAsync(doctor, patientId, emergencyReason: null);
        await Json(await kit.AssignBedAsync(desk, admissionId, bedId));
        await Json(await doctor.PutAsJsonAsync($"/api/care/admissions/{admissionId}/discharge-summary", new
        {
            diagnoses = "Malaria", treatment = "ACT", condition = "Stable", takeHome = "Rest",
        }, Ct));
        await Json(await doctor.PostAsync($"/api/care/admissions/{admissionId}/discharge-summary/sign", null, Ct));
        async Task<byte[]> DischargePdfAsync() => await (await doctor.GetAsync($"/api/care/admissions/{admissionId}/discharge.pdf", Ct)).Content.ReadAsByteArrayAsync(Ct);
        var withSignature = Images(await DischargePdfAsync());
        var signedPrescription = Images(await PrescriptionPdfAsync());
        // Wave 2: the signature is frozen at signing. Removing it (or a new one, or the doctor leaving) changes no signed paper.
        Assert.Equal(HttpStatusCode.NoContent, (await doctor.DeleteAsync("/api/care/staff/me/signature", Ct)).StatusCode);
        Assert.Equal(withSignature, Images(await DischargePdfAsync()));
        Assert.Equal(signedPrescription, Images(await PrescriptionPdfAsync()));
        Assert.True(withSignature > 0);

        // A receipt carries the signature the cashier had when they took the money: none here, and a later one doesn't appear.
        var billId = (await kit.CheckInAsync(desk, (await kit.PatientAsync(clinic, "Receipt", "Payer")).GetProperty("id").GetGuid())).GetProperty("billId").GetGuid();
        var item = await kit.PriceItemAsync(clinic, "Card fee", "Other", 100_000);
        await Json(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/lines", new { priceItemId = item, quantity = 1, reason = "Card" }, Ct));
        var paymentId = (await Json(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/payments", new { amountMinor = 50_000, method = "Cash" }, Ct)))
            .GetProperty("id").GetGuid();
        async Task<byte[]> ReceiptAsync(Guid id) => await (await desk.GetAsync($"/api/care/payments/{id}/receipt.pdf", Ct)).Content.ReadAsByteArrayAsync(Ct);
        var plain = Images(await ReceiptAsync(paymentId));
        await Json(await cashier.PutAsync("/api/care/staff/me/signature", ImageForm(PngOf(300, 100, SKColors.DarkGreen)), Ct));
        Assert.Equal(plain, Images(await ReceiptAsync(paymentId)));
        var second = (await Json(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/payments", new { amountMinor = 50_000, method = "Cash" }, Ct)))
            .GetProperty("id").GetGuid();
        var signed = await ReceiptAsync(second);
        Assert.True(Images(signed) > plain, "the cashier's signature prints on the receipt");
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(signed, 0, 4));
        // A newer signature leaves the old file in place while a signed paper uses it, and that paper keeps printing it.
        await Json(await cashier.PutAsync("/api/care/staff/me/signature", ImageForm(PngOf(300, 100, SKColors.Black)), Ct));
        Assert.Equal(Images(signed), Images(await ReceiptAsync(second)));
    }
}
