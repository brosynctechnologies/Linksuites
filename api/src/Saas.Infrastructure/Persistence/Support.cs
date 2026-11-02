using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Infrastructure.Persistence;

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    public void SetTenant(Guid tenantId)
    {
        if (TenantId is { } current && current != tenantId)
            throw new InvalidOperationException("The tenant for this request is already set to a different tenant.");
        TenantId = tenantId;
    }
}

internal sealed class NoUser : ICurrentUser
{
    public Guid? UserId => null;
    public MemberRole? Role => null;
    public string? IpAddress => null;
}

/// <summary>Used by `dotnet ef` so migrations do not need the web host.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=55432;Database=saas;Username=postgres;Password=postgres", o => o.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options, new TenantContext(), new NoUser());
    }
}

/// <summary>Idempotent reference data: plans for each product.</summary>
public static class DataSeeder
{
    private static Plan BookingPlan(string code, string name, long price, int order, long staff, long bookings, bool paid, long seats, List<string> features) => new()
    {
        Product = Products.Bookings, Code = code, Name = name, PriceMinor = price, Currency = "NGN", IsDefault = code == "free",
        SortOrder = order, Features = features,
        Limits = new()
        {
            [LimitKeys.StaffMembers] = staff, [LimitKeys.BookingsPerMonth] = bookings, [LimitKeys.Deposits] = paid ? 1 : 0,
            [LimitKeys.BookingReminders] = paid ? 1 : 0, [LimitKeys.RemoveBranding] = paid ? 1 : 0, [LimitKeys.TeamSeats] = seats,
        },
    };

    private static Plan CoursePlan(string code, string name, long price, int order, long courses, long storageMb, long feePct, bool paid, long seats, List<string> features) => new()
    {
        Product = Products.Courses, Code = code, Name = name, PriceMinor = price, Currency = "NGN", IsDefault = code == "free",
        SortOrder = order, Features = features,
        Limits = new()
        {
            [LimitKeys.Courses] = courses, [LimitKeys.VideoStorageMb] = storageMb, [LimitKeys.AcademyFeePercent] = feePct,
            [LimitKeys.Coupons] = paid ? 1 : 0, [LimitKeys.Certificates] = paid ? 1 : 0, [LimitKeys.RemoveBranding] = paid ? 1 : 0,
            [LimitKeys.TeamSeats] = seats,
        },
    };

    private static Plan PropertyPlan(string code, string name, long price, int order, long listings, long expiryDays, bool paid, long seats, List<string> features) => new()
    {
        Product = Products.Property, Code = code, Name = name, PriceMinor = price, Currency = "NGN", IsDefault = code == "free",
        SortOrder = order, Features = features,
        Limits = new()
        {
            [LimitKeys.Listings] = listings, [LimitKeys.ListingExpiryDays] = expiryDays, [LimitKeys.ListingStats] = paid ? 1 : 0,
            [LimitKeys.TeamSeats] = seats,
        },
    };

    private static Plan SchoolPlan(string code, string name, long price, int order, long students, long seats, bool paid, bool onlineFees, List<string> features) => new()
    {
        Product = Products.School, Code = code, Name = name, PriceMinor = price, Currency = "NGN", Interval = BillingInterval.Term,
        IsDefault = code == "free", SortOrder = order, Features = features,
        Limits = new()
        {
            [LimitKeys.Students] = students, [LimitKeys.TeamSeats] = seats, [LimitKeys.RemoveBranding] = paid ? 1 : 0,
            [LimitKeys.OnlineFees] = onlineFees ? 1 : 0,
        },
    };

    private static Plan CarePlan(string code, string name, long price, int order, long seats, long beds, bool onlinePayments, bool removeBranding, List<string> features) => new()
    {
        Product = Products.Care, Code = code, Name = name, PriceMinor = price, Currency = "NGN", Interval = BillingInterval.Month,
        IsDefault = code == "free", SortOrder = order, Features = features,
        Limits = new()
        {
            [LimitKeys.TeamSeats] = seats, [LimitKeys.Beds] = beds, [LimitKeys.OnlinePayments] = onlinePayments ? 1 : 0,
            [LimitKeys.RemoveBranding] = removeBranding ? 1 : 0,
        },
    };

