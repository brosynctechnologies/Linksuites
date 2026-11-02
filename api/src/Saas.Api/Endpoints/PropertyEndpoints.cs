using Saas.Api.Infrastructure;
using Saas.Application.Auth;
using Saas.Application.Property;
using Saas.Domain.Core;
using Saas.Domain.Property;
using Saas.Infrastructure.Auth;

namespace Saas.Api.Endpoints;

/// <summary>Property Listing Website: seeker accounts, agency studio, public search, leads, trust and short-lets.</summary>
public static class PropertyEndpoints
{
    public static void MapPropertyEndpoints(this IEndpointRouteBuilder app)
    {
        // Seekers and guests: accounts with no business attached (the same accounts LearnLink learners use).
        var seekerAuth = app.MapGroup("/api/auth/seeker").WithTags("Auth").RequireRateLimiting(RateLimits.Auth);
        seekerAuth.MapPost("/signup", (LearnerSignupRequest r, AuthService s, CancellationToken ct) => s.LearnerSignupAsync(r, ct))
            .Validate<LearnerSignupRequest>();
        seekerAuth.MapPost("/login", (LoginRequest r, AuthService s, CancellationToken ct) => s.LearnerLoginAsync(r, ct)).Validate<LoginRequest>();

        // Public: search, listing pages, agents, agencies, locations and area guides.
        var pub = app.MapGroup("/api").WithTags("Property: public");
        pub.MapGet("/listings", ([AsParameters] SearchQuery q, PropertySearchService s, CancellationToken ct) => s.SearchAsync(q, ct));
        pub.MapGet("/listings/featured", (PropertySearchService s, CancellationToken ct) => s.FeaturedAsync(ct));
        pub.MapGet("/listings/{agency}/{listing}", (string agency, string listing, PropertySearchService s, CancellationToken ct) => s.ListingAsync(agency, listing, ct));
        pub.MapPost("/listings/{id:guid}/events", async (Guid id, ListingEventRequest r, ListingEventsService s, CancellationToken ct) =>
        {
            await s.RecordAsync(id, r.Kind, ct);
            return Results.NoContent();
        }).RequireRateLimiting(RateLimits.PublicWrite);
        pub.MapPost("/listings/{id:guid}/report", async (Guid id, ReportRequest r, ReportService s, CancellationToken ct) =>
        {
            await s.ReportAsync(id, r, ct);
            return Results.NoContent();
        }).Validate<ReportRequest>().RequireRateLimiting(RateLimits.PublicWrite);
        pub.MapPost("/listings/{id:guid}/enquiries", async (Guid id, EnquiryRequest r, LeadService s, CancellationToken ct) =>
        {
            await s.SendAsync(id, r, ct);
            return Results.NoContent();
        }).Validate<EnquiryRequest>().RequireRateLimiting(RateLimits.PublicWrite);
        pub.MapGet("/listings/renew/{token}", (string token, ListingRenewService s, CancellationToken ct) => s.PreviewAsync(token, ct));
        pub.MapPost("/listings/renew/{token}", (string token, ListingRenewService s, CancellationToken ct) => s.RenewAsync(token, ct))
            .RequireRateLimiting(RateLimits.PublicWrite);
        pub.MapGet("/listings/{id:guid}/availability", (Guid id, DateOnly? from, DateOnly? to, StayService s, CancellationToken ct) =>
            s.AvailabilityAsync(id, from, to, ct));
        pub.MapPost("/listings/{id:guid}/stay-quote", (Guid id, StayRequest r, StayService s, CancellationToken ct) => s.QuoteAsync(id, r, ct));
        pub.MapGet("/listings/{id:guid}/inspection-times", (Guid id, InspectionService s, CancellationToken ct) => s.TimesAsync(id, ct));

        // Signed-in seekers (any account).
        var seeker = app.MapGroup("/api").RequireAuthorization().WithTags("Property: seekers");
        seeker.MapPost("/listings/{id:guid}/inspections", (Guid id, InspectionRequest r, InspectionService s, CancellationToken ct) => s.RequestAsync(id, r, ct)).Validate<InspectionRequest>()
            .RequireRateLimiting(RateLimits.PublicWrite);
        seeker.MapPost("/agents/{slug}/reviews", (string slug, ReviewRequest r, AgentReviewService s, CancellationToken ct) => s.ReviewAsync(slug, r, ct))
            .Validate<ReviewRequest>();
        seeker.MapPost("/reviews/{id:guid}/report", async (Guid id, AgentReviewService s, CancellationToken ct) =>
        {
            await s.ReportAsync(id, ct);
            return Results.NoContent();
        });
        seeker.MapPost("/stays", (StayRequest r, StayService s, CancellationToken ct) => s.BookAsync(r, ct)).Validate<StayRequest>().RequireRateLimiting(RateLimits.PublicWrite);
        seeker.MapPost("/stays/{id:guid}/verify", (Guid id, VerifyStayRequest r, StayService s, CancellationToken ct) => s.VerifyAsync(id, r, ct));
        seeker.MapPost("/stays/{id:guid}/cancel", (Guid id, StayService s, CancellationToken ct) => s.CancelAsync(id, ct));
        var me = seeker.MapGroup("/me");
        me.MapGet("/stays", (StayService s, CancellationToken ct) => s.MineAsync(ct));
        me.MapGet("/saved-listings", (SeekerService s, CancellationToken ct) => s.SavedListingsAsync(ct));
        me.MapPost("/saved-listings/{id:guid}", async (Guid id, SeekerService s, CancellationToken ct) =>
        {
            await s.SaveListingAsync(id, ct);
            return Results.NoContent();
        });
        me.MapDelete("/saved-listings/{id:guid}", async (Guid id, SeekerService s, CancellationToken ct) =>
        {
            await s.UnsaveListingAsync(id, ct);
            return Results.NoContent();
        });
        me.MapGet("/saved-searches", (SeekerService s, CancellationToken ct) => s.SearchesAsync(ct));
        me.MapPost("/saved-searches", (SaveSearchRequest r, SeekerService s, CancellationToken ct) => s.SaveSearchAsync(null, r, ct)).Validate<SaveSearchRequest>();
        me.MapPut("/saved-searches/{id:guid}", (Guid id, SaveSearchRequest r, SeekerService s, CancellationToken ct) => s.SaveSearchAsync(id, r, ct))
            .Validate<SaveSearchRequest>();
        me.MapDelete("/saved-searches/{id:guid}", async (Guid id, SeekerService s, CancellationToken ct) =>
        {
            await s.DeleteSearchAsync(id, ct);
            return Results.NoContent();
        });
        me.MapGet("/enquiries", (LeadService s, CancellationToken ct) => s.MineAsync(ct));
        me.MapGet("/inspections", (InspectionService s, CancellationToken ct) => s.MineAsync(ct));
        me.MapPost("/inspections/{id:guid}/cancel", async (Guid id, InspectionService s, CancellationToken ct) =>
        {
            await s.CancelMineAsync(id, ct);
            return Results.NoContent();
        });

        pub.MapGet("/locations", (string? parent, PropertySearchService s, CancellationToken ct) => s.LocationsAsync(parent, ct));
        pub.MapGet("/property-types", (PropertySearchService s, CancellationToken ct) => s.TypesAsync(ct));
        pub.MapGet("/amenities", (PropertySearchService s, CancellationToken ct) => s.AmenitiesAsync(ct));
        pub.MapGet("/areas/{slug}", (string slug, PropertySearchService s, CancellationToken ct) => s.AreaAsync(slug, ct));
        pub.MapGet("/agents/{slug}", (string slug, PropertySearchService s, CancellationToken ct) => s.AgentAsync(slug, ct));
        pub.MapGet("/agencies/{slug}", (string slug, PropertySearchService s, CancellationToken ct) => s.AgencyAsync(slug, ct));

        // Agency studio.
        MemberRole[] editors = [MemberRole.Owner, MemberRole.Admin, MemberRole.Staff];
        var studio = app.MapGroup("/api/property").RequireAuthorization(Policies.PropertyProduct).WithTags("Property: studio");

        var listings = studio.MapGroup("/listings").WritableBy(editors);
        listings.MapGet("/", (ListingStatus? status, ListingStudioService s, CancellationToken ct) => s.ListAsync(status, ct));
        listings.MapPost("/", (SaveListingRequest r, ListingStudioService s, CancellationToken ct) => s.CreateAsync(r, ct)).Validate<SaveListingRequest>();
        listings.MapGet("/{id:guid}", (Guid id, ListingStudioService s, CancellationToken ct) => s.GetAsync(id, ct));
        listings.MapPut("/{id:guid}", (Guid id, SaveListingRequest r, ListingStudioService s, CancellationToken ct) => s.UpdateAsync(id, r, ct))
            .Validate<SaveListingRequest>();
        listings.MapDelete("/{id:guid}", async (Guid id, ListingStudioService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });
        listings.MapPost("/{id:guid}/submit", (Guid id, ListingStudioService s, CancellationToken ct) => s.SubmitAsync(id, ct));
        listings.MapPost("/{id:guid}/renew", (Guid id, ListingStudioService s, CancellationToken ct) => s.RenewAsync(id, ct));
        listings.MapPost("/{id:guid}/status", (Guid id, SetListingStatusRequest r, ListingStudioService s, CancellationToken ct) => s.SetStatusAsync(id, r, ct));

        listings.MapPost("/{id:guid}/media", (Guid id, IFormFile file, ListingMediaService s, CancellationToken ct) => s.UploadAsync(id, file.ToUpload(), ct))
            .DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16 * 1024 * 1024));
        listings.MapPut("/{id:guid}/media/order", async (Guid id, ReorderRequest r, ListingMediaService s, CancellationToken ct) =>
        {
            await s.ReorderAsync(id, r, ct);
            return Results.NoContent();
        });
        listings.MapDelete("/{id:guid}/media/{mediaId:guid}", async (Guid id, Guid mediaId, ListingMediaService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, mediaId, ct);
            return Results.NoContent();
        });

        var leads = studio.MapGroup("").WritableBy(editors);
        leads.MapGet("/enquiries", (EnquiryStatus? status, LeadService s, CancellationToken ct) => s.InboxAsync(status, ct));
        leads.MapPost("/enquiries/{id:guid}/status", async (Guid id, SetEnquiryStatusRequest r, LeadService s, CancellationToken ct) =>
        {
            await s.SetStatusAsync(id, r, ct);
            return Results.NoContent();
        });
        leads.MapGet("/inspection-slots", (InspectionService s, CancellationToken ct) => s.SlotsAsync(ct));
        leads.MapPut("/inspection-slots", (SaveSlotsRequest r, InspectionService s, CancellationToken ct) => s.SaveSlotsAsync(r, ct));
        leads.MapGet("/inspections", (DateTimeOffset? from, DateTimeOffset? to, InspectionService s, CancellationToken ct) => s.StudioAsync(from, to, ct));
        leads.MapPost("/inspections/{id:guid}/status", (Guid id, SetInspectionStatusRequest r, InspectionService s, CancellationToken ct) => s.SetStatusAsync(id, r, ct));

        leads.MapGet("/stays", (DateOnly? from, DateOnly? to, HostStayService s, CancellationToken ct) => s.ListAsync(from, to, ct));
        leads.MapPost("/stays/{id:guid}/cancel", async (Guid id, HostStayService s, CancellationToken ct) =>
        {
            await s.CancelAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Admin);
        leads.MapGet("/listings/{id:guid}/blocks", (Guid id, HostStayService s, CancellationToken ct) => s.BlocksAsync(id, ct));
        leads.MapPost("/listings/{id:guid}/blocks", (Guid id, BlockRequest r, HostStayService s, CancellationToken ct) => s.AddBlockAsync(id, r, ct));
        leads.MapDelete("/listings/{id:guid}/blocks/{blockId:guid}", async (Guid id, Guid blockId, HostStayService s, CancellationToken ct) =>
        {
            await s.DeleteBlockAsync(id, blockId, ct);
            return Results.NoContent();
        });

        var money = studio.MapGroup("").WritableBy([MemberRole.Owner, MemberRole.Admin]);
        money.MapGet("/boost-options", () => BoostService.Options);
        money.MapGet("/boosts", (BoostService s, CancellationToken ct) => s.ListAsync(ct));
        money.MapPost("/listings/{id:guid}/boosts", (Guid id, BuyBoostRequest r, BoostService s, CancellationToken ct) => s.BuyAsync(id, r, ct));
        money.MapPost("/boosts/{id:guid}/verify", (Guid id, VerifyBoostRequest r, BoostService s, CancellationToken ct) => s.VerifyAsync(id, r, ct));
        // Earnings, payouts and the bank account are for Owner and Admin; Staff and Accountant can't read them.
        money.MapGet("/earnings", (HostStayService s, CancellationToken ct) => s.EarningsAsync(ct)).RequireAuthorization(Policies.Admin);
        money.MapGet("/payouts", (Saas.Application.Payouts.PayoutService s, CancellationToken ct) => s.ListAsync(ct)).RequireAuthorization(Policies.Admin);
        money.MapPost("/payouts", (Saas.Application.Payouts.PayoutRequest r, HostStayService s, CancellationToken ct) => s.RequestPayoutAsync(r, ct))
            .RequireAuthorization(Policies.Owner);
        money.MapGet("/payout-account", async (HostStayService s, CancellationToken ct) => await s.PayoutAccountAsync(ct) is { } a ? Results.Ok(a) : Results.NoContent())
            .RequireAuthorization(Policies.Admin);
        money.MapPut("/payout-account", (Saas.Application.Payouts.PayoutAccountDto r, HostStayService s, CancellationToken ct) => s.SetPayoutAccountAsync(r, ct))
            .Validate<Saas.Application.Payouts.PayoutAccountDto>().RequireAuthorization(Policies.Owner);

        studio.MapGet("/stats", (int? days, ListingEventsService s, CancellationToken ct) => s.StatsAsync(days ?? 30, ct));

        var verification = studio.MapGroup("/verification").WritableBy([MemberRole.Owner, MemberRole.Admin]);
        verification.MapGet("/", async (VerificationService s, CancellationToken ct) => await s.LatestAsync(ct) is { } v ? Results.Ok(v) : Results.NoContent());
        verification.MapPost("/", (IFormFile idDocument, IFormFile? businessDocument, [Microsoft.AspNetCore.Mvc.FromForm] string? licenceNo,
                VerificationService s, CancellationToken ct) => s.SubmitAsync(idDocument.ToUpload(), businessDocument?.ToUpload(), licenceNo, ct))
            .DisableAntiforgery();

        // Platform admins: moderation, verification, reports and locations.
        var admin = app.MapGroup("/api/admin/property").RequireAuthorization(Policies.PlatformSession, Policies.PlatformModerator).RequireRateLimiting(RateLimits.Admin).WithTags("Property: admin");
        admin.MapGet("/moderation", (PropertyAdminService s, CancellationToken ct) => s.ModerationAsync(ct));
        admin.MapPost("/listings/{id:guid}/decision", async (Guid id, ListingDecisionRequest r, PropertyAdminService s, CancellationToken ct) =>
        {
            await s.DecideListingAsync(id, r, ct);
            return Results.NoContent();
        });
        admin.MapGet("/verifications", (VerificationStatus? status, PropertyAdminService s, CancellationToken ct) => s.VerificationsAsync(status, ct));
        admin.MapGet("/verifications/{id:guid}/documents/{index:int}", async (Guid id, int index, PropertyAdminService s, CancellationToken ct) =>
        {
            var file = await s.DocumentAsync(id, index, ct);
            return file.RedirectUrl is { } url ? Results.Redirect(url) : Results.Stream(file.Content!, file.ContentType);
        });
        admin.MapPost("/verifications/{id:guid}/decision", async (Guid id, VerificationDecisionRequest r, PropertyAdminService s, CancellationToken ct) =>
        {
            await s.DecideVerificationAsync(id, r, ct);
            return Results.NoContent();
        });
        admin.MapGet("/reports", (ReportStatus? status, PropertyAdminService s, CancellationToken ct) => s.ReportsAsync(status, ct));
        admin.MapPost("/reports/{id:guid}/decision", async (Guid id, ReportDecisionRequest r, PropertyAdminService s, CancellationToken ct) =>
        {
            await s.DecideReportAsync(id, r, ct);
            return Results.NoContent();
        });
        admin.MapGet("/reviews", (AgentReviewService s, CancellationToken ct) => s.HiddenAsync(ct));
        admin.MapPost("/reviews/{id:guid}/decision", async (Guid id, ReviewDecisionRequest r, AgentReviewService s, CancellationToken ct) =>
        {
            await s.DecideAsync(id, r, ct);
            return Results.NoContent();
        });
        admin.MapGet("/locations", (PropertyAdminService s, CancellationToken ct) => s.LocationsAsync(ct));
        admin.MapPost("/locations", (SaveLocationRequest r, PropertyAdminService s, CancellationToken ct) => s.SaveLocationAsync(null, r, ct));
        admin.MapPut("/locations/{id:guid}", (Guid id, SaveLocationRequest r, PropertyAdminService s, CancellationToken ct) => s.SaveLocationAsync(id, r, ct));

        var profile = studio.MapGroup("/profile");
        profile.MapGet("/agency", (ListingStudioService s, CancellationToken ct) => s.GetAgencyAsync(ct));
        profile.MapPut("/agency", (UpdateAgencyRequest r, ListingStudioService s, CancellationToken ct) => s.UpdateAgencyAsync(r, ct))
            .Validate<UpdateAgencyRequest>().RequireAuthorization(Policies.Admin);
        profile.MapGet("/agents", (ListingStudioService s, CancellationToken ct) => s.AgentsAsync(ct));
        profile.MapGet("/agent", (ListingStudioService s, CancellationToken ct) => s.MyAgentAsync(ct));
        profile.MapPut("/agent", (UpdateAgentRequest r, ListingStudioService s, CancellationToken ct) => s.UpdateMyAgentAsync(r, ct))
            .Validate<UpdateAgentRequest>();
    }
}
