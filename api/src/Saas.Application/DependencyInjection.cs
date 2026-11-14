using Saas.Application.Payouts;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Auth;
using Saas.Application.Billing;
using Saas.Application.Bookings;
using Saas.Application.Common;
using Saas.Application.Courses;
using Saas.Application.Invoices;
using Saas.Application.Orders;
using Saas.Application.Team;

namespace Saas.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<QuoteRequestValidator>(includeInternalTypes: true);
        services.AddSingleton(TimeProvider.System);

        // Shared core
        services.AddScoped<PlanService>();
        services.AddScoped<NotificationQueue>();
        services.AddScoped<InAppNotifications>();
        services.AddScoped<BillingService>();
        services.AddScoped<PlanPaymentJobs>();
        services.AddSingleton<PaymentSettingsCache>();
        services.AddScoped<PaymentSettings>();

        // WhatsApp Order System
        services.AddScoped<IProductProvisioner, OrdersProvisioner>();
        services.AddScoped<IUsageProvider, OrdersUsage>();
        services.AddScoped<StoreSettingsService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<CheckoutSettingsService>();
        services.AddScoped<OrderAdminService>();
        services.AddScoped<StorefrontService>();

        // Invoice Generator
        services.AddScoped<IProductProvisioner, InvoicesProvisioner>();
        services.AddScoped<IUsageProvider, InvoicesUsage>();
        services.AddScoped<InvoiceWorkspace>();
        services.AddScoped<InvoiceSetupService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<QuoteService>();
        services.AddScoped<RecurringService>();
        services.AddScoped<InvoiceJobs>();
        services.AddScoped<PublicDocumentsService>();
        services.AddScoped<ExchangeRateService>();
        services.AddScoped<ExpenseService>();
        services.AddScoped<ReportService>();

        // Teams (any product)
        services.AddScoped<TeamService>();
        services.AddScoped<TeamUsage>();
        services.AddScoped<IUsageProvider>(sp => new ProductTeamUsage(Products.Invoices, sp.GetRequiredService<TeamUsage>()));
        services.AddScoped<IUsageProvider>(sp => new ProductTeamUsage(Products.Bookings, sp.GetRequiredService<TeamUsage>()));

        // Booking System
        services.AddScoped<IProductProvisioner, BookingsProvisioner>();
        services.AddScoped<IUsageProvider, BookingsUsage>();
        services.AddScoped<BookingCore>();
        services.AddScoped<BookingSetupService>();
        services.AddScoped<BookingService>();
        services.AddScoped<PublicBookingService>();
        services.AddScoped<BookingJobs>();

        // Online Course Platform
        services.AddScoped<IProductProvisioner, CoursesProvisioner>();
        services.AddScoped<IUsageProvider, CoursesUsage>();
        services.AddScoped<IUsageProvider>(sp => new ProductTeamUsage(Products.Courses, sp.GetRequiredService<TeamUsage>()));

        // Property Listing Website
        services.AddScoped<Property.AgentSlugs>();
        services.AddScoped<Property.PropertyCore>();
        services.AddScoped<Property.ListingStudioService>();
        services.AddScoped<Property.ListingMediaService>();
        services.AddMemoryCache();
        services.AddScoped<Property.LocationTree>();
        services.AddScoped<Property.PropertySearchService>();
        services.AddScoped<Property.ListingEventsService>();
        services.AddScoped<Property.ReportService>();
        services.AddScoped<Property.VerificationService>();
        services.AddScoped<Property.PropertyAdminService>();
        services.AddScoped<Property.LeadService>();
        services.AddScoped<Property.InspectionService>();
        services.AddScoped<Property.SeekerService>();
        services.AddScoped<Property.SearchAlerts>();
        services.AddScoped<Property.AgentReviewService>();
        services.AddScoped<Property.PropertyJobs>();
        services.AddScoped<Property.ListingRenewService>();
        services.AddScoped<Property.StayService>();
        services.AddScoped<Property.HostStayService>();
        services.AddScoped<Property.BoostService>();
        services.AddScoped<IProductProvisioner, Property.PropertyProvisioner>();
        services.AddScoped<IUsageProvider, Property.PropertyUsage>();
        services.AddScoped<IUsageProvider>(sp => new ProductTeamUsage(Products.Property, sp.GetRequiredService<TeamUsage>()));

        // CareLink
        services.AddScoped<IProductProvisioner, Care.CareProvisioner>();
        services.AddScoped<IUsageProvider, Care.CareUsage>();
        services.AddScoped<IUsageProvider>(sp => new ProductTeamUsage(Products.Care, sp.GetRequiredService<TeamUsage>()));
        services.AddScoped<Care.CareAccess>();
        services.AddScoped<Care.CareSetupService>();
        services.AddScoped<Care.CareStaffService>();
        services.AddScoped<Care.PriceListService>();
        services.AddScoped<Care.PatientService>();
        services.AddScoped<Care.BillingService>();
        services.AddScoped<Care.AppointmentService>();
        services.AddSingleton<Care.ClinicalText>();
        services.AddScoped<Care.RecordGuard>();
        services.AddScoped<Care.VisitService>();
        services.AddScoped<Care.ConsultationService>();
        services.AddScoped<Care.RecordTimelineService>();
        services.AddScoped<Care.PrescriptionService>();
        services.AddScoped<Care.TestService>();
        services.AddScoped<Care.PaymentService>();
        services.AddScoped<Care.OnlinePaymentService>();
        services.AddScoped<Care.WardService>();
        services.AddScoped<Care.AdmissionService>();
        services.AddScoped<Care.WardCareService>();
        services.AddScoped<Care.DischargeService>();
        services.AddScoped<Care.CareReportService>();
        // CareLink imports run through the shared engine (kinds care-patients, care-price-list, care-staff under /api/care/imports).
        services.AddScoped<School.Imports.IImporter, Care.Imports.CarePatientImporter>();
        services.AddScoped<School.Imports.IImporter, Care.Imports.CarePriceListImporter>();
        services.AddScoped<School.Imports.IImporter, Care.Imports.CareStaffImporter>();
        services.AddSingleton<Care.CareFileSigner>();
        // Patient notices go to the portal accounts with an active link to the patient (Task 15).
        services.AddScoped<Care.ICarePortalRecipients, Care.PatientLinkRecipients>();
        services.AddScoped<Care.CareNotices>();
        services.AddScoped<Care.CarePortalGate>();
        // The patient portal (Task 15): the gate every portal route asks first, invites and links, and the portal's routes.
        services.AddScoped<Care.PatientPortalAccess>();
        services.AddScoped<Care.PatientInviteService>();
        services.AddScoped<Care.PortalService>();
        services.AddScoped<Care.CareJobSteps>();
        services.AddSingleton<Care.CareJobs>();

        // SchoolLink
        services.AddScoped<IProductProvisioner, School.SchoolProvisioner>();
        services.AddScoped<IUsageProvider, School.SchoolUsage>();
        services.AddScoped<IUsageProvider>(sp => new ProductTeamUsage(Products.School, sp.GetRequiredService<TeamUsage>()));
        services.AddScoped<School.SchoolSetupService>();
        services.AddScoped<School.AcademicService>();
        services.AddScoped<School.StaffService>();
        services.AddScoped<School.SchoolAccess>();
        services.AddScoped<School.SchoolFeed>();
        services.AddScoped<School.StudentService>();
        services.AddScoped<School.StudentImportService>();
        // Imports: one engine; each kind is an IImporter, resolved by its Kind from the route.
        services.AddScoped<School.Imports.ImportEngine>();
        services.AddScoped<School.Imports.Importers.StudentImporter>();
        services.AddScoped<School.Imports.IImporter>(sp => sp.GetRequiredService<School.Imports.Importers.StudentImporter>());
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.QuestionImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.ScoreImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.TraitImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.DevelopmentalImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.RemarkImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.WordListImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.TeaserImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.SubjectImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.ClassImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.ClassSubjectImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.StaffImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.FeeItemImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.FeeStructureImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.PaymentImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.ExpenseImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.AttendanceImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.AssignmentMarkImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.ExamSittingImporter>();
        services.AddScoped<School.Imports.IImporter, School.Imports.Importers.TimetableImporter>();
        services.AddScoped<School.PromotionService>();
        services.AddScoped<School.IdCardService>();
        services.AddScoped<School.ParentAccess>();
        services.AddScoped<School.GuardianInviteService>();
        services.AddScoped<School.StudentAccess>();
        services.AddScoped<School.SchoolPublicService>();
        services.AddScoped<School.AttendanceService>();
        services.AddScoped<School.ScoreService>();
        services.AddScoped<School.AssignmentService>();
        services.AddScoped<School.LessonPlanService>();
        services.AddScoped<School.ExamTimetableService>();
        services.AddScoped<School.CbtService>();
        services.AddScoped<School.PlayService>();
        services.AddScoped<School.ResultService>();
        services.AddScoped<School.CommentBankService>();
        services.AddScoped<School.TraitService>();
        services.AddScoped<School.DevelopmentalService>();
        services.AddScoped<School.ReportCardService>();
        services.AddScoped<School.BroadsheetService>();
        services.AddScoped<School.FeeService>();
        services.AddScoped<School.FeePaymentService>();
        services.AddScoped<School.FinanceService>();
        services.AddSingleton<School.SchoolJobs>();
        services.AddScoped<School.ExpenseService>();
        services.AddScoped<School.AnnouncementService>();
        services.AddScoped<School.MessageService>();
        services.AddScoped<School.TimetableService>();
        services.AddScoped<School.PortalService>();
        services.AddScoped<School.SchoolBrandingService>();
        services.AddScoped<School.SchoolDemoService>();
        services.AddSingleton<School.SchoolPhotoSigner>();

        services.AddScoped<CourseCore>();
        services.AddScoped<StudioService>();
        services.AddScoped<StudioPeopleService>();
        services.AddScoped<CourseCatalogService>();
        services.AddScoped<LearnService>();
        services.AddScoped<CourseAdminService>();
        services.AddScoped<PayoutService>();
        services.AddScoped<Platform.PlatformAccess>();
        services.AddScoped<Platform.PlatformViewer>();
        services.AddScoped<Platform.AuthSessions>();
        services.AddScoped<Platform.PlatformMeService>();
        services.AddScoped<Platform.HealthService>();
        services.AddSingleton<Platform.TenantStatusCache>();
        services.AddScoped<Platform.PlatformAudit>();
        services.AddScoped<Platform.PlatformBusinessService>();
        services.AddScoped<Platform.PlatformUserService>();
        services.AddScoped<Platform.PlatformPlansService>();
        services.AddScoped<Platform.PlatformMoneyService>();
        services.AddScoped<Platform.PlatformPaymentSettingsService>();
        services.AddScoped<Platform.PlatformTransfersService>();
        services.AddScoped<Platform.PlatformOverviewService>();
        services.AddScoped<Platform.PlatformAuditService>();
        services.AddScoped<Platform.PlatformAdminsService>();
        services.AddScoped<PayoutAdminService>();
        return services;
    }
}
