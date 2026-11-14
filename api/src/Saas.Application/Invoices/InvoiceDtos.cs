using FluentValidation;
using Saas.Application.Common;
using Saas.Domain.Invoices;

namespace Saas.Application.Invoices;

public sealed record BusinessDto(
    string LegalName, string? LogoUrl, string? Address, string? Email, string? Phone, string? TaxId, string? BankDetails,
    string Currency, string Country, int PaymentTermsDays, string InvoicePrefix, long NextInvoiceNumber, string QuotePrefix,
    long NextQuoteNumber, string DefaultTemplate, string BrandColor, string? DefaultNotes, string? DefaultTerms,
    bool RemindersEnabled, int? ReminderDaysBefore, IReadOnlyList<int> ReminderDaysAfter, bool PaystackConnected,
    string? Tagline, string? RegistrationNumber, string HeaderLine, string? SecondaryColor, string LogoSize, string FontStyle,
    string DocumentTitle, bool ShowTaxId, bool ShowRegistrationNumber, bool ShowBankDetails, bool ShowSignature, string? SignatureName,
    string? SignatureRole, string? SignatureImageUrl, string? StampImageUrl, string? FooterText, bool BrandImagesAvailable);

/// <summary>
/// The letterhead choices (<c>HeaderLine</c> to <c>ShowSignature</c>) keep their saved value when omitted; the letterhead text
/// fields are cleared when null or blank, like the other text fields.
/// </summary>
public sealed record UpdateBusinessRequest(
    string LegalName, string? Address, string? Email, string? Phone, string? TaxId, string? BankDetails, string Currency,
    int PaymentTermsDays, string InvoicePrefix, long NextInvoiceNumber, string QuotePrefix, long NextQuoteNumber,
    string DefaultTemplate, string BrandColor, string? DefaultNotes, string? DefaultTerms, bool RemindersEnabled,
    int? ReminderDaysBefore, List<int>? ReminderDaysAfter,
    string? Tagline = null, string? RegistrationNumber = null, string? HeaderLine = null, string? SecondaryColor = null,
    string? LogoSize = null, string? FontStyle = null, string? DocumentTitle = null, bool? ShowTaxId = null,
    bool? ShowRegistrationNumber = null, bool? ShowBankDetails = null, bool? ShowSignature = null, string? SignatureName = null,
    string? SignatureRole = null, string? FooterText = null);

public sealed record PaystackKeyRequest(string SecretKey);

/// <summary>The images a business can put on its documents (a Pro feature).</summary>
public enum BrandImage { Logo, Signature, Stamp }

public sealed record TaxRateDto(Guid Id, string Name, decimal RatePercent);
public sealed record SaveTaxRateRequest(string Name, decimal RatePercent);

public sealed record SaveClientRequest(string Name, string? Company, string? Email, string? Phone, string? Address, string? TaxId, string? Notes);
public sealed record ClientDto(
    Guid Id, string Name, string? Company, string? Email, string? Phone, string? Address, string? TaxId, string? Notes,
    int InvoiceCount, long OutstandingMinor);

public sealed record SaveItemRequest(string Name, string? Description, long UnitPriceMinor, Guid? TaxRateId, string? Unit);
public sealed record ItemDto(Guid Id, string Name, string? Description, long UnitPriceMinor, Guid? TaxRateId, string? Unit);

public sealed record LineRequest(Guid? ItemId, string Description, decimal Quantity, long UnitPriceMinor, decimal DiscountPercent, Guid? TaxRateId);

public record DocumentRequest : DocumentRequestBase
{
    public Guid ClientId { get; init; }
    public DateOnly IssueDate { get; init; }
}

public sealed record SaveInvoiceRequest : DocumentRequest
{
    public DateOnly DueDate { get; init; }
}

public sealed record SaveQuoteRequest : DocumentRequest
{
    public DateOnly ExpiryDate { get; init; }
}

public sealed record SendRequest(string? To, string? Message);

public sealed record RecordPaymentRequest(long AmountMinor, PaymentMethod Method, string? Reference, DateOnly PaidAt, string? Note, bool SendReceipt);

public sealed record ClientSnapshotDto(Guid? Id, string Name, string? Company, string? Email, string? Phone, string? Address, string? TaxId);
public sealed record LineDto(
    Guid Id, Guid? ItemId, string Description, decimal Quantity, long UnitPriceMinor, decimal DiscountPercent, Guid? TaxRateId,
    string? TaxName, decimal TaxRatePercent, long LineTotalMinor);
