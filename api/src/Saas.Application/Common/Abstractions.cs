using Saas.Domain.Property;
using Saas.Domain.Payouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Saas.Domain.Bookings;
using Saas.Domain.Core;
using Saas.Domain.Courses;
using Saas.Domain.Invoices;
using Saas.Domain.Orders;
using Saas.Domain.School;

namespace Saas.Application.Common;

/// <summary>
/// The tenant the current request acts for. Set from the JWT for signed-in users,
/// or from the store slug on public storefront requests.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    void SetTenant(Guid tenantId);
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    MemberRole? Role { get; }
    string? IpAddress { get; }
    /// <summary>True on support's read-only view-as tokens: hide lasting secrets such as calendar feed links.</summary>
    bool IsSupportView => false;
    /// <summary>When the caller's access token was issued (its <c>iat</c> claim, whole seconds); null outside a request.</summary>
    DateTimeOffset? TokenIssuedAt => null;
}

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<AppUser> Users { get; }
    DbSet<Membership> Memberships { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Plan> Plans { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<Payment> Payments { get; }
    DbSet<StoredFile> Files { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<UserNotification> UserNotifications { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<PlatformAdmin> PlatformAdmins { get; }
    DbSet<JobRun> JobRuns { get; }
    DbSet<AuthSession> AuthSessions { get; }

    DbSet<Store> Stores { get; }
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<DeliveryZone> DeliveryZones { get; }
    DbSet<DiscountCode> DiscountCodes { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderStatusHistory> OrderStatusHistory { get; }

    DbSet<BusinessSettings> BusinessSettings { get; }
    DbSet<Client> Clients { get; }
    DbSet<CatalogItem> CatalogItems { get; }
    DbSet<TaxRate> TaxRates { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceItem> InvoiceItems { get; }
    DbSet<InvoicePayment> InvoicePayments { get; }
    DbSet<InvoiceEvent> InvoiceEvents { get; }
    DbSet<Quote> Quotes { get; }
    DbSet<RecurringSchedule> RecurringSchedules { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<Invitation> Invitations { get; }
    DbSet<ExchangeRate> ExchangeRates { get; }

    DbSet<BookingBusiness> BookingBusinesses { get; }
    DbSet<BusinessHours> BusinessHours { get; }
    DbSet<BookableService> BookableServices { get; }
    DbSet<StaffMember> StaffMembers { get; }
    DbSet<TimeOff> TimeOffs { get; }
    DbSet<BookingCustomer> BookingCustomers { get; }
    DbSet<Booking> Bookings { get; }
    DbSet<BookingPayment> BookingPayments { get; }
    DbSet<BookingStatusHistory> BookingStatusHistory { get; }
    DbSet<BookingReminder> BookingReminders { get; }

    DbSet<Academy> Academies { get; }
    DbSet<CourseCategory> CourseCategories { get; }
    DbSet<Course> Courses { get; }
    DbSet<CourseSection> CourseSections { get; }
    DbSet<Lesson> Lessons { get; }
    DbSet<Video> Videos { get; }
    DbSet<LessonResource> LessonResources { get; }
    DbSet<QuizAttempt> QuizAttempts { get; }
    DbSet<Enrollment> Enrollments { get; }
    DbSet<LessonProgress> LessonProgress { get; }
    DbSet<LessonNote> LessonNotes { get; }
    DbSet<LessonQuestion> LessonQuestions { get; }
    DbSet<LessonAnswer> LessonAnswers { get; }
    DbSet<CourseReview> CourseReviews { get; }
    DbSet<CourseCoupon> CourseCoupons { get; }
    DbSet<CourseOrder> CourseOrders { get; }
    DbSet<Earning> Earnings { get; }
    DbSet<Payout> Payouts { get; }
    DbSet<Certificate> Certificates { get; }

    // SchoolLink
    DbSet<SchoolProfile> SchoolProfiles { get; }
    DbSet<StaffProfile> StaffProfiles { get; }
    DbSet<AcademicSession> AcademicSessions { get; }
    DbSet<Term> Terms { get; }
    DbSet<Level> Levels { get; }
    DbSet<SchoolClass> SchoolClasses { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<ClassSubject> ClassSubjects { get; }
    DbSet<AssessmentComponent> AssessmentComponents { get; }
    DbSet<GradingBand> GradingBands { get; }
    DbSet<Student> Students { get; }
    DbSet<Enrolment> Enrolments { get; }
    DbSet<Guardian> Guardians { get; }
    DbSet<StudentGuardian> StudentGuardians { get; }
    DbSet<GuardianInvite> GuardianInvites { get; }
    DbSet<ImportBatch> ImportBatches { get; }
    DbSet<AttendanceRegister> AttendanceRegisters { get; }
    DbSet<AttendanceMark> AttendanceMarks { get; }
    DbSet<AbsenceAlert> AbsenceAlerts { get; }
    DbSet<Score> Scores { get; }
    DbSet<Assignment> Assignments { get; }
    DbSet<AssignmentSubmission> AssignmentSubmissions { get; }
    DbSet<LessonPlan> LessonPlans { get; }
    DbSet<ExamPeriod> ExamPeriods { get; }
    DbSet<ExamSitting> ExamSittings { get; }
    DbSet<BankQuestion> BankQuestions { get; }
    DbSet<CbtPaper> CbtPapers { get; }
    DbSet<CbtPaperPick> CbtPaperPicks { get; }
    DbSet<CbtAttempt> CbtAttempts { get; }
    DbSet<CbtAttemptItem> CbtAttemptItems { get; }
    DbSet<WordList> WordLists { get; }
    DbSet<BrainTeaser> BrainTeasers { get; }
    DbSet<TermResultLock> TermResultLocks { get; }
    DbSet<SubjectResult> SubjectResults { get; }
    DbSet<CommentBankEntry> CommentBankEntries { get; }
    DbSet<TraitCriterion> TraitCriteria { get; }
    DbSet<TraitRating> TraitRatings { get; }
    DbSet<DevelopmentalArea> DevelopmentalAreas { get; }
    DbSet<DevelopmentalScale> DevelopmentalScales { get; }
    DbSet<DevelopmentalRating> DevelopmentalRatings { get; }
    DbSet<TermResult> TermResults { get; }
    DbSet<ResultsNotice> ResultsNotices { get; }
    DbSet<ReportCardBundle> ReportCardBundles { get; }
    DbSet<FeeItem> FeeItems { get; }
    DbSet<FeeStructureLine> FeeStructureLines { get; }
    DbSet<StudentInvoice> StudentInvoices { get; }
    DbSet<InvoiceLine> InvoiceLines { get; }
    DbSet<Instalment> Instalments { get; }
    DbSet<FeePayment> FeePayments { get; }
    DbSet<FeeReminder> FeeReminders { get; }
    DbSet<SchoolExpense> SchoolExpenses { get; }
    DbSet<Announcement> Announcements { get; }
    DbSet<AnnouncementDelivery> AnnouncementDeliveries { get; }
    DbSet<MessageThread> MessageThreads { get; }
    DbSet<Message> Messages { get; }
    DbSet<TimetableSlot> TimetableSlots { get; }

    // Property Listing Website
    DbSet<Location> Locations { get; }
    DbSet<PropertyType> PropertyTypes { get; }
    DbSet<Amenity> Amenities { get; }
    DbSet<SavedListing> SavedListings { get; }
    DbSet<SavedSearch> SavedSearches { get; }
    DbSet<AgencyProfile> AgencyProfiles { get; }
    DbSet<AgentProfile> AgentProfiles { get; }
    DbSet<Listing> Listings { get; }
    DbSet<ListingCost> ListingCosts { get; }
    DbSet<ListingAmenity> ListingAmenities { get; }
    DbSet<ListingMedia> ListingMedia { get; }
    DbSet<Enquiry> Enquiries { get; }
    DbSet<InspectionSlot> InspectionSlots { get; }
    DbSet<Inspection> Inspections { get; }
    DbSet<ListingStatDay> ListingStats { get; }
    DbSet<VerificationRequest> VerificationRequests { get; }
    DbSet<ListingReport> ListingReports { get; }
    DbSet<AgentReview> AgentReviews { get; }
    DbSet<Boost> Boosts { get; }
    DbSet<StayBlock> StayBlocks { get; }
    DbSet<Stay> Stays { get; }

    // CareLink
    DbSet<Saas.Domain.Care.ClinicProfile> ClinicProfiles { get; }
    DbSet<Saas.Domain.Care.CareStaffProfile> CareStaffProfiles { get; }
    DbSet<Saas.Domain.Care.PriceItem> PriceItems { get; }
    DbSet<Saas.Domain.Care.Patient> Patients { get; }
    DbSet<Saas.Domain.Care.PatientBill> PatientBills { get; }
    DbSet<Saas.Domain.Care.BillLine> BillLines { get; }
    DbSet<Saas.Domain.Care.CareAssignment> CareAssignments { get; }
    DbSet<Saas.Domain.Care.RecordAccess> RecordAccesses { get; }
    DbSet<Saas.Domain.Care.DoctorSchedule> DoctorSchedules { get; }
    DbSet<Saas.Domain.Care.Appointment> Appointments { get; }
    DbSet<Saas.Domain.Care.Visit> Visits { get; }
    DbSet<Saas.Domain.Care.VitalSigns> VitalSigns { get; }
    DbSet<Saas.Domain.Care.Consultation> Consultations { get; }
    DbSet<Saas.Domain.Care.Diagnosis> Diagnoses { get; }
    DbSet<Saas.Domain.Care.Addendum> Addenda { get; }
    DbSet<Saas.Domain.Care.Prescription> Prescriptions { get; }
    DbSet<Saas.Domain.Care.PrescriptionItem> PrescriptionItems { get; }
    DbSet<Saas.Domain.Care.TestRequest> TestRequests { get; }
    DbSet<Saas.Domain.Care.Ward> Wards { get; }
    DbSet<Saas.Domain.Care.Bed> Beds { get; }
    DbSet<Saas.Domain.Care.Admission> Admissions { get; }
    DbSet<Saas.Domain.Care.BedMove> BedMoves { get; }
    DbSet<Saas.Domain.Care.CarePayment> CarePayments { get; }
    DbSet<Saas.Domain.Care.BillAdjustment> BillAdjustments { get; }
    DbSet<Saas.Domain.Care.NursingNote> NursingNotes { get; }
    DbSet<Saas.Domain.Care.FluidEntry> FluidEntries { get; }
    DbSet<Saas.Domain.Care.WardRound> WardRounds { get; }
    DbSet<Saas.Domain.Care.MedicationDose> MedicationDoses { get; }
    DbSet<Saas.Domain.Care.DischargeSummary> DischargeSummaries { get; }
    DbSet<Saas.Domain.Care.PatientLink> PatientLinks { get; }
    DbSet<Saas.Domain.Care.PatientInvite> PatientInvites { get; }

    DatabaseFacade Database { get; }
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }
    /// <summary>The entity's change-tracking entry (for example to reload a tracked row that may be stale).</summary>
    Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>A composable query over raw SQL (cross-tenant; no query filters apply). Columns map to snake_case property names.</summary>
    IQueryable<T> SqlQuery<T>(FormattableString sql);

    /// <summary>Takes a Postgres transaction-scoped advisory lock; call inside a transaction. Released at commit or rollback.</summary>
    Task LockAsync(long key, CancellationToken ct);

    /// <summary>Takes a shared transaction-scoped advisory lock: many holders at once, but an exclusive <see cref="LockAsync"/> on the same key waits for them (and they wait for it).</summary>
    Task LockSharedAsync(long key, CancellationToken ct);

    /// <summary>Atomically adds one to a listing's daily counter: views, whats_app_clicks, call_clicks or enquiries.</summary>
    Task AddListingStatAsync(Guid tenantId, Guid listingId, DateOnly day, string column, CancellationToken ct = default);

    /// <summary>Atomically reserves the store's next order number.</summary>
    Task<long> NextOrderNumberAsync(Guid storeId, CancellationToken ct = default);

    /// <summary>Atomically reserves the current tenant's next invoice, quote or receipt number.</summary>
    Task<long> NextDocumentNumberAsync(DocumentCounter counter, CancellationToken ct = default);
}

public sealed record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public enum DocumentCounter { Invoice, Quote, Receipt, SchoolInvoice, SchoolReceipt, CarePatient, CareBill, CareReceipt }

/// <summary>A keyed (HMAC) hash with a server secret, for values like IP addresses that must not be reversible by guessing.</summary>
public interface IKeyedHash
{
    string Hash(string purpose, string value);
}

/// <summary>Recognises database errors the application handles, without depending on the database driver. Looks through every wrapping layer.</summary>
public interface IDbErrors
{
    /// <summary>True when an exclusion constraint rejected the change (e.g. two bookings overlapping for one staff member).</summary>
    bool IsExclusionViolation(Exception ex);
    /// <summary>True when a unique index rejected the change (e.g. two viewings booked for one agent at one time).</summary>
    bool IsUniqueViolation(Exception ex);
    /// <summary>True when the database broke a deadlock between simultaneous writes; one of them lost a race.</summary>
    bool IsConcurrencyConflict(Exception ex);
}

public interface IFileStorage
{
    /// <summary>Saves the stream and returns its public URL.</summary>
    Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    /// <summary>Opens a stored file for reading, or returns null when it is missing.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);
    /// <summary>A short-lived direct URL (cloud storage), or null when files must be streamed through the API.</summary>
    string? TemporaryUrl(string key, TimeSpan lifetime);
}

/// <summary>Public URLs. Each product has its own web app; <c>WebBaseUrl</c> is the WhatsApp Order System's.</summary>
public sealed record AppUrls(string WebBaseUrl, string ApiBaseUrl, IReadOnlyDictionary<string, string>? ProductWebUrls = null)
{
    public string Web(string product) =>
        (ProductWebUrls is not null && ProductWebUrls.TryGetValue(product, out var url) ? url : WebBaseUrl).TrimEnd('/');

    public string StoreUrl(string slug) => $"{WebBaseUrl.TrimEnd('/')}/{slug}";
    public string OrderUrl(string code) => $"{WebBaseUrl.TrimEnd('/')}/o/{code}";
}

/// <summary>Encrypts secrets stored in the database, such as a business's own payment key.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}