    private static readonly string[] Categories =
    [
        "Exam prep", "Technology and coding", "Design", "Business", "Finance and accounting", "Marketing", "Languages",
        "Personal development", "Health and fitness", "Music", "Photography and video", "Professional certifications",
    ];

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        for (var i = 0; i < Categories.Length; i++)
        {
            var slug = Saas.Application.Common.Slugs.From(Categories[i]);
            if (!await db.CourseCategories.AnyAsync(c => c.Slug == slug, ct))
                db.CourseCategories.Add(new Saas.Domain.Courses.CourseCategory { Name = Categories[i], Slug = slug, Position = i });
        }

        await PropertySeed.SeedAsync(db, ct);

        var plans = new[]
        {
            new Plan
            {
                Product = Products.WhatsAppOrders, Code = "free", Name = "Free", PriceMinor = 0, Currency = "NGN",
                IsDefault = true, SortOrder = 0,
                Limits = new()
                {
                    [LimitKeys.Products] = 20, [LimitKeys.DeliveryZones] = 1, [LimitKeys.DiscountCodes] = 0,
                    [LimitKeys.RemoveBranding] = 0, [LimitKeys.CustomDomain] = 0,
                },
                Features = ["Up to 20 products", "1 delivery zone", "Unlimited orders", "QR code and store link"],
            },
            new Plan
            {
                Product = Products.WhatsAppOrders, Code = "pro", Name = "Pro", PriceMinor = 300_000, Currency = "NGN",
                SortOrder = 1,
                Limits = new()
                {
                    [LimitKeys.Products] = -1, [LimitKeys.DeliveryZones] = -1, [LimitKeys.DiscountCodes] = -1,
                    [LimitKeys.RemoveBranding] = 1, [LimitKeys.CustomDomain] = 1,
                },
                Features = ["Unlimited products", "Unlimited delivery zones", "Discount codes", "Remove branding", "Custom domain"],
            },
            new Plan
            {
                Product = Products.Invoices, Code = "free", Name = "Free", PriceMinor = 0, Currency = "NGN",
                IsDefault = true, SortOrder = 0,
                Limits = new()
                {
                    [LimitKeys.InvoicesPerMonth] = 10, [LimitKeys.PremiumTemplates] = 0, [LimitKeys.Reminders] = 0,
                    [LimitKeys.Recurring] = 0, [LimitKeys.PaymentLinks] = 0, [LimitKeys.RemoveBranding] = 0,
                    [LimitKeys.Expenses] = 0, [LimitKeys.Reports] = 0, [LimitKeys.TeamSeats] = 1,
                },
                Features = ["10 invoices a month", "Unlimited quotes and clients", "3 templates", "PDF, email and WhatsApp sharing"],
            },
            new Plan
            {
                Product = Products.Invoices, Code = "pro", Name = "Pro", PriceMinor = 400_000, Currency = "NGN",
                SortOrder = 1,
                Limits = new()
                {
                    [LimitKeys.InvoicesPerMonth] = -1, [LimitKeys.PremiumTemplates] = 1, [LimitKeys.Reminders] = 1,
                    [LimitKeys.Recurring] = 1, [LimitKeys.PaymentLinks] = 1, [LimitKeys.RemoveBranding] = 1,
                    [LimitKeys.Expenses] = 0, [LimitKeys.Reports] = 0, [LimitKeys.TeamSeats] = 1,
                },
                Features = ["Unlimited invoices", "All 5 templates", "Pay-now links on invoices", "Automatic payment reminders",
                    "Recurring invoices", "Remove branding"],
            },
            new Plan
            {
                Product = Products.Invoices, Code = "business", Name = "Business", PriceMinor = 1_000_000, Currency = "NGN",
                SortOrder = 2,
                Limits = new()
                {
                    [LimitKeys.InvoicesPerMonth] = -1, [LimitKeys.PremiumTemplates] = 1, [LimitKeys.Reminders] = 1,
                    [LimitKeys.Recurring] = 1, [LimitKeys.PaymentLinks] = 1, [LimitKeys.RemoveBranding] = 1,
                    [LimitKeys.Expenses] = 1, [LimitKeys.Reports] = 1, [LimitKeys.TeamSeats] = 10,
                },
                Features = ["Everything in Pro", "Expense tracking with receipts", "Profit & loss report",
                    "Team of up to 10, including your accountant"],
            },
            CoursePlan("free", "Free", 0, 0, courses: 2, storageMb: 2_048, feePct: 10, paid: false, seats: 1,
                ["2 courses", "2 GB of video", "10% fee on sales from your academy", "Marketplace listing (30% fee)"]),
            CoursePlan("pro", "Pro", 1_500_000, 1, courses: -1, storageMb: 51_200, feePct: 0, paid: true, seats: 3,
                ["Unlimited courses", "50 GB of video", "No fee on sales from your academy", "Coupons and certificates", "Remove branding"]),
            CoursePlan("business", "Business", 4_000_000, 2, courses: -1, storageMb: 256_000, feePct: 0, paid: true, seats: 10,
                ["Everything in Pro", "250 GB of video", "Team of up to 10"]),
            PropertyPlan("free", "Free", 0, 0, listings: 3, expiryDays: 30, paid: false, seats: 1,
                ["3 live listings", "Listings stay up 30 days, renew in one click", "Enquiries and WhatsApp leads", "Verified badge after checks"]),
            PropertyPlan("pro", "Pro", 1_000_000, 1, listings: 30, expiryDays: 60, paid: true, seats: 3,
                ["30 live listings", "Listings stay up 60 days", "Views and lead stats", "Team of up to 3"]),
            PropertyPlan("agency", "Agency", 3_500_000, 2, listings: -1, expiryDays: 60, paid: true, seats: 10,
                ["Unlimited listings", "Everything in Pro", "Team of up to 10 agents"]),
            SchoolPlan("free", "Free", 0, 0, students: 50, seats: 3, paid: false, onlineFees: false,
                ["Up to 50 students", "3 staff logins", "Results, report cards and attendance", "Your school's logo and branding"]),
            SchoolPlan("standard", "Standard", 6_000_000, 1, students: 300, seats: 15, paid: false, onlineFees: true,
                ["Up to 300 students", "15 staff logins", "Online fee payments to your Paystack", "Fee reminders to parents"]),
            SchoolPlan("premium", "Premium", 15_000_000, 2, students: 1_000, seats: 50, paid: true, onlineFees: true,
                ["Up to 1,000 students", "50 staff logins", "Everything in Standard", "Remove SchoolLink branding"]),
            CarePlan("free", "Free", 0, 0, seats: 3, beds: 0, onlinePayments: false, removeBranding: false,
                ["3 staff logins", "Patient records, visits and bills", "Outpatient clinic"]),
            CarePlan("clinic", "Clinic", 2_500_000, 1, seats: 15, beds: 20, onlinePayments: true, removeBranding: false,
                ["15 staff logins", "Up to 20 beds", "Online payments to your Paystack", "Patient portal"]),
            CarePlan("hospital", "Hospital", 7_500_000, 2, seats: 60, beds: 150, onlinePayments: true, removeBranding: true,
                ["60 staff logins", "Up to 150 beds", "Everything in Clinic", "Remove CareLink branding"]),
            BookingPlan("free", "Free", 0, 0, staff: 1, bookings: 50, paid: false, seats: 1,
                ["1 staff member", "50 bookings a month", "Online booking page", "Calendar and customer list"]),
            BookingPlan("solo", "Solo", 500_000, 1, staff: 1, bookings: -1, paid: true, seats: 2,
                ["Unlimited bookings", "Deposits paid to your Paystack", "Email reminders 24 and 2 hours before", "Remove branding"]),
            BookingPlan("team", "Team", 1_200_000, 2, staff: 5, bookings: -1, paid: true, seats: 6,
                ["Everything in Solo", "Up to 5 staff with their own calendars", "Team logins"]),
            BookingPlan("business", "Business", 2_500_000, 3, staff: -1, bookings: -1, paid: true, seats: 15,
                ["Everything in Team", "Unlimited staff", "Up to 15 team logins"]),
        };

        foreach (var plan in plans)
        {
            var existing = await db.Plans.FirstOrDefaultAsync(p => p.Product == plan.Product && p.Code == plan.Code, ct);
            // Seeds only plans that don't exist yet: after that, plans are managed in the admin panel and must not be
            // overwritten on every start.
            if (existing is null) db.Plans.Add(plan);
        }
        await db.SaveChangesAsync(ct);
    }
}
