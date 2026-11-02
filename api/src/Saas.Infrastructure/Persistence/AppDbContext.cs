using Saas.Domain.Property;
using Saas.Domain.School;
using Saas.Domain.Payouts;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Saas.Application.Common;
using Saas.Domain.Bookings;
using Saas.Domain.Common;
using Saas.Domain.Core;
using Saas.Domain.Courses;
using Saas.Domain.Invoices;
using Saas.Domain.Orders;

namespace Saas.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant, ICurrentUser currentUser)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IAppDbContext, IDataProtectionKeyContext
{
    // Shared core
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // WhatsApp Order System
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductOption> ProductOptions => Set<ProductOption>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();
    public DbSet<DiscountCode> DiscountCodes => Set<DiscountCode>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();

    // Invoice Generator
    public DbSet<BusinessSettings> BusinessSettings => Set<BusinessSettings>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<InvoicePayment> InvoicePayments => Set<InvoicePayment>();
    public DbSet<InvoiceEvent> InvoiceEvents => Set<InvoiceEvent>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteItem> QuoteItems => Set<QuoteItem>();
    public DbSet<RecurringSchedule> RecurringSchedules => Set<RecurringSchedule>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    // Booking System
    public DbSet<BookingBusiness> BookingBusinesses => Set<BookingBusiness>();
    public DbSet<BusinessHours> BusinessHours => Set<BusinessHours>();
    public DbSet<BookableService> BookableServices => Set<BookableService>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<TimeOff> TimeOffs => Set<TimeOff>();
    public DbSet<BookingCustomer> BookingCustomers => Set<BookingCustomer>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingPayment> BookingPayments => Set<BookingPayment>();
    public DbSet<BookingStatusHistory> BookingStatusHistory => Set<BookingStatusHistory>();
    public DbSet<BookingReminder> BookingReminders => Set<BookingReminder>();

    // Online Course Platform
    public DbSet<Academy> Academies => Set<Academy>();
    public DbSet<CourseCategory> CourseCategories => Set<CourseCategory>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseSection> CourseSections => Set<CourseSection>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<LessonResource> LessonResources => Set<LessonResource>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();
    public DbSet<LessonNote> LessonNotes => Set<LessonNote>();
    public DbSet<LessonQuestion> LessonQuestions => Set<LessonQuestion>();
    public DbSet<LessonAnswer> LessonAnswers => Set<LessonAnswer>();
    public DbSet<CourseReview> CourseReviews => Set<CourseReview>();
    public DbSet<CourseCoupon> CourseCoupons => Set<CourseCoupon>();
    public DbSet<CourseOrder> CourseOrders => Set<CourseOrder>();
    public DbSet<Earning> Earnings => Set<Earning>();
    public DbSet<Payout> Payouts => Set<Payout>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<SchoolProfile> SchoolProfiles => Set<SchoolProfile>();
    public DbSet<StaffProfile> StaffProfiles => Set<StaffProfile>();
    public DbSet<AcademicSession> AcademicSessions => Set<AcademicSession>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<Level> Levels => Set<Level>();
    public DbSet<SchoolClass> SchoolClasses => Set<SchoolClass>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<ClassSubject> ClassSubjects => Set<ClassSubject>();
    public DbSet<AssessmentComponent> AssessmentComponents => Set<AssessmentComponent>();
    public DbSet<GradingBand> GradingBands => Set<GradingBand>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Enrolment> Enrolments => Set<Enrolment>();
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();
    public DbSet<GuardianInvite> GuardianInvites => Set<GuardianInvite>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<AttendanceRegister> AttendanceRegisters => Set<AttendanceRegister>();
    public DbSet<AttendanceMark> AttendanceMarks => Set<AttendanceMark>();
    public DbSet<AbsenceAlert> AbsenceAlerts => Set<AbsenceAlert>();
    public DbSet<Score> Scores => Set<Score>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentSubmission> AssignmentSubmissions => Set<AssignmentSubmission>();
    public DbSet<LessonPlan> LessonPlans => Set<LessonPlan>();
    public DbSet<ExamPeriod> ExamPeriods => Set<ExamPeriod>();
    public DbSet<ExamSitting> ExamSittings => Set<ExamSitting>();
    public DbSet<BankQuestion> BankQuestions => Set<BankQuestion>();
    public DbSet<CbtPaper> CbtPapers => Set<CbtPaper>();
    public DbSet<CbtPaperPick> CbtPaperPicks => Set<CbtPaperPick>();
    public DbSet<CbtAttempt> CbtAttempts => Set<CbtAttempt>();
    public DbSet<CbtAttemptItem> CbtAttemptItems => Set<CbtAttemptItem>();
    public DbSet<WordList> WordLists => Set<WordList>();
    public DbSet<BrainTeaser> BrainTeasers => Set<BrainTeaser>();
    public DbSet<TermResultLock> TermResultLocks => Set<TermResultLock>();
    public DbSet<SubjectResult> SubjectResults => Set<SubjectResult>();
    public DbSet<CommentBankEntry> CommentBankEntries => Set<CommentBankEntry>();
    public DbSet<TraitCriterion> TraitCriteria => Set<TraitCriterion>();
    public DbSet<TraitRating> TraitRatings => Set<TraitRating>();
    public DbSet<DevelopmentalArea> DevelopmentalAreas => Set<DevelopmentalArea>();
    public DbSet<DevelopmentalScale> DevelopmentalScales => Set<DevelopmentalScale>();
    public DbSet<DevelopmentalRating> DevelopmentalRatings => Set<DevelopmentalRating>();
    public DbSet<TermResult> TermResults => Set<TermResult>();
    public DbSet<ResultsNotice> ResultsNotices => Set<ResultsNotice>();
    public DbSet<ReportCardBundle> ReportCardBundles => Set<ReportCardBundle>();
    public DbSet<FeeItem> FeeItems => Set<FeeItem>();
    public DbSet<FeeStructureLine> FeeStructureLines => Set<FeeStructureLine>();
    public DbSet<StudentInvoice> StudentInvoices => Set<StudentInvoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Instalment> Instalments => Set<Instalment>();
    public DbSet<FeePayment> FeePayments => Set<FeePayment>();
    public DbSet<FeeReminder> FeeReminders => Set<FeeReminder>();
    public DbSet<SchoolExpense> SchoolExpenses => Set<SchoolExpense>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementDelivery> AnnouncementDeliveries => Set<AnnouncementDelivery>();
    public DbSet<MessageThread> MessageThreads => Set<MessageThread>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<TimetableSlot> TimetableSlots => Set<TimetableSlot>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<PropertyType> PropertyTypes => Set<PropertyType>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<SavedListing> SavedListings => Set<SavedListing>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<AgencyProfile> AgencyProfiles => Set<AgencyProfile>();
    public DbSet<AgentProfile> AgentProfiles => Set<AgentProfile>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingCost> ListingCosts => Set<ListingCost>();
    public DbSet<ListingAmenity> ListingAmenities => Set<ListingAmenity>();
    public DbSet<ListingMedia> ListingMedia => Set<ListingMedia>();
    public DbSet<Enquiry> Enquiries => Set<Enquiry>();
    public DbSet<InspectionSlot> InspectionSlots => Set<InspectionSlot>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<ListingStatDay> ListingStats => Set<ListingStatDay>();
    public DbSet<VerificationRequest> VerificationRequests => Set<VerificationRequest>();
    public DbSet<ListingReport> ListingReports => Set<ListingReport>();
    public DbSet<AgentReview> AgentReviews => Set<AgentReview>();
    public DbSet<Boost> Boosts => Set<Boost>();
    public DbSet<StayBlock> StayBlocks => Set<StayBlock>();
    public DbSet<Stay> Stays => Set<Stay>();
    public DbSet<Saas.Domain.Care.ClinicProfile> ClinicProfiles => Set<Saas.Domain.Care.ClinicProfile>();
    public DbSet<Saas.Domain.Care.CareStaffProfile> CareStaffProfiles => Set<Saas.Domain.Care.CareStaffProfile>();
    public DbSet<Saas.Domain.Care.PriceItem> PriceItems => Set<Saas.Domain.Care.PriceItem>();
    public DbSet<Saas.Domain.Care.Patient> Patients => Set<Saas.Domain.Care.Patient>();
    public DbSet<Saas.Domain.Care.PatientBill> PatientBills => Set<Saas.Domain.Care.PatientBill>();
    public DbSet<Saas.Domain.Care.BillLine> BillLines => Set<Saas.Domain.Care.BillLine>();
    public DbSet<Saas.Domain.Care.CareAssignment> CareAssignments => Set<Saas.Domain.Care.CareAssignment>();
    public DbSet<Saas.Domain.Care.RecordAccess> RecordAccesses => Set<Saas.Domain.Care.RecordAccess>();
    public DbSet<Saas.Domain.Care.DoctorSchedule> DoctorSchedules => Set<Saas.Domain.Care.DoctorSchedule>();
    public DbSet<Saas.Domain.Care.Appointment> Appointments => Set<Saas.Domain.Care.Appointment>();
    public DbSet<Saas.Domain.Care.Visit> Visits => Set<Saas.Domain.Care.Visit>();
    public DbSet<Saas.Domain.Care.VitalSigns> VitalSigns => Set<Saas.Domain.Care.VitalSigns>();
    public DbSet<Saas.Domain.Care.Consultation> Consultations => Set<Saas.Domain.Care.Consultation>();
    public DbSet<Saas.Domain.Care.Diagnosis> Diagnoses => Set<Saas.Domain.Care.Diagnosis>();
    public DbSet<Saas.Domain.Care.Addendum> Addenda => Set<Saas.Domain.Care.Addendum>();
    public DbSet<Saas.Domain.Care.Prescription> Prescriptions => Set<Saas.Domain.Care.Prescription>();
    public DbSet<Saas.Domain.Care.PrescriptionItem> PrescriptionItems => Set<Saas.Domain.Care.PrescriptionItem>();
    public DbSet<Saas.Domain.Care.TestRequest> TestRequests => Set<Saas.Domain.Care.TestRequest>();
    public DbSet<Saas.Domain.Care.Ward> Wards => Set<Saas.Domain.Care.Ward>();
    public DbSet<Saas.Domain.Care.Bed> Beds => Set<Saas.Domain.Care.Bed>();
    public DbSet<Saas.Domain.Care.Admission> Admissions => Set<Saas.Domain.Care.Admission>();
    public DbSet<Saas.Domain.Care.BedMove> BedMoves => Set<Saas.Domain.Care.BedMove>();
    public DbSet<Saas.Domain.Care.CarePayment> CarePayments => Set<Saas.Domain.Care.CarePayment>();
    public DbSet<Saas.Domain.Care.BillAdjustment> BillAdjustments => Set<Saas.Domain.Care.BillAdjustment>();
    public DbSet<Saas.Domain.Care.NursingNote> NursingNotes => Set<Saas.Domain.Care.NursingNote>();
    public DbSet<Saas.Domain.Care.FluidEntry> FluidEntries => Set<Saas.Domain.Care.FluidEntry>();
    public DbSet<Saas.Domain.Care.WardRound> WardRounds => Set<Saas.Domain.Care.WardRound>();
    public DbSet<Saas.Domain.Care.MedicationDose> MedicationDoses => Set<Saas.Domain.Care.MedicationDose>();
    public DbSet<Saas.Domain.Care.DischargeSummary> DischargeSummaries => Set<Saas.Domain.Care.DischargeSummary>();
    public DbSet<Saas.Domain.Care.PatientLink> PatientLinks => Set<Saas.Domain.Care.PatientLink>();
    public DbSet<Saas.Domain.Care.PatientInvite> PatientInvites => Set<Saas.Domain.Care.PatientInvite>();