public sealed record PaymentDto(Guid Id, long AmountMinor, PaymentMethod Method, string? Reference, DateOnly PaidAt, string ReceiptNumber, string? Note, bool Online,
    decimal ExchangeRate, long BaseAmountMinor);
public sealed record EventDto(InvoiceEventType Type, string? Note, DateTimeOffset CreatedAt);

public sealed record InvoiceSummaryDto(
    Guid Id, string Number, string ClientName, DateOnly IssueDate, DateOnly DueDate, string Currency, long TotalMinor,
    long BalanceMinor, InvoiceStatus Status, DateTimeOffset CreatedAt, long TotalBaseMinor, long BalanceBaseMinor);

public sealed record InvoiceDto(
    Guid Id, string Number, InvoiceStatus Status, ClientSnapshotDto Client, DateOnly IssueDate, DateOnly DueDate, string Currency,
    string Template, IReadOnlyList<LineDto> Items, long SubtotalMinor, long DiscountTotalMinor, long TaxTotalMinor,
    IReadOnlyList<TaxBreakdown> Taxes, long ShippingMinor, long TotalMinor, long AmountPaidMinor, long BalanceMinor,
    string? Notes, string? Terms, string PublicUrl, string WhatsappShareUrl, DateTimeOffset? SentAt, DateTimeOffset? ViewedAt,
    DateTimeOffset? PaidAt, Guid? QuoteId, Guid? RecurringScheduleId, IReadOnlyList<PaymentDto> Payments,
    IReadOnlyList<EventDto> Events, DateTimeOffset CreatedAt, string BaseCurrency, decimal ExchangeRate, long TotalBaseMinor,
    long BalanceBaseMinor);

public sealed record QuoteSummaryDto(
    Guid Id, string Number, string ClientName, DateOnly IssueDate, DateOnly ExpiryDate, string Currency, long TotalMinor,
    QuoteStatus Status, DateTimeOffset CreatedAt);

public sealed record QuoteDto(
    Guid Id, string Number, QuoteStatus Status, ClientSnapshotDto Client, DateOnly IssueDate, DateOnly ExpiryDate, string Currency,
    string Template, IReadOnlyList<LineDto> Items, long SubtotalMinor, long DiscountTotalMinor, long TaxTotalMinor,
    IReadOnlyList<TaxBreakdown> Taxes, long ShippingMinor, long TotalMinor, string? Notes, string? Terms, string PublicUrl,
    string WhatsappShareUrl, DateTimeOffset? SentAt, DateTimeOffset? ViewedAt, Guid? ConvertedInvoiceId, DateTimeOffset CreatedAt,
    string BaseCurrency, decimal ExchangeRate, long TotalBaseMinor);

public sealed record InvoiceDashboardDto(
    string Currency, long OutstandingMinor, long OverdueMinor, int OverdueCount, long PaidThisMonthMinor, int DraftCount,
    int InvoicesThisMonth, long InvoiceLimit, IReadOnlyList<InvoiceSummaryDto> RecentInvoices);

public sealed record TemplateDto(string Key, string Name, bool IsPremium, bool Available);

