using Saas.Domain.Common;

namespace Saas.Domain.Invoices;

/// <summary>The business profile printed on every invoice, plus numbering and reminder settings.</summary>
[Audited]
public class BusinessSettings : TenantEntity
{
    public required string LegalName { get; set; }
    public string? LogoUrl { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? TaxId { get; set; }
    /// <summary>Free text: bank, account name and number.</summary>
    public string? BankDetails { get; set; }
    public string Currency { get; set; } = "NGN";
    public int PaymentTermsDays { get; set; } = 14;
    public string InvoicePrefix { get; set; } = "INV-";
    public long NextInvoiceNumber { get; set; } = 1;
    public string QuotePrefix { get; set; } = "QUO-";
    public long NextQuoteNumber { get; set; } = 1;
    public long NextReceiptNumber { get; set; } = 1;
    public string DefaultTemplate { get; set; } = "classic";
    public string BrandColor { get; set; } = "#2563eb";
    public string? DefaultNotes { get; set; }
    public string? DefaultTerms { get; set; }
    public bool RemindersEnabled { get; set; }
    public int? ReminderDaysBefore { get; set; } = 3;
    public List<int> ReminderDaysAfter { get; set; } = [1, 7, 14];
    /// <summary>The business's own Paystack secret key, encrypted. Online payments go straight to them.</summary>
    public string? PaystackSecretKeyProtected { get; set; }

    // Letterhead: how documents look. Images (logo, signature, stamp) print only while the plan includes brand images.
    public string? Tagline { get; set; }
    public string? RegistrationNumber { get; set; }
    /// <summary>The line under the business name: tagline, address, registration or none.</summary>
    public string HeaderLine { get; set; } = "address";
    public string? SecondaryColor { get; set; }
    public string LogoSize { get; set; } = "medium";
    public string FontStyle { get; set; } = "modern";
    public string DocumentTitle { get; set; } = "Invoice";
    public bool ShowTaxId { get; set; } = true;
    public bool ShowRegistrationNumber { get; set; } = true;
    public bool ShowBankDetails { get; set; } = true;
    public bool ShowSignature { get; set; }
    public string? SignatureName { get; set; }
    public string? SignatureRole { get; set; }
    public string? SignatureImageUrl { get; set; }
    public string? StampImageUrl { get; set; }
    public string? FooterText { get; set; }
}

public class Client : TenantEntity
{
    public required string Name { get; set; }
    public string? Company { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? TaxId { get; set; }
    public string? Notes { get; set; }
}

/// <summary>A saved product or service, used to fill invoice lines quickly.</summary>
public class CatalogItem : TenantEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public long UnitPriceMinor { get; set; }
    public Guid? TaxRateId { get; set; }
    public string? Unit { get; set; }
}

public class TaxRate : TenantEntity
{
    public required string Name { get; set; }
    public decimal RatePercent { get; set; }
}

/// <summary>A copy of the client's details at the time of the document, so later edits don't change it.</summary>
public class ClientSnapshot
{
    public string Name { get; set; } = "";
    public string? Company { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? TaxId { get; set; }

    public static ClientSnapshot From(Client c) =>
        new() { Name = c.Name, Company = c.Company, Email = c.Email, Phone = c.Phone, Address = c.Address, TaxId = c.TaxId };
}

/// <summary>Shared by invoice and quote lines.</summary>
public abstract class DocumentLine : TenantEntity
{
    public Guid? ItemId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public long UnitPriceMinor { get; set; }
    public decimal DiscountPercent { get; set; }
    public Guid? TaxRateId { get; set; }
    public string? TaxName { get; set; }
    public decimal TaxRatePercent { get; set; }
    /// <summary>After discount, before tax.</summary>
    public long LineTotalMinor { get; set; }
    public long TaxMinor { get; set; }
    public int Position { get; set; }
}

/// <summary>Fields shared by invoices and quotes.</summary>
public abstract class Document : TenantEntity
{
    public Guid? ClientId { get; set; }
    public ClientSnapshot Client { get; set; } = new();
    public required string Number { get; set; }
    public DateOnly IssueDate { get; set; }
    public required string Currency { get; set; }
    /// <summary>Value of 1 unit of <see cref="Currency"/> in the business base currency, fixed when saved (1 if the same).</summary>
    public decimal ExchangeRate { get; set; } = 1;
    public string Template { get; set; } = "classic";
    public long SubtotalMinor { get; set; }
    public long DiscountTotalMinor { get; set; }
    public long TaxTotalMinor { get; set; }
    public long ShippingMinor { get; set; }
    public long TotalMinor { get; set; }
    public string? Notes { get; set; }
    public string? Terms { get; set; }
    public required string PublicToken { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ViewedAt { get; set; }
}

[Audited]
public class Invoice : Document
{
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateOnly DueDate { get; set; }
    public long AmountPaidMinor { get; set; }
    public long BalanceMinor { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public Guid? QuoteId { get; set; }
    public Guid? RecurringScheduleId { get; set; }
    public DateOnly? LastReminderOn { get; set; }
    public List<InvoiceItem> Items { get; set; } = [];
    public List<InvoicePayment> Payments { get; set; } = [];
    public List<InvoiceEvent> Events { get; set; } = [];

    /// <summary>Overdue is derived, never stored: open, not fully paid, and past its due date.</summary>
    public bool IsOverdue(DateOnly today) =>
        Status is InvoiceStatus.Sent or InvoiceStatus.Viewed or InvoiceStatus.PartiallyPaid && DueDate < today && BalanceMinor > 0;

    public InvoiceStatus DisplayStatus(DateOnly today) => IsOverdue(today) ? InvoiceStatus.Overdue : Status;
}

public class InvoiceItem : DocumentLine
{
    public Guid InvoiceId { get; set; }
}

public class InvoicePayment : TenantEntity
{
    public Guid InvoiceId { get; set; }
    public long AmountMinor { get; set; }
    /// <summary>Rate to the base currency on the payment date; income reports use <see cref="BaseAmountMinor"/>.</summary>
    public decimal ExchangeRate { get; set; } = 1;
    public long BaseAmountMinor { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    /// <summary>Set for online payments; unique so a charge is only recorded once.</summary>
    public string? ProviderRef { get; set; }
    public DateOnly PaidAt { get; set; }
    public required string ReceiptNumber { get; set; }
    public string? Note { get; set; }
}

public class InvoiceEvent : TenantEntity
{
    public Guid InvoiceId { get; set; }
    public InvoiceEventType Type { get; set; }
    public string? Note { get; set; }
}

[Audited]
public class Quote : Document
{
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public DateOnly ExpiryDate { get; set; }
    public Guid? ConvertedInvoiceId { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public List<QuoteItem> Items { get; set; } = [];

    public QuoteStatus DisplayStatus(DateOnly today) =>
        Status is QuoteStatus.Draft or QuoteStatus.Sent && ExpiryDate < today ? QuoteStatus.Expired : Status;
}

public class QuoteItem : DocumentLine
{
    public Guid QuoteId { get; set; }
}

[Audited]
public class RecurringSchedule : TenantEntity
{
    public Guid ClientId { get; set; }
    public RecurringFrequency Frequency { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly NextRunDate { get; set; }
    public DateOnly? LastRunDate { get; set; }
    public int DueInDays { get; set; } = 14;
    public bool AutoSend { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int InvoicesCreated { get; set; }
    public required string Currency { get; set; }
    public string Template { get; set; } = "classic";
    public long ShippingMinor { get; set; }
    public string? Notes { get; set; }
    public string? Terms { get; set; }
    /// <summary>The invoice lines to copy each time, stored as JSON.</summary>
    public List<RecurringLine> Lines { get; set; } = [];
}

public class RecurringLine
{
    public Guid? ItemId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public long UnitPriceMinor { get; set; }
    public decimal DiscountPercent { get; set; }
    public Guid? TaxRateId { get; set; }
}

/// <summary>Money the business spent. Amounts include tax; <see cref="TaxMinor"/> is the tax part.</summary>
[Audited]
public class Expense : TenantEntity
{
    public DateOnly Date { get; set; }
    public required string Category { get; set; }
    public string? Vendor { get; set; }
    public string? Description { get; set; }
    public long AmountMinor { get; set; }
    public required string Currency { get; set; }
    public long TaxMinor { get; set; }
    public decimal ExchangeRate { get; set; } = 1;
    public long BaseAmountMinor { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? Reference { get; set; }
    public Guid? ClientId { get; set; }
    public string? Notes { get; set; }
    public string? ReceiptUrl { get; set; }
    public string? ReceiptKey { get; set; }
}

public enum InvoiceStatus { Draft, Sent, Viewed, PartiallyPaid, Paid, Overdue, Void }
public enum QuoteStatus { Draft, Sent, Accepted, Declined, Converted, Expired }
public enum PaymentMethod { Cash, BankTransfer, Card, Online, Other }
public enum InvoiceEventType { Created, Sent, Viewed, Reminded, PaymentRecorded, PaymentRemoved, Paid, Voided, Overpaid }
public enum RecurringFrequency { Weekly, Monthly, Quarterly, Yearly }