    /// <summary>Read by the global query filters on every query; null means "no tenant", which matches no rows.</summary>
    public Guid? CurrentTenantId => tenant.TenantId;

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(30);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>(e =>
        {
            e.ToTable("users");
            e.Property(u => u.FullName).HasMaxLength(100);
            e.Property(u => u.AvatarUrl).HasMaxLength(500);
            e.HasIndex(u => u.PhoneNumber);
        });
        b.Entity<IdentityRole<Guid>>().ToTable("roles");
        b.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        b.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        b.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        b.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        b.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        b.Entity<Tenant>(e =>
        {
            e.HasIndex(t => t.Slug).IsUnique();
            e.HasIndex(t => t.CustomDomain).IsUnique().HasFilter("custom_domain IS NOT NULL");
            e.Property(t => t.Country).HasMaxLength(2);
            e.Property(t => t.Currency).HasMaxLength(3);
        });
        b.Entity<Membership>(e =>
        {
            e.HasIndex(m => new { m.TenantId, m.UserId }).IsUnique();
            e.HasIndex(m => m.UserId);
            e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.Tenant).WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<RolePermission>().HasIndex(r => new { r.TenantId, r.Role, r.PermissionKey }).IsUnique();
        b.Entity<PlatformAdmin>().HasIndex(x => x.UserId).IsUnique();
        b.Entity<AuthSession>().HasIndex(x => new { x.UserId, x.Kind, x.EndedAt });
        b.Entity<JobRun>(e =>
        {
            e.HasKey(x => x.Name);
            e.Property(x => x.Name).HasMaxLength(60);
        });
        b.Entity<Plan>(e =>
        {
            e.HasIndex(p => new { p.Product, p.Code }).IsUnique();
            e.Property(p => p.Limits).HasColumnType("jsonb").HasConversion(JsonConverter<Dictionary<string, long>>(), JsonComparer<Dictionary<string, long>>());
        });
        b.Entity<Subscription>(e =>
        {
            e.HasIndex(s => new { s.TenantId, s.Status });
            e.HasOne(s => s.Plan).WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.Property(s => s.Limits).HasColumnType("jsonb").HasConversion(JsonConverter<Dictionary<string, long>>(), JsonComparer<Dictionary<string, long>>());
        });
        b.Entity<Payment>(e =>
        {
            e.HasIndex(p => p.ProviderRef).IsUnique();
            e.Property(p => p.Metadata).HasColumnType("jsonb").HasConversion(JsonConverter<Dictionary<string, string>>(), JsonComparer<Dictionary<string, string>>());
        });
        b.Entity<Notification>(e =>
        {
            e.HasIndex(n => new { n.Status, n.CreatedAt });
            e.Property(n => n.Payload).HasColumnType("jsonb").HasConversion(JsonConverter<Dictionary<string, string>>(), JsonComparer<Dictionary<string, string>>());
        });
        b.Entity<UserNotification>(e =>
        {
            e.HasIndex(n => new { n.UserId, n.ReadAt, n.CreatedAt }).IsDescending(false, false, true);
            e.Property(n => n.Product).HasMaxLength(30);
            e.Property(n => n.Kind).HasMaxLength(60);
            e.Property(n => n.Title).HasMaxLength(200);
            e.Property(n => n.Body).HasMaxLength(1000);
            e.Property(n => n.Link).HasMaxLength(500);
            e.HasOne<AppUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Tenant>().WithMany().HasForeignKey(n => n.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(a => new { a.TenantId, a.CreatedAt });
            e.Property(a => a.BeforeJson).HasColumnType("jsonb");
            e.Property(a => a.AfterJson).HasColumnType("jsonb");
        });
        b.Entity<OtpCode>().HasIndex(o => new { o.Target, o.Purpose, o.CreatedAt });
        b.Entity<RefreshToken>().HasIndex(r => r.TokenHash).IsUnique();

        b.Entity<Store>(e =>
        {
            e.HasIndex(s => s.Slug).IsUnique();
            e.HasIndex(s => s.TenantId);
            e.Property(s => s.Slug).HasMaxLength(60);
            e.Property(s => s.Currency).HasMaxLength(3);
            e.Property(s => s.ThemeColor).HasMaxLength(7);
        });
        b.Entity<Category>().HasIndex(c => new { c.TenantId, c.StoreId });
        b.Entity<Product>(e =>
        {
            e.HasIndex(p => new { p.StoreId, p.Slug }).IsUnique();
            e.HasIndex(p => new { p.TenantId, p.IsActive });
            e.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(p => p.Images).WithOne().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Options).WithOne().HasForeignKey(o => o.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Variants).WithOne().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ProductVariant>().Ignore(v => v.Title);
        b.Entity<Product>().Ignore(p => p.IsAvailable);
        b.Entity<DeliveryZone>().HasIndex(z => new { z.TenantId, z.StoreId });
        b.Entity<DiscountCode>().HasIndex(d => new { d.StoreId, d.Code }).IsUnique();
        b.Entity<Customer>().HasIndex(c => new { c.StoreId, c.Phone }).IsUnique();
        b.Entity<Order>(e =>
        {
            e.HasIndex(o => o.Code).IsUnique();
            e.HasIndex(o => new { o.StoreId, o.Number }).IsUnique();
            e.HasIndex(o => new { o.TenantId, o.Status, o.CreatedAt });
            e.Property(o => o.Code).HasMaxLength(12);
            e.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(o => o.History).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureInvoices(b);
        ConfigureBookings(b);
        ConfigureCourses(b);
        ConfigureProperty(b);
        ConfigureSchool(b);

        // Ids are Guid v7 values set in code. Telling EF they are never generated makes it treat new children
        // added to a tracked collection (product.Images.Add) as inserts instead of updates.
        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
            b.Entity(type.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();

        // Tenant entities hang off the tenants table, so deleting a tenant removes its data.
        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(ITenantEntity).IsAssignableFrom(t.ClrType)).ToList())
        {
            if (type.ClrType != typeof(Membership))
                b.Entity(type.ClrType).HasOne(typeof(Tenant)).WithMany().HasForeignKey(nameof(ITenantEntity.TenantId)).OnDelete(DeleteBehavior.Cascade);
            b.Entity(type.ClrType).HasQueryFilter(TenantFilter(type.ClrType));
        }
    }

    private static void ConfigureSchool(ModelBuilder b)
    {
        b.Entity<SchoolProfile>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.TenantId).IsUnique();
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Slug).HasMaxLength(60);
            e.Property(x => x.Motto).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(300);
            // Existing schools keep review on, and keep Play on.
            e.Property(x => x.LessonPlansNeedReview).HasDefaultValue(true);
            e.Property(x => x.PlayEnabled).HasDefaultValue(true);
        });
        b.Entity<StaffProfile>(e =>
        {
            e.HasIndex(x => x.MembershipId).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.StaffNo }).IsUnique().HasFilter("staff_no is not null");
            e.HasOne<Membership>().WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.StaffNo).HasMaxLength(30);
            e.Property(x => x.JobTitle).HasMaxLength(80);
            e.Property(x => x.Phone).HasMaxLength(30);
        });
        b.Entity<AcademicSession>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            // One current session per school.
            e.HasIndex(x => x.TenantId).HasFilter("is_current").IsUnique().HasDatabaseName("ix_academic_sessions_current");
            e.Property(x => x.Name).HasMaxLength(20);
        });
        b.Entity<Term>(e =>
        {
            e.HasIndex(x => new { x.SessionId, x.Number }).IsUnique();
            // One current term per school.
            e.HasIndex(x => x.TenantId).HasFilter("is_current").IsUnique().HasDatabaseName("ix_terms_current");
            e.HasOne<AcademicSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(40);
        });
        b.Entity<Level>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(40);
            // Existing rows stay numeric. Primary logins are a template default only, not a backfill.
            e.Property(x => x.AssessmentMode).HasDefaultValue(AssessmentMode.Numeric);
        });
        b.Entity<SchoolClass>(e =>
        {
            e.HasIndex(x => new { x.SessionId, x.LevelId, x.Arm }).IsUnique();
            e.HasIndex(x => new { x.SessionId, x.Name }).IsUnique();
            e.HasOne<AcademicSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Level>().WithMany().HasForeignKey(x => x.LevelId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StaffProfile>().WithMany().HasForeignKey(x => x.ClassTeacherStaffId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Arm).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(60);
        });
        b.Entity<Subject>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Code).HasMaxLength(10);
        });
        b.Entity<ClassSubject>(e =>
        {
            e.HasIndex(x => new { x.ClassId, x.SubjectId }).IsUnique();
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StaffProfile>().WithMany().HasForeignKey(x => x.TeacherStaffId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<AssessmentComponent>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.LevelId });
            e.HasOne<Level>().WithMany().HasForeignKey(x => x.LevelId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(40);
            e.Property(x => x.MaxScore).HasPrecision(5, 1);
        });
        b.Entity<GradingBand>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.LevelId });
            e.HasOne<Level>().WithMany().HasForeignKey(x => x.LevelId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Grade).HasMaxLength(10);
            e.Property(x => x.Remark).HasMaxLength(60);
            e.Property(x => x.Min).HasPrecision(4, 1);
            e.Property(x => x.Max).HasPrecision(4, 1);
        });
        b.Entity<Student>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.AdmissionNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.Property(x => x.AdmissionNo).HasMaxLength(30);
            e.Property(x => x.FirstName).HasMaxLength(60);
            e.Property(x => x.MiddleName).HasMaxLength(60);
            e.Property(x => x.LastName).HasMaxLength(60);
            e.Property(x => x.PhotoKey).HasMaxLength(300);
            e.Property(x => x.StatusReason).HasMaxLength(300);
        });
        b.Entity<Enrolment>(e =>
        {
            e.HasIndex(x => new { x.StudentId, x.SessionId }).IsUnique();
            e.HasIndex(x => x.ClassId);
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AcademicSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Guardian>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Phone });
            e.HasIndex(x => new { x.TenantId, x.Email });
            // The parent portal finds a parent's guardian rows across schools.
            e.HasIndex(x => x.UserId).HasFilter("user_id is not null");
            e.Property(x => x.FullName).HasMaxLength(120);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(200);
        });
        b.Entity<StudentGuardian>(e =>
        {
            e.HasIndex(x => new { x.StudentId, x.GuardianId }).IsUnique();
            e.HasIndex(x => x.GuardianId);
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Guardian>().WithMany().HasForeignKey(x => x.GuardianId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Relationship).HasMaxLength(40);
        });
        b.Entity<GuardianInvite>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.GuardianId);
            e.HasOne<Guardian>().WithMany().HasForeignKey(x => x.GuardianId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.TokenHash).HasMaxLength(64);
        });
        b.Entity<AttendanceRegister>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.ClassId, x.Date }).IsUnique();
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<StaffProfile>().WithMany().HasForeignKey(x => x.TakenByStaffId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.ClientId).HasMaxLength(80);
        });
        b.Entity<AttendanceMark>(e =>
        {
            e.HasIndex(x => new { x.RegisterId, x.EnrolmentId }).IsUnique();
            e.HasIndex(x => x.EnrolmentId);
            e.HasOne<AttendanceRegister>().WithMany().HasForeignKey(x => x.RegisterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Enrolment>().WithMany().HasForeignKey(x => x.EnrolmentId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Note).HasMaxLength(200);
        });
        b.Entity<AbsenceAlert>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.StudentId, x.Date }).IsUnique();
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Score>(e =>
        {
            e.HasIndex(x => new { x.EnrolmentId, x.ClassSubjectId, x.TermId, x.ComponentId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ClassSubjectId, x.TermId });
            e.HasOne<Enrolment>().WithMany().HasForeignKey(x => x.EnrolmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ClassSubject>().WithMany().HasForeignKey(x => x.ClassSubjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AssessmentComponent>().WithMany().HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StaffProfile>().WithMany().HasForeignKey(x => x.EnteredByStaffId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Value).HasPrecision(5, 1);
        });
        b.Entity<Assignment>(e =>
        {
            e.HasIndex(x => new { x.ClassSubjectId, x.TermId });
            e.HasOne<ClassSubject>().WithMany().HasForeignKey(x => x.ClassSubjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AssessmentComponent>().WithMany().HasForeignKey(x => x.AssessmentComponentId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Title).HasMaxLength(150);
            e.Property(x => x.Instructions).HasMaxLength(5000);
            e.Property(x => x.MaxMarks).HasPrecision(6, 1);
        });
        b.Entity<AssignmentSubmission>(e =>
        {
            e.HasIndex(x => new { x.AssignmentId, x.EnrolmentId }).IsUnique();
            e.HasIndex(x => x.EnrolmentId);
            e.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Enrolment>().WithMany().HasForeignKey(x => x.EnrolmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<StaffProfile>().WithMany().HasForeignKey(x => x.MarkedByStaffId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Body).HasMaxLength(5000);
            e.Property(x => x.FileKey).HasMaxLength(300);
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.FileType).HasMaxLength(100);
            e.Property(x => x.Mark).HasPrecision(6, 1);
            e.Property(x => x.Feedback).HasMaxLength(500);
        });
        b.Entity<LessonPlan>(e =>
        {
            e.HasIndex(x => new { x.ClassSubjectId, x.TermId });
            e.HasIndex(x => x.AssignmentId);
            e.HasOne<ClassSubject>().WithMany().HasForeignKey(x => x.ClassSubjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Topic).HasMaxLength(200);
            e.Property(x => x.Objectives).HasMaxLength(4000);
            e.Property(x => x.Materials).HasMaxLength(4000);
            e.Property(x => x.Methods).HasMaxLength(4000);
            e.Property(x => x.Activities).HasMaxLength(4000);
            e.Property(x => x.PreviousKnowledge).HasMaxLength(4000);
            e.Property(x => x.Introduction).HasMaxLength(4000);
            e.Property(x => x.Presentation).HasMaxLength(4000);
            e.Property(x => x.Conclusion).HasMaxLength(4000);
            e.Property(x => x.Evaluation).HasMaxLength(4000);
            e.Property(x => x.TeacherRemarks).HasMaxLength(2000);
            e.Property(x => x.ReviewerNote).HasMaxLength(2000);
        });
        b.Entity<ExamPeriod>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.TermId });
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(120);
        });
        b.Entity<ExamSitting>(e =>
        {
            e.HasIndex(x => new { x.PeriodId, x.Date });
            e.HasIndex(x => new { x.TenantId, x.Date });
            e.HasIndex(x => x.ClassSubjectId);
            e.HasOne<ExamPeriod>().WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ClassSubject>().WithMany().HasForeignKey(x => x.ClassSubjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<StaffProfile>().WithMany().HasForeignKey(x => x.InvigilatorStaffId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Venue).HasMaxLength(200);
            e.Property(x => x.Instructions).HasMaxLength(4000);
        });
        b.Entity<BankQuestion>(e =>
        {
            e.HasIndex(x => new { x.SubjectId, x.LevelId });
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Level>().WithMany().HasForeignKey(x => x.LevelId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Stem).HasMaxLength(2000);
            e.Property(x => x.Options).HasColumnType("jsonb").HasConversion(JsonConverter<List<string>>(), JsonComparer<List<string>>());
            e.Property(x => x.Marks).HasPrecision(6, 1);
            e.Property(x => x.Archived).HasDefaultValue(false);
        });
        b.Entity<CbtPaper>(e =>
        {
            e.HasIndex(x => new { x.ClassSubjectId, x.TermId });
            e.HasIndex(x => x.ExamSittingId);
            e.HasOne<ClassSubject>().WithMany().HasForeignKey(x => x.ClassSubjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ExamSitting>().WithMany().HasForeignKey(x => x.ExamSittingId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<AssessmentComponent>().WithMany().HasForeignKey(x => x.AssessmentComponentId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Title).HasMaxLength(150);
            e.Property(x => x.DrawCount).HasDefaultValue(0);
        });
        b.Entity<CbtPaperPick>(e =>
        {
            e.HasIndex(x => new { x.PaperId, x.BankQuestionId }).IsUnique();
            e.HasIndex(x => x.BankQuestionId);
            e.HasOne<CbtPaper>().WithMany().HasForeignKey(x => x.PaperId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BankQuestion>().WithMany().HasForeignKey(x => x.BankQuestionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CbtAttempt>(e =>
        {
            e.HasIndex(x => new { x.PaperId, x.EnrolmentId }).IsUnique().HasFilter("submitted_at IS NULL AND reopened_at IS NULL");
            e.HasIndex(x => x.EnrolmentId);
            e.HasOne<CbtPaper>().WithMany().HasForeignKey(x => x.PaperId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Enrolment>().WithMany().HasForeignKey(x => x.EnrolmentId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.TotalMarks).HasPrecision(8, 1).HasDefaultValue(0m);
            e.Property(x => x.AutoMarked).HasDefaultValue(false);
        });
        b.Entity<CbtAttemptItem>(e =>
        {
            e.HasIndex(x => new { x.AttemptId, x.BankQuestionId }).IsUnique();
            e.HasIndex(x => new { x.AttemptId, x.Order }).IsUnique();
            e.HasOne<CbtAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BankQuestion>().WithMany().HasForeignKey(x => x.BankQuestionId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Stem).HasMaxLength(2000);
            e.Property(x => x.Options).HasColumnType("jsonb").HasConversion(JsonConverter<List<string>>(), JsonComparer<List<string>>());
            e.Property(x => x.OptionOrder).HasColumnType("jsonb").HasConversion(JsonConverter<List<int>>(), JsonComparer<List<int>>());
            e.Property(x => x.Marks).HasPrecision(6, 1);
            e.Property(x => x.MarksAwarded).HasPrecision(6, 1);
        });
        b.Entity<WordList>(e =>
        {
            e.HasIndex(x => x.ClassSubjectId);
            e.HasIndex(x => new { x.TenantId, x.LevelId });
            e.HasOne<ClassSubject>().WithMany().HasForeignKey(x => x.ClassSubjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Level>().WithMany().HasForeignKey(x => x.LevelId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Words).HasColumnType("jsonb").HasConversion(JsonConverter<List<string>>(), JsonComparer<List<string>>());
        });
        b.Entity<BrainTeaser>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.LevelId });
            e.HasOne<Level>().WithMany().HasForeignKey(x => x.LevelId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Prompt).HasMaxLength(500);
            e.Property(x => x.Answer).HasMaxLength(200);
            e.Property(x => x.Archived).HasDefaultValue(false);
        });
        b.Entity<TermResultLock>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.ClassId, x.TermId }).IsUnique();
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SubjectResult>(e =>
        {
            e.HasIndex(x => new { x.EnrolmentId, x.ClassSubjectId, x.TermId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ClassId, x.TermId });
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Enrolment>().WithMany().HasForeignKey(x => x.EnrolmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ClassSubject>().WithMany().HasForeignKey(x => x.ClassSubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Total).HasPrecision(6, 1);
            e.Property(x => x.ClassAverage).HasPrecision(6, 2);
            e.Property(x => x.Grade).HasMaxLength(20);
            e.Property(x => x.Remark).HasMaxLength(500);
        });
        b.Entity<CommentBankEntry>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Scope, x.MatchKey, x.Order }).IsUnique();
            e.Property(x => x.MatchKey).HasMaxLength(40);
            e.Property(x => x.Text).HasMaxLength(500);
        });
        b.Entity<TraitCriterion>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.LevelId, x.Kind });
            e.HasOne<Level>().WithMany().HasForeignKey(x => x.LevelId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(40);
        });
        b.Entity<TraitRating>(e =>
        {
            e.HasIndex(x => new { x.EnrolmentId, x.TermId, x.CriterionId }).IsUnique();
            e.HasOne<Enrolment>().WithMany().HasForeignKey(x => x.EnrolmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TraitCriterion>().WithMany().HasForeignKey(x => x.CriterionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<DevelopmentalArea>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.LevelId });
            e.HasOne<Level>().WithMany().HasForeignKey(x => x.LevelId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(40);
        });
        b.Entity<DevelopmentalScale>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.LevelId });
            e.HasOne<Level>().WithMany().HasForeignKey(x => x.LevelId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Label).HasMaxLength(40);
        });
        b.Entity<DevelopmentalRating>(e =>
        {
            e.HasIndex(x => new { x.EnrolmentId, x.TermId, x.AreaId }).IsUnique();
            e.HasOne<Enrolment>().WithMany().HasForeignKey(x => x.EnrolmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<DevelopmentalArea>().WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DevelopmentalScale>().WithMany().HasForeignKey(x => x.ScaleId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Comment).HasMaxLength(500);
        });
        b.Entity<TermResult>(e =>
        {
            e.HasIndex(x => new { x.EnrolmentId, x.ClassId, x.TermId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ClassId, x.TermId });
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Enrolment>().WithMany().HasForeignKey(x => x.EnrolmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Total).HasPrecision(8, 1);
            e.Property(x => x.Average).HasPrecision(6, 2);
            e.Property(x => x.TeacherRemark).HasMaxLength(500);
            e.Property(x => x.PrincipalRemark).HasMaxLength(500);
        });
        b.Entity<ResultsNotice>(e =>
        {
            e.HasIndex(x => new { x.StudentId, x.TermId }).IsUnique();
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ReportCardBundle>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.ClassId, x.TermId });
            e.HasIndex(x => x.CreatedAt);
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.FileKey).HasMaxLength(300);
        });
        b.Entity<FeeItem>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(80);
        });
        b.Entity<FeeStructureLine>(e =>
        {
            e.HasIndex(x => new { x.TermId, x.LevelId, x.FeeItemId }).IsUnique();
            e.HasIndex(x => x.FeeItemId);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Level>().WithMany().HasForeignKey(x => x.LevelId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<FeeItem>().WithMany().HasForeignKey(x => x.FeeItemId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StudentInvoice>(e =>
        {
            e.HasIndex(x => new { x.EnrolmentId, x.TermId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.TermId, x.Status });
            e.HasOne<Enrolment>().WithMany().HasForeignKey(x => x.EnrolmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        });
        b.Entity<InvoiceLine>(e =>
        {
            e.HasIndex(x => x.InvoiceId);
            e.HasOne<StudentInvoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<FeeItem>().WithMany().HasForeignKey(x => x.FeeItemId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.DiscountKind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.DiscountValue).HasPrecision(18, 4);
        });
        b.Entity<Instalment>(e =>
        {
            e.HasIndex(x => x.InvoiceId);
            e.HasOne<StudentInvoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<FeePayment>(e =>
        {
            e.HasIndex(x => x.InvoiceId);
            e.HasIndex(x => x.ProviderRef).IsUnique().HasFilter("provider_ref IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.ReceiptNumber }).IsUnique().HasFilter("receipt_number IS NOT NULL");
            // Money received: an invoice with payments can't be deleted out from under them.
            e.HasOne<StudentInvoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.ChargedCurrency).HasMaxLength(3);
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.ProviderRef).HasMaxLength(60);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.ReceiptNumber).HasMaxLength(30);
            e.Property(x => x.ReversalReason).HasMaxLength(300);
        });
        b.Entity<FeeReminder>(e =>
        {
            // One reminder per invoice per school day: a rerun of the daily job can't send twice.
            e.HasIndex(x => new { x.InvoiceId, x.SentOn }).IsUnique();
            e.HasOne<StudentInvoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        });
        b.Entity<SchoolExpense>(e =>
        {
            e.ToTable("school_expenses");
            e.HasIndex(x => new { x.TenantId, x.Date });
            e.Property(x => x.Category).HasMaxLength(60);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.ReceiptKey).HasMaxLength(300);
        });
        b.Entity<Announcement>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.PostedAt });
            e.Property(x => x.Title).HasMaxLength(150);
            e.Property(x => x.Body).HasMaxLength(5000);
            e.Property(x => x.AttachmentKey).HasMaxLength(300);
            e.Property(x => x.AttachmentName).HasMaxLength(200);
            e.Property(x => x.AttachmentType).HasMaxLength(60);
            e.Property(x => x.Audience).HasConversion<string>().HasMaxLength(10);
        });
        b.Entity<AnnouncementDelivery>(e =>
        {
            e.HasIndex(x => new { x.AnnouncementId, x.GuardianId }).IsUnique();
            // The broadcast job reads the oldest pending rows of a school.
            e.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
            e.HasOne<Announcement>().WithMany().HasForeignKey(x => x.AnnouncementId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Guardian>().WithMany().HasForeignKey(x => x.GuardianId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        });
        b.Entity<MessageThread>(e =>
        {
            e.HasIndex(x => new { x.GuardianId, x.StudentId, x.StaffId }).IsUnique().HasFilter("is_office = false");
            e.HasIndex(x => new { x.GuardianId, x.StudentId }).IsUnique().HasFilter("is_office = true");
            e.HasIndex(x => new { x.TenantId, x.LastMessageAt });
            e.HasIndex(x => x.StudentId);
            e.HasIndex(x => x.StaffId);
            // A thread is part of the student's records: the student and the guardian can't be deleted from under it.
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Guardian>().WithMany().HasForeignKey(x => x.GuardianId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StaffProfile>().WithMany().HasForeignKey(x => x.StaffId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<Message>(e =>
        {
            e.HasIndex(x => new { x.ThreadId, x.CreatedAt });
            e.HasOne<MessageThread>().WithMany().HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Body).HasMaxLength(2000);
            e.Property(x => x.AttachmentKey).HasMaxLength(300);
        });
        b.Entity<TimetableSlot>(e =>
        {
            e.HasIndex(x => new { x.ClassId, x.Day, x.Period }).IsUnique();
            // Clash warnings look up the other classes' slots at the same day and period.
            e.HasIndex(x => new { x.TenantId, x.Day, x.Period });
            e.HasIndex(x => x.ClassSubjectId);
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ClassSubject>().WithMany().HasForeignKey(x => x.ClassSubjectId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Day).HasConversion<string>().HasMaxLength(3);
        });
        b.Entity<ImportBatch>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.ExpiresAt);
            e.Property(x => x.Token).HasMaxLength(64);
            e.Property(x => x.Kind).HasMaxLength(40).HasDefaultValue("students");
            e.Property(x => x.Scope).HasColumnType("jsonb");
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Result).HasColumnType("jsonb");
        });
    }

    private static void ConfigureProperty(ModelBuilder b)
    {
        b.Entity<Location>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.ParentId, x.Position });
            e.Property(x => x.Centre).HasColumnType("geography (point, 4326)");
        });
        b.Entity<PropertyType>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Amenity>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<SavedListing>().HasIndex(x => new { x.UserId, x.ListingId }).IsUnique();
        b.Entity<SavedSearch>(e =>
        {
            e.HasIndex(x => new { x.UserId });
            e.HasIndex(x => new { x.Frequency, x.LastSentAt });
            e.Property(x => x.Filter).HasColumnType("jsonb").HasConversion(ListingFilterConverter(), ListingFilterComparer());
        });

        b.Entity<AgencyProfile>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.TenantId).IsUnique();
            e.Property(x => x.PayoutAccount).HasColumnType("jsonb").HasConversion(JsonConverter<PayoutAccount>(), JsonComparer<PayoutAccount>());
        });
        b.Entity<AgentProfile>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        });
        b.Entity<Listing>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
            e.HasIndex(x => new { x.Status, x.Purpose, x.LocationId, x.PriceMinor });
            e.HasIndex(x => new { x.Status, x.ExpiresAt });
            e.HasIndex(x => x.Point).HasMethod("gist");
            e.Property(x => x.Point).HasColumnType("geography (point, 4326)");
            e.HasMany(x => x.Costs).WithOne().HasForeignKey(c => c.ListingId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Amenities).WithOne().HasForeignKey(a => a.ListingId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ListingAmenity>().HasIndex(x => new { x.ListingId, x.AmenityId }).IsUnique();
        b.Entity<ListingMedia>(e =>
        {
            e.HasIndex(x => new { x.ListingId, x.Position });
            e.HasIndex(x => x.Phash);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });
        b.Entity<Enquiry>().HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
        b.Entity<InspectionSlot>().HasIndex(x => new { x.AgentProfileId, x.Weekday });
        b.Entity<Inspection>(e =>
        {
            // Two seekers racing for one viewing time: only one open booking per agent per time.
            e.HasIndex(x => new { x.AgentProfileId, x.ScheduledAt }).IsUnique().HasFilter("status IN ('Requested', 'Confirmed')")
                .HasDatabaseName("ix_inspections_open_slot");
            e.HasIndex(x => new { x.SeekerUserId });
        });
        b.Entity<ListingStatDay>().HasIndex(x => new { x.ListingId, x.Day }).IsUnique();
        b.Entity<VerificationRequest>(e =>
        {
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Documents).HasColumnType("jsonb")
                .HasConversion(JsonConverter<List<VerificationDocument>>(), JsonComparer<List<VerificationDocument>>());
        });
        b.Entity<ListingReport>(e =>
        {
            e.HasIndex(x => new { x.ListingId, x.ReporterKey }).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });
        b.Entity<AgentReview>(e =>
        {
            e.HasIndex(x => new { x.AgentProfileId, x.UserId }).IsUnique();
            e.HasIndex(x => new { x.Status });
        });
        b.Entity<Boost>(e =>
        {
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.ListingId, x.Status });
        });
        b.Entity<StayBlock>().HasIndex(x => new { x.ListingId, x.Start });
        b.Entity<Stay>(e =>
        {
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.ListingId, x.CheckIn });
            e.HasIndex(x => new { x.GuestUserId });
            e.HasIndex(x => new { x.Status, x.HoldExpiresAt });
        });
    }

    private static ValueConverter<ListingFilter, string> ListingFilterConverter() =>
        new(v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<ListingFilter>(v, JsonSerializerOptions.Default) ?? new ListingFilter());

    private static ValueComparer<ListingFilter> ListingFilterComparer() =>
        new((a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(),
            v => JsonSerializer.Deserialize<ListingFilter>(JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default) ?? new ListingFilter());

    private static void ConfigureCourses(ModelBuilder b)
    {
        b.Entity<Academy>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.TenantId).IsUnique();
            e.Property(x => x.PayoutAccount).HasColumnType("jsonb").HasConversion(JsonConverter<PayoutAccount>(), JsonComparer<PayoutAccount>());
        });
        b.Entity<CourseCategory>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Course>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
            e.HasIndex(x => new { x.Status, x.MarketplaceStatus });
            e.HasMany(x => x.Sections).WithOne().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CourseSection>().HasMany(x => x.Lessons).WithOne().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Lesson>(e =>
        {
            e.HasIndex(x => x.CourseId);
            e.Property(x => x.Quiz).HasColumnType("jsonb").HasConversion(JsonConverter<QuizDefinition>(), JsonComparer<QuizDefinition>());
        });
        b.Entity<Video>(e =>
        {
            e.HasIndex(x => x.LessonId);
            e.HasIndex(x => new { x.Status, x.ProcessingStartedAt });
        });
        b.Entity<LessonResource>().HasIndex(x => x.LessonId);
        b.Entity<QuizAttempt>(e =>
        {
            e.HasIndex(x => new { x.LessonId, x.UserId });
            e.Property(x => x.Answers).HasColumnType("jsonb").HasConversion(JsonConverter<List<QuizAnswer>>(), JsonComparer<List<QuizAnswer>>());
        });
        b.Entity<Enrollment>(e =>
        {
            e.HasIndex(x => new { x.CourseId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
        });
        b.Entity<LessonProgress>().HasIndex(x => new { x.EnrollmentId, x.LessonId }).IsUnique();
        b.Entity<LessonNote>().HasIndex(x => new { x.LessonId, x.UserId }).IsUnique();
        b.Entity<LessonQuestion>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.LessonId });
            e.HasMany(x => x.Answers).WithOne().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CourseReview>().HasIndex(x => new { x.CourseId, x.UserId }).IsUnique();
        b.Entity<CourseCoupon>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.Entity<CourseOrder>(e =>
        {
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CourseId });
        });
        b.Entity<Earning>(e =>
        {
            e.HasIndex(x => new { x.Source, x.SourceId }).IsUnique(); // a sale earns once, even if the webhook and return page race
            e.HasIndex(x => new { x.TenantId, x.AvailableAt });
        });
        b.Entity<Payout>(e =>
        {
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Account).HasColumnType("jsonb").HasConversion(JsonConverter<PayoutAccount>(), JsonComparer<PayoutAccount>());
        });
        b.Entity<Certificate>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.EnrollmentId).IsUnique();
            e.HasIndex(x => x.UserId);
        });
    }

    private static void ConfigureBookings(ModelBuilder b)
    {
        b.Entity<BookingBusiness>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.TenantId).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(60);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.BrandColor).HasMaxLength(7);
        });
        b.Entity<BusinessHours>().HasIndex(x => new { x.TenantId, x.Weekday }).IsUnique();
        b.Entity<BookableService>(e =>
        {
            e.ToTable("services");
            e.Ignore(x => x.DepositMinor);
            e.HasIndex(x => new { x.TenantId, x.IsActive });
        });
        b.Entity<StaffMember>(e =>
        {
            e.HasIndex(x => x.FeedToken).IsUnique();
            e.Property(x => x.Schedule).HasColumnType("jsonb")
                .HasConversion(JsonConverter<List<ScheduleInterval>>(), JsonComparer<List<ScheduleInterval>>());
        });
        b.Entity<TimeOff>().HasIndex(x => new { x.TenantId, x.StaffId, x.StartsAt });
        b.Entity<BookingCustomer>().HasIndex(x => new { x.TenantId, x.Phone }).IsUnique();
        b.Entity<Booking>(e =>
        {
            e.HasIndex(x => x.ManageToken).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.StartsAt });
            e.HasIndex(x => new { x.StaffId, x.BlockedFrom });
            e.HasIndex(x => new { x.Status, x.HoldExpiresAt });
            e.Property(x => x.Code).HasMaxLength(12);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.History).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Reminders).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<BookingPayment>().HasIndex(x => x.ProviderRef).IsUnique().HasFilter("provider_ref IS NOT NULL");
        b.Entity<BookingReminder>().HasIndex(x => new { x.Status, x.SendAt });
    }

    private static void ConfigureInvoices(ModelBuilder b)
    {
        b.Entity<BusinessSettings>(e =>
        {
            e.HasIndex(x => x.TenantId).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.BrandColor).HasMaxLength(7);
        });
        b.Entity<Client>().HasIndex(x => new { x.TenantId, x.Name });
        b.Entity<CatalogItem>().HasIndex(x => new { x.TenantId, x.Name });
        b.Entity<TaxRate>().Property(x => x.RatePercent).HasPrecision(9, 4);

        b.Entity<Invoice>(e =>
        {
            e.OwnsOne(x => x.Client);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => x.PublicToken).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status, x.DueDate });
            e.HasIndex(x => new { x.TenantId, x.ClientId });
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<InvoicePayment>().HasIndex(x => x.ProviderRef).IsUnique().HasFilter("provider_ref IS NOT NULL");
        b.Entity<Quote>(e =>
        {
            e.OwnsOne(x => x.Client);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => x.PublicToken).IsUnique();
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Cascade);
        });
        foreach (var line in new[] { typeof(InvoiceItem), typeof(QuoteItem) })
        {
            b.Entity(line).Property(nameof(DocumentLine.Quantity)).HasPrecision(18, 3);
            b.Entity(line).Property(nameof(DocumentLine.DiscountPercent)).HasPrecision(9, 4);
            b.Entity(line).Property(nameof(DocumentLine.TaxRatePercent)).HasPrecision(9, 4);
        }
        foreach (var doc in new[] { typeof(Invoice), typeof(Quote) })
            b.Entity(doc).Property(nameof(Document.ExchangeRate)).HasPrecision(18, 8);
        b.Entity<InvoicePayment>().Property(x => x.ExchangeRate).HasPrecision(18, 8);
        b.Entity<Expense>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Date });
            e.HasIndex(x => new { x.TenantId, x.Category });
            e.Property(x => x.ExchangeRate).HasPrecision(18, 8);
            e.Property(x => x.Currency).HasMaxLength(3);
        });
        b.Entity<Invitation>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Email });
            e.Property(x => x.ClinicalRole).HasConversion<string>().HasMaxLength(12);
        });

        // CareLink
        b.Entity<Saas.Domain.Care.ClinicProfile>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.TenantId).IsUnique();
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Slug).HasMaxLength(60);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.PatientPrefix).HasMaxLength(6);
        });
        b.Entity<Saas.Domain.Care.CareStaffProfile>(e =>
        {
            e.HasIndex(x => x.MembershipId).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.UserId });
            e.HasOne<Membership>().WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.ClinicalRole).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Title).HasMaxLength(80);
            e.Property(x => x.LicenceNumber).HasMaxLength(40);
        });
        b.Entity<Saas.Domain.Care.PriceItem>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(14);
        });
        b.Entity<Saas.Domain.Care.Patient>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LastName, x.FirstName });
            e.HasIndex(x => new { x.TenantId, x.Phone });
            e.Property(x => x.Number).HasMaxLength(20);
            e.Property(x => x.FirstName).HasMaxLength(60);
            e.Property(x => x.MiddleName).HasMaxLength(60);
            e.Property(x => x.LastName).HasMaxLength(60);
            e.Property(x => x.Sex).HasConversion<string>().HasMaxLength(8);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.NextOfKinName).HasMaxLength(120);
            e.Property(x => x.NextOfKinPhone).HasMaxLength(20);
            e.Property(x => x.NextOfKinRelationship).HasMaxLength(40);
            e.Property(x => x.BloodGroup).HasMaxLength(4);
            e.Property(x => x.Genotype).HasMaxLength(4);
            e.Property(x => x.Allergies).HasColumnType("jsonb").HasConversion(JsonConverter<List<string>>(), JsonComparer<List<string>>());
            e.Property(x => x.Conditions).HasColumnType("jsonb").HasConversion(JsonConverter<List<string>>(), JsonComparer<List<string>>());
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        });
        b.Entity<Saas.Domain.Care.PatientBill>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            // One bill per visit and one per admission: a racing second open hits these and re-reads the winner.
            e.HasIndex(x => new { x.TenantId, x.VisitId }).IsUnique().HasFilter("visit_id IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.AdmissionId }).IsUnique().HasFilter("admission_id IS NOT NULL");
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        });
        b.Entity<Saas.Domain.Care.BillLine>(e =>
        {
            e.HasIndex(x => x.BillId);
            e.HasOne<Saas.Domain.Care.PatientBill>().WithMany().HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.PriceItem>().WithMany().HasForeignKey(x => x.PriceItemId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(8);
            e.Property(x => x.ClinicalRef).HasMaxLength(100);
            e.Property(x => x.Reason).HasMaxLength(300);
            e.Property(x => x.RemovedReason).HasMaxLength(300);
            // One bed line per bill, bed item and Lagos day (Task 13): the backstop that keeps the daily bed charge idempotent.
            e.HasIndex(x => new { x.BillId, x.PriceItemId, x.ChargeDay }).IsUnique().HasFilter("charge_day IS NOT NULL").HasDatabaseName("ux_bill_lines_bed_day");
        });
        b.Entity<Saas.Domain.Care.CareAssignment>(e =>
        {
            // RecordGuard asks "does this staff member have an open assignment for this patient?"
            e.HasIndex(x => new { x.TenantId, x.PatientId, x.StaffUserId });
            // Admission and transfer find the admission's assignments (admitting doctor, ward nurses).
            e.HasIndex(x => x.AdmissionId);
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        });
        b.Entity<Saas.Domain.Care.RecordAccess>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.PatientId, x.At });
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Role).HasMaxLength(20);
            e.Property(x => x.Area).HasMaxLength(16);
            // Reason is stored through ClinicalText (ciphertext), so the column is unbounded text; the plain reason is at most 300 characters.
        });
        b.Entity<Saas.Domain.Care.DoctorSchedule>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.DoctorUserId, x.Weekday });
            e.Property(x => x.Weekday).HasConversion<string>().HasMaxLength(10);
        });
        b.Entity<Saas.Domain.Care.Appointment>(e =>
        {
            // A backstop for the per-doctor booking lock: two live appointments never start at the same instant for one doctor.
            e.HasIndex(x => new { x.TenantId, x.DoctorUserId, x.Start }).IsUnique().HasFilter("status <> 'Cancelled'").HasDatabaseName("ux_appointments_live_slot");
            e.HasIndex(x => new { x.TenantId, x.PatientId, x.Start });
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            // Reason is a short, plain-text, non-clinical booking reason (at most 120 characters), not ClinicalText; it is [AuditIgnore]d.
            e.Property(x => x.Reason).HasMaxLength(120);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(8);
        });
        b.Entity<Saas.Domain.Care.Visit>(e =>
        {
            // The queue reads one Lagos day of arrivals; check-in looks for the patient's open visit.
            e.HasIndex(x => new { x.TenantId, x.ArrivedAt });
            e.HasIndex(x => new { x.TenantId, x.PatientId, x.Stage });
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Stage).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Urgency).HasConversion<string>().HasMaxLength(10);
        });
        b.Entity<Saas.Domain.Care.VitalSigns>(e =>
        {
            // Clinical values (numbers and computed flags, no free text); read only by clinical staff through RecordGuard.
            e.HasIndex(x => new { x.TenantId, x.PatientId, x.TakenAt });
            e.HasIndex(x => x.VisitId);
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.TemperatureC).HasPrecision(4, 1);
            e.Property(x => x.WeightKg).HasPrecision(5, 1);
            e.Property(x => x.HeightCm).HasPrecision(5, 1);
            e.Property(x => x.Bmi).HasPrecision(4, 1);
            e.Property(x => x.Flags).HasColumnType("jsonb").HasConversion(JsonConverter<List<string>>(), JsonComparer<List<string>>());
        });
        b.Entity<Saas.Domain.Care.Consultation>(e =>
        {
            // One consultation per visit (a backstop for the per-visit open lock). Clinical text columns hold ClinicalText ciphertext.
            e.HasIndex(x => new { x.TenantId, x.VisitId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(8);
        });
        b.Entity<Saas.Domain.Care.Diagnosis>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasIndex(x => x.ConsultationId);
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Consultation>().WithMany().HasForeignKey(x => x.ConsultationId).OnDelete(DeleteBehavior.Restrict);
            // Text is ClinicalText ciphertext (unbounded); the code comes from DiagnosisList.
            e.Property(x => x.Code).HasMaxLength(10);
        });
        b.Entity<Saas.Domain.Care.Addendum>(e =>
        {
            // NoteId points at a consultation, ward round or discharge summary by NoteKind, so it has no foreign key.
            e.HasIndex(x => new { x.TenantId, x.NoteKind, x.NoteId });
            e.Property(x => x.NoteKind).HasConversion<string>().HasMaxLength(14);
        });
        b.Entity<Saas.Domain.Care.Prescription>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasIndex(x => x.ConsultationId);
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Consultation>().WithMany().HasForeignKey(x => x.ConsultationId).OnDelete(DeleteBehavior.Restrict);
            // Overridden allergens are ClinicalText ciphertext, one per entry.
            e.HasIndex(x => x.AdmissionId);
            e.HasOne<Saas.Domain.Care.Admission>().WithMany().HasForeignKey(x => x.AdmissionId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.OverriddenAllergens).HasColumnType("jsonb").HasConversion(JsonConverter<List<string>>(), JsonComparer<List<string>>());
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PrescriptionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Saas.Domain.Care.PrescriptionItem>(e =>
        {
            // Drug, strength, dose, route and instructions are ClinicalText ciphertext (unbounded); the frequency is a plain enum.
            e.HasIndex(x => x.PrescriptionId);
            e.HasOne<Saas.Domain.Care.PriceItem>().WithMany().HasForeignKey(x => x.PriceItemId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(6);
        });
        b.Entity<Saas.Domain.Care.TestRequest>(e =>
        {
            // Description and result text are ClinicalText ciphertext (unbounded); the result file is private (signed links only).
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasIndex(x => x.ConsultationId);
            e.HasIndex(x => x.AdmissionId);
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Consultation>().WithMany().HasForeignKey(x => x.ConsultationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Admission>().WithMany().HasForeignKey(x => x.AdmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.PriceItem>().WithMany().HasForeignKey(x => x.PriceItemId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(8);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.ResultFileKey).HasMaxLength(300);
            e.Property(x => x.ResultFileType).HasMaxLength(40);
        });
        b.Entity<Saas.Domain.Care.Ward>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasOne<Saas.Domain.Care.PriceItem>().WithMany().HasForeignKey(x => x.BedPriceItemId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.NurseUserIds).HasColumnType("jsonb").HasConversion(JsonConverter<List<Guid>>(), JsonComparer<List<Guid>>());
        });
        b.Entity<Saas.Domain.Care.Bed>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.WardId, x.Label }).IsUnique();
            e.HasOne<Saas.Domain.Care.Ward>().WithMany().HasForeignKey(x => x.WardId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Label).HasMaxLength(40);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        });
        b.Entity<Saas.Domain.Care.Admission>(e =>
        {
            // Reason, admitting diagnosis, death cause and AMA reason are ClinicalText ciphertext (unbounded).
            e.HasIndex(x => new { x.TenantId, x.PatientId, x.Status });
            // One admission per bed at a time: a backstop for the conditional bed claim.
            e.HasIndex(x => new { x.TenantId, x.BedId }).IsUnique().HasFilter("bed_id IS NOT NULL AND status = 'Admitted'");
            // One open admission per patient: a backstop for the per-patient admit lock.
            e.HasIndex(x => new { x.TenantId, x.PatientId }).IsUnique().HasFilter("status = 'Admitted'").HasDatabaseName("ux_admissions_open_patient");
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Bed>().WithMany().HasForeignKey(x => x.BedId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Consultation>().WithMany().HasForeignKey(x => x.ConsultationId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.DischargeType).HasConversion<string>().HasMaxLength(16);
        });
        b.Entity<Saas.Domain.Care.BedMove>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.AdmissionId, x.At });
            e.HasOne<Saas.Domain.Care.Admission>().WithMany().HasForeignKey(x => x.AdmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Bed>().WithMany().HasForeignKey(x => x.FromBedId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Bed>().WithMany().HasForeignKey(x => x.ToBedId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Saas.Domain.Care.CarePayment>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.ReceiptNumber }).IsUnique();
            e.HasIndex(x => x.BillId);
            e.HasOne<Saas.Domain.Care.PatientBill>().WithMany().HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.ReceiptNumber).HasMaxLength(30);
            // Task 11: online checkouts and desk idempotency keys are unique per clinic (the backstop behind the lock and the claim).
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.ChargedCurrency).HasMaxLength(3);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.ProviderRef).HasMaxLength(100);
            e.Property(x => x.ReversalReason).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.ProviderRef }).IsUnique().HasFilter("provider_ref IS NOT NULL");
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });
        b.Entity<Saas.Domain.Care.BillAdjustment>(e =>
        {
            e.HasIndex(x => x.BillId);
            e.HasOne<Saas.Domain.Care.PatientBill>().WithMany().HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(300);
        });
        b.Entity<Saas.Domain.Care.NursingNote>(e =>
        {
            // Text is ClinicalText ciphertext (unbounded).
            e.HasIndex(x => new { x.TenantId, x.AdmissionId, x.At });
            e.HasOne<Saas.Domain.Care.Admission>().WithMany().HasForeignKey(x => x.AdmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Saas.Domain.Care.FluidEntry>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.AdmissionId, x.At });
            e.HasOne<Saas.Domain.Care.Admission>().WithMany().HasForeignKey(x => x.AdmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(4);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(8);
        });
        b.Entity<Saas.Domain.Care.WardRound>(e =>
        {
            // Note is ClinicalText ciphertext (unbounded).
            e.HasIndex(x => new { x.TenantId, x.AdmissionId });
            e.HasOne<Saas.Domain.Care.Admission>().WithMany().HasForeignKey(x => x.AdmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(8);
        });
        b.Entity<Saas.Domain.Care.MedicationDose>(e =>
        {
            // The chart reads one admission's doses by due time; Task 13's missed-dose job scans Due doses by due time. Note is ClinicalText.
            e.HasIndex(x => new { x.TenantId, x.AdmissionId, x.DueAt });
            e.HasIndex(x => new { x.Status, x.DueAt });
            e.HasIndex(x => x.PrescriptionItemId);
            e.HasOne<Saas.Domain.Care.Admission>().WithMany().HasForeignKey(x => x.AdmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Prescription>().WithMany().HasForeignKey(x => x.PrescriptionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.PrescriptionItem>().WithMany().HasForeignKey(x => x.PrescriptionItemId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(8);
        });
        b.Entity<Saas.Domain.Care.DischargeSummary>(e =>
        {
            // Diagnoses, treatment, condition and take-home instructions are ClinicalText ciphertext (unbounded). One per admission.
            e.HasIndex(x => new { x.TenantId, x.AdmissionId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasOne<Saas.Domain.Care.Admission>().WithMany().HasForeignKey(x => x.AdmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(8);
        });
        b.Entity<Saas.Domain.Care.PatientLink>(e =>
        {
            // One link per patient and portal account; the portal finds an account's links across clinics by user.
            e.HasIndex(x => new { x.TenantId, x.PatientId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Relationship).HasConversion<string>().HasMaxLength(10);
        });
        b.Entity<Saas.Domain.Care.PatientInvite>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasOne<Saas.Domain.Care.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(20);
        });
        b.Entity<ExchangeRate>(e =>
        {
            e.HasIndex(x => new { x.AsOf, x.Currency }).IsUnique();
            e.Property(x => x.PerUsd).HasPrecision(24, 10);
            e.Property(x => x.Currency).HasMaxLength(3);
        });

        b.Entity<RecurringSchedule>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.IsActive, x.NextRunDate });
            e.Property(x => x.Lines).HasColumnType("jsonb").HasConversion(JsonConverter<List<RecurringLine>>(), JsonComparer<List<RecurringLine>>());
        });
    }

    public async Task<long> NextDocumentNumberAsync(DocumentCounter counter, CancellationToken ct = default)
    {
        var tenantId = CurrentTenantId ?? throw new InvalidOperationException("No current tenant.");
        // Column names are fixed here, never taken from input.
        var sql = counter switch
        {
            DocumentCounter.Invoice => "UPDATE business_settings SET next_invoice_number = next_invoice_number + 1 WHERE tenant_id = {0} RETURNING next_invoice_number - 1 AS \"Value\"",
            DocumentCounter.Quote => "UPDATE business_settings SET next_quote_number = next_quote_number + 1 WHERE tenant_id = {0} RETURNING next_quote_number - 1 AS \"Value\"",
            DocumentCounter.Receipt => "UPDATE business_settings SET next_receipt_number = next_receipt_number + 1 WHERE tenant_id = {0} RETURNING next_receipt_number - 1 AS \"Value\"",
            DocumentCounter.SchoolInvoice => "UPDATE school_profiles SET next_invoice_number = next_invoice_number + 1 WHERE tenant_id = {0} RETURNING next_invoice_number - 1 AS \"Value\"",
            DocumentCounter.SchoolReceipt => "UPDATE school_profiles SET next_receipt_number = next_receipt_number + 1 WHERE tenant_id = {0} RETURNING next_receipt_number - 1 AS \"Value\"",
            DocumentCounter.CarePatient => "UPDATE clinic_profiles SET next_patient_number = next_patient_number + 1 WHERE tenant_id = {0} RETURNING next_patient_number - 1 AS \"Value\"",
            DocumentCounter.CareBill => "UPDATE clinic_profiles SET next_bill_number = next_bill_number + 1 WHERE tenant_id = {0} RETURNING next_bill_number - 1 AS \"Value\"",
            DocumentCounter.CareReceipt => "UPDATE clinic_profiles SET next_receipt_number = next_receipt_number + 1 WHERE tenant_id = {0} RETURNING next_receipt_number - 1 AS \"Value\"",
            _ => throw new ArgumentOutOfRangeException(nameof(counter)),
        };
        var numbers = await Database.SqlQueryRaw<long>(sql, tenantId).ToListAsync(ct);
        return numbers.Single();
    }

    private LambdaExpression TenantFilter(Type type)
    {
        // e => (Guid?)e.TenantId == this.CurrentTenantId
        var e = Expression.Parameter(type, "e");
        var body = Expression.Equal(
            Expression.Convert(Expression.Property(e, nameof(ITenantEntity.TenantId)), typeof(Guid?)),
            Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));
        return Expression.Lambda(body, e);
    }

    private static ValueConverter<T, string> JsonConverter<T>() where T : new() =>
        new(v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<T>(v, JsonSerializerOptions.Default) ?? new T());

    private static ValueComparer<T> JsonComparer<T>() where T : new() =>
        new((a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default) ?? new T());

    public Task AddListingStatAsync(Guid tenantId, Guid listingId, DateOnly day, string column, CancellationToken ct = default)
    {
        if (column is not ("views" or "whats_app_clicks" or "call_clicks" or "enquiries")) throw new ArgumentOutOfRangeException(nameof(column));
#pragma warning disable EF1002 // the column name comes from the fixed list above
        return Database.ExecuteSqlRawAsync($$"""
            INSERT INTO listing_stats (id, listing_id, day, views, whats_app_clicks, call_clicks, enquiries, created_at, tenant_id)
            VALUES ({0}, {1}, {2}, 0, 0, 0, 0, {3}, {4})
            ON CONFLICT (listing_id, day) DO NOTHING;
            UPDATE listing_stats SET {{column}} = {{column}} + 1, updated_at = {3} WHERE listing_id = {1} AND day = {2};
            """, [Guid.CreateVersion7(), listingId, day, DateTimeOffset.UtcNow, tenantId], ct);
#pragma warning restore EF1002
    }

    public async Task<long> NextOrderNumberAsync(Guid storeId, CancellationToken ct = default)
    {
        var numbers = await Database.SqlQuery<long>(
            $"UPDATE stores SET next_order_number = next_order_number + 1 WHERE id = {storeId} RETURNING next_order_number - 1 AS \"Value\"")
            .ToListAsync(ct);
        return numbers.Single();
    }

    public IQueryable<T> SqlQuery<T>(FormattableString sql) => Database.SqlQuery<T>(sql);

    public Task LockAsync(long key, CancellationToken ct) => Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", ct);

    public Task LockSharedAsync(long key, CancellationToken ct) => Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared({key})", ct);

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        ApplyRules();
        return base.SaveChangesAsync(ct);
    }

    public override int SaveChanges()
    {
        ApplyRules();
        return base.SaveChanges();
    }

    /// <summary>Timestamps, tenant stamping, the cross-tenant write guard, and audit rows.</summary>
    private void ApplyRules()
    {
        var now = DateTimeOffset.UtcNow;
        var audits = new List<AuditLog>();
        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            if (entry.Entity is Entity entity)
            {
                if (entry.State == EntityState.Added && entity.CreatedAt == default) entity.CreatedAt = now;
                if (entry.State == EntityState.Modified) entity.UpdatedAt = now;
            }

            if (entry.Entity is ITenantEntity owned)
            {
                if (entry.State == EntityState.Added && owned.TenantId == Guid.Empty)
                    owned.TenantId = CurrentTenantId ?? throw new InvalidOperationException(
                        $"Cannot add {entry.Metadata.ClrType.Name} without a current tenant.");
                if (CurrentTenantId is { } current && owned.TenantId != current)
                    throw new InvalidOperationException($"Blocked a cross-tenant write to {entry.Metadata.ClrType.Name}.");
            }

            if (entry.Metadata.ClrType.GetCustomAttribute<AuditedAttribute>() is not null && Audit(entry, now) is { } log)
                audits.Add(log);
        }
        AuditLogs.AddRange(audits);
    }

    /// <summary>Geometries don't serialize to JSON, so they're logged as text (e.g. "POINT (3.47 6.44)").</summary>
    private static object? Loggable(object? value) => value is NetTopologySuite.Geometries.Geometry g ? g.AsText() : value;

    private AuditLog? Audit(EntityEntry entry, DateTimeOffset now)
    {
        Dictionary<string, object?>? before = null, after = null;
        var props = entry.Properties.Where(p => !p.Metadata.IsPrimaryKey()
            && p.Metadata.PropertyInfo?.GetCustomAttribute<AuditIgnoreAttribute>() is null).ToList();
        switch (entry.State)
        {
            case EntityState.Added:
                after = props.ToDictionary(p => p.Metadata.Name, p => Loggable(p.CurrentValue));
                break;
            case EntityState.Deleted:
                before = props.ToDictionary(p => p.Metadata.Name, p => Loggable(p.OriginalValue));
                break;
            case EntityState.Modified:
                var changed = props.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)
                    && p.Metadata.Name is not nameof(Entity.UpdatedAt)).ToList();
                if (changed.Count == 0) return null;
                before = changed.ToDictionary(p => p.Metadata.Name, p => Loggable(p.OriginalValue));
                after = changed.ToDictionary(p => p.Metadata.Name, p => Loggable(p.CurrentValue));
                break;
        }

        return new AuditLog
        {
            TenantId = (entry.Entity as ITenantEntity)?.TenantId ?? CurrentTenantId,
            ActorId = currentUser.UserId,
            Action = entry.State.ToString().ToLowerInvariant(),
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = entry.Property("Id").CurrentValue?.ToString() ?? "",
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after),
            Ip = currentUser.IpAddress,
            CreatedAt = now,
        };
    }
}