public sealed record SaveRecurringRequest : DocumentRequestBase
{
    public Guid ClientId { get; init; }
    public RecurringFrequency Frequency { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public int DueInDays { get; init; } = 14;
    public bool AutoSend { get; init; } = true;
    public bool IsActive { get; init; } = true;
}

/// <summary>The money parts shared by invoices, quotes and recurring schedules.</summary>
public abstract record DocumentRequestBase
{
    public string Currency { get; init; } = "";
    public string? Template { get; init; }
    public List<LineRequest> Items { get; init; } = [];
    public long ShippingMinor { get; init; }
    public string? Notes { get; init; }
    public string? Terms { get; init; }
    /// <summary>Optional manual rate to the base currency; today's rate is used when omitted.</summary>
    public decimal? ExchangeRate { get; init; }
}

public sealed record RecurringDto(
    Guid Id, Guid ClientId, string ClientName, RecurringFrequency Frequency, DateOnly StartDate, DateOnly? EndDate, int DueInDays,
    bool AutoSend, bool IsActive, string Currency, string Template, IReadOnlyList<LineRequest> Items, long ShippingMinor,
    string? Notes, string? Terms, DateOnly NextRunDate, DateOnly? LastRunDate, int InvoicesCreated, long TotalMinor);

// Public pages

/// <summary>Image URLs (logo, signature, stamp) are null unless the business's plan includes brand images.</summary>
public sealed record PublicBusinessDto(
    string Name, string? LogoUrl, string? Address, string? Email, string? Phone, string? TaxId, string? BankDetails, string BrandColor,
    string? Tagline, string? RegistrationNumber, string HeaderLine, string? SecondaryColor, string LogoSize, string FontStyle,
    string DocumentTitle, bool ShowTaxId, bool ShowRegistrationNumber, bool ShowBankDetails, bool ShowSignature, string? SignatureName,
    string? SignatureRole, string? SignatureImageUrl, string? StampImageUrl, string? FooterText);
public sealed record PublicPaymentDto(Guid Id, long AmountMinor, PaymentMethod Method, DateOnly PaidAt, string ReceiptNumber);

public sealed record PublicInvoiceDto(
    string Number, InvoiceStatus Status, PublicBusinessDto Business, ClientSnapshotDto Client, DateOnly IssueDate, DateOnly DueDate,
    string Currency, string Template, IReadOnlyList<LineDto> Items, long SubtotalMinor, long DiscountTotalMinor, long TaxTotalMinor,
    IReadOnlyList<TaxBreakdown> Taxes, long ShippingMinor, long TotalMinor, long AmountPaidMinor, long BalanceMinor,
    string? Notes, string? Terms, bool CanPayOnline, IReadOnlyList<PublicPaymentDto> Payments, bool ShowBranding,
    bool? PaymentConfirmed = null); // set by verify only: see PublicDocumentsService.VerifyAsync

public sealed record PublicQuoteDto(
    string Number, QuoteStatus Status, PublicBusinessDto Business, ClientSnapshotDto Client, DateOnly IssueDate, DateOnly ExpiryDate,
    string Currency, string Template, IReadOnlyList<LineDto> Items, long SubtotalMinor, long DiscountTotalMinor, long TaxTotalMinor,
    IReadOnlyList<TaxBreakdown> Taxes, long ShippingMinor, long TotalMinor, string? Notes, string? Terms, bool CanRespond, bool ShowBranding);

public sealed record PayRequest(string? Email);
public sealed record VerifyRequest(string Reference);
public sealed record PayResult(string AuthorizationUrl);

/// <summary>The generator's business: the free letterhead fields only, never images (those are paid).</summary>
public sealed record PreviewBusiness(
    string? Name, string? Address, string? Email, string? Phone, string? TaxId, string? BankDetails, string? BrandColor,
    string? Tagline = null, string? RegistrationNumber = null, string? HeaderLine = null, string? SecondaryColor = null,
    string? FontStyle = null, string? DocumentTitle = null, bool? ShowSignature = null, string? SignatureName = null,
    string? SignatureRole = null, string? FooterText = null);
public sealed record PreviewClient(string? Name, string? Company, string? Email, string? Phone, string? Address);
public sealed record PreviewLine(string? Description, decimal Quantity, long UnitPriceMinor, decimal DiscountPercent, decimal TaxRatePercent);
public sealed record PreviewRequest(
    PreviewBusiness? Business, PreviewClient? Client, string? Number, DateOnly? IssueDate, DateOnly? DueDate, string? Currency,
    string? Template, List<PreviewLine>? Items, long ShippingMinor, string? Notes, string? Terms);

// Validators

public sealed class UpdateBusinessRequestValidator : AbstractValidator<UpdateBusinessRequest>
{
    public UpdateBusinessRequestValidator()
    {
        RuleFor(x => x.LegalName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.TaxId).MaximumLength(50);
        RuleFor(x => x.BankDetails).MaximumLength(300);
        RuleFor(x => x.Currency).Must(c => Money.SupportedCurrencies.Contains(c)).WithMessage("Unsupported currency.");
        RuleFor(x => x.PaymentTermsDays).InclusiveBetween(0, 365);
        RuleFor(x => x.InvoicePrefix).MaximumLength(12).Matches("^[A-Za-z0-9/_-]*$").WithMessage("Use letters, numbers, - / or _.");
        RuleFor(x => x.QuotePrefix).MaximumLength(12).Matches("^[A-Za-z0-9/_-]*$").WithMessage("Use letters, numbers, - / or _.");
        RuleFor(x => x.NextInvoiceNumber).InclusiveBetween(1, 99_999_999);
        RuleFor(x => x.NextQuoteNumber).InclusiveBetween(1, 99_999_999);
        RuleFor(x => x.DefaultTemplate).Must(InvoiceTemplates.Exists).WithMessage("Unknown template.");
        RuleFor(x => x.BrandColor).Matches("^#[0-9a-fA-F]{6}$").WithMessage("Use a hex colour such as #2563eb.");
        RuleFor(x => x.DefaultNotes).MaximumLength(1000);
        RuleFor(x => x.DefaultTerms).MaximumLength(2000);
        RuleFor(x => x.ReminderDaysBefore).InclusiveBetween(1, 60);
        RuleFor(x => x.ReminderDaysAfter).Must(d => d is null || (d.Count <= 6 && d.All(n => n is >= 1 and <= 120)))
            .WithMessage("Use up to 6 reminders, each 1-120 days after the due date.");
        RuleFor(x => x.Tagline).MaximumLength(Letterhead.TaglineMax);
        RuleFor(x => x.RegistrationNumber).MaximumLength(Letterhead.RegistrationNumberMax);
        RuleFor(x => x.HeaderLine).Must(v => v is null || Letterhead.HeaderLines.Contains(v)).WithMessage(Letterhead.HeaderLineMessage);
        RuleFor(x => x.SecondaryColor).Must(Letterhead.IsColorOrBlank).WithMessage("Use a hex colour such as #0f766e.");
        RuleFor(x => x.LogoSize).Must(v => v is null || Letterhead.LogoSizes.Contains(v)).WithMessage("Choose small, medium or large.");
        RuleFor(x => x.FontStyle).Must(v => v is null || Letterhead.FontStyles.Contains(v)).WithMessage("Choose modern, serif or sans.");
        RuleFor(x => x.DocumentTitle).Must(v => v is null || Letterhead.DocumentTitles.Contains(v)).WithMessage(Letterhead.DocumentTitleMessage);
        RuleFor(x => x.SignatureName).MaximumLength(Letterhead.SignatureMax);
        RuleFor(x => x.SignatureRole).MaximumLength(Letterhead.SignatureMax);
        RuleFor(x => x.FooterText).MaximumLength(Letterhead.FooterMax);
    }
}

/// <summary>The letterhead choices a business (or the free generator) can make, with their limits.</summary>
public static class Letterhead
{
    public const int TaglineMax = 100;
    public const int RegistrationNumberMax = 40;
    public const int SignatureMax = 80;
    public const int FooterMax = 160;

    public static readonly IReadOnlyList<string> HeaderLines = ["tagline", "address", "registration", "none"];
    public static readonly IReadOnlyList<string> LogoSizes = ["small", "medium", "large"];
    public static readonly IReadOnlyList<string> FontStyles = ["modern", "serif", "sans"];
    public static readonly IReadOnlyList<string> DocumentTitles = ["Invoice", "Tax invoice", "Bill", "Pro forma invoice", "Statement of account"];

    public const string HeaderLineMessage = "Choose tagline, address, registration or none.";
    public const string DocumentTitleMessage = "Choose Invoice, Tax invoice, Bill, Pro forma invoice or Statement of account.";

    public static bool IsColorOrBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) || System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), "^#[0-9a-fA-F]{6}$");
}

public sealed class SaveTaxRateRequestValidator : AbstractValidator<SaveTaxRateRequest>
{
    public SaveTaxRateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(40);
        RuleFor(x => x.RatePercent).InclusiveBetween(0, 100);
    }
}

public sealed class SaveClientRequestValidator : AbstractValidator<SaveClientRequest>
{
    public SaveClientRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Company).MaximumLength(120);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.TaxId).MaximumLength(50);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class SaveItemRequestValidator : AbstractValidator<SaveItemRequest>
{
    public SaveItemRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.UnitPriceMinor).InclusiveBetween(0, 100_000_000_000);
        RuleFor(x => x.Unit).MaximumLength(20);
    }
}

public sealed class LineRequestValidator : AbstractValidator<LineRequest>
{
    public LineRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000);
        RuleFor(x => x.UnitPriceMinor).InclusiveBetween(0, 100_000_000_000);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100);
    }
}

/// <summary>Rules for the money parts shared by invoices, quotes and recurring schedules.</summary>
public sealed class DocumentRequestBaseValidator : AbstractValidator<DocumentRequestBase>
{
    public DocumentRequestBaseValidator()
    {
        RuleFor(x => x.Currency).Must(c => Money.SupportedCurrencies.Contains(c)).WithMessage("Unsupported currency.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Add at least one line.")
            .Must(i => i.Count <= 100).WithMessage("Use at most 100 lines.");
        RuleForEach(x => x.Items).SetValidator(new LineRequestValidator());
        RuleFor(x => x.ShippingMinor).InclusiveBetween(0, 100_000_000_000);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Terms).MaximumLength(2000);
        RuleFor(x => x.Template).Must(t => t is null || InvoiceTemplates.Exists(t)).WithMessage("Unknown template.");
        RuleFor(x => x.ExchangeRate).GreaterThan(0).LessThan(10_000_000).When(x => x.ExchangeRate is not null);
    }
}

public sealed class SaveInvoiceRequestValidator : AbstractValidator<SaveInvoiceRequest>
{
    public SaveInvoiceRequestValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty().WithMessage("Choose a client.");
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.IssueDate).WithMessage("The due date can't be before the issue date.");
        Include(new DocumentRequestBaseValidator());
    }
}

public sealed class SaveQuoteRequestValidator : AbstractValidator<SaveQuoteRequest>
{
    public SaveQuoteRequestValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty().WithMessage("Choose a client.");
        RuleFor(x => x.ExpiryDate).GreaterThanOrEqualTo(x => x.IssueDate).WithMessage("The expiry date can't be before the issue date.");
        Include(new DocumentRequestBaseValidator());
    }
}

public sealed class SaveRecurringRequestValidator : AbstractValidator<SaveRecurringRequest>
{
    public SaveRecurringRequestValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty().WithMessage("Choose a client.");
        RuleFor(x => x.Frequency).IsInEnum();
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate).When(x => x.EndDate is not null);
        RuleFor(x => x.DueInDays).InclusiveBetween(0, 365);
        Include(new DocumentRequestBaseValidator());
    }
}

public sealed class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(x => x.AmountMinor).GreaterThan(0);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class SendRequestValidator : AbstractValidator<SendRequest>
{
    public SendRequestValidator()
    {
        RuleFor(x => x.To).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.To));
        RuleFor(x => x.Message).MaximumLength(1000);
    }
}

public sealed class PreviewRequestValidator : AbstractValidator<PreviewRequest>
{
    public PreviewRequestValidator()
    {
        RuleFor(x => x.Items).Must(i => i is null || i.Count <= 100).WithMessage("Use at most 100 lines.");
        RuleForEach(x => x.Items).ChildRules(l =>
        {
            l.RuleFor(x => x.Description).MaximumLength(500);
            l.RuleFor(x => x.Quantity).InclusiveBetween(0, 1_000_000);
            l.RuleFor(x => x.UnitPriceMinor).InclusiveBetween(0, 100_000_000_000);
            l.RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100);
            l.RuleFor(x => x.TaxRatePercent).InclusiveBetween(0, 100);
        });
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Terms).MaximumLength(2000);
        RuleFor(x => x.Business!.Tagline).MaximumLength(Letterhead.TaglineMax).When(x => x.Business is not null);
        RuleFor(x => x.Business!.RegistrationNumber).MaximumLength(Letterhead.RegistrationNumberMax).When(x => x.Business is not null);
        RuleFor(x => x.Business!.HeaderLine).Must(v => v is null || Letterhead.HeaderLines.Contains(v))
            .WithMessage(Letterhead.HeaderLineMessage).When(x => x.Business is not null);
        RuleFor(x => x.Business!.SecondaryColor).Must(Letterhead.IsColorOrBlank).WithMessage("Use a hex colour such as #0f766e.")
            .When(x => x.Business is not null);
        RuleFor(x => x.Business!.FontStyle).Must(v => v is null || Letterhead.FontStyles.Contains(v))
            .WithMessage("Choose modern, serif or sans.").When(x => x.Business is not null);
        RuleFor(x => x.Business!.DocumentTitle).Must(v => v is null || Letterhead.DocumentTitles.Contains(v))
            .WithMessage(Letterhead.DocumentTitleMessage).When(x => x.Business is not null);
        RuleFor(x => x.Business!.SignatureName).MaximumLength(Letterhead.SignatureMax).When(x => x.Business is not null);
        RuleFor(x => x.Business!.SignatureRole).MaximumLength(Letterhead.SignatureMax).When(x => x.Business is not null);
        RuleFor(x => x.Business!.FooterText).MaximumLength(Letterhead.FooterMax).When(x => x.Business is not null);
    }
}

public static class InvoiceTemplates
{
    public static readonly IReadOnlyList<(string Key, string Name, bool Premium)> All =
    [
        ("classic", "Classic", false),
        ("modern", "Modern", false),
        ("minimal", "Minimal", false),
        ("bold", "Bold", false),
        ("elegant", "Elegant", false),
        ("executive", "Executive", true),
        ("corporate", "Corporate", true),
        ("ledger", "Ledger", true),
        ("studio", "Studio", true),
        ("prestige", "Prestige", true),
    ];

    public static bool Exists(string key) => All.Any(t => t.Key == key);
    public static bool IsPremium(string key) => All.Any(t => t.Key == key && t.Premium);
}
