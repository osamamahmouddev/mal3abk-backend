using System.Globalization;
using System.Text;
using Malabak.Modules.Subscriptions.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Subscriptions.Infrastructure;

public sealed record InvoiceDto(
    Guid Id,
    string Number,
    Guid OwnerId,
    string PlanCode,
    string PlanName,
    string Cycle,
    decimal Amount,
    string Currency,
    DateTime IssuedAtUtc,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    string Status,
    string? VoidReason,
    string? VenueName = null,
    string? OwnerName = null,
    string? OwnerPhone = null);

public sealed record ReconciliationDto(DateOnly From, DateOnly To, string Currency, int Invoices, decimal Collected, decimal Voided, IReadOnlyList<MethodTotal> ByMethod);

public sealed record MethodTotal(string Method, int Count, decimal Amount);

/// <summary>Invoices (TASK-709): owners see and download their own; admins list, reconcile and void (never delete).</summary>
public sealed class InvoiceService
{
    private readonly SubscriptionsDbContext _dbContext;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;
    private readonly IOwnerUsageReader? _ownerUsage;
    private readonly IUserAccountsContract? _accounts;

    public InvoiceService(
        SubscriptionsDbContext dbContext,
        IAuditLog audit,
        IClock clock,
        IOwnerUsageReader? ownerUsage = null,
        IUserAccountsContract? accounts = null)
    {
        _dbContext = dbContext;
        _audit = audit;
        _clock = clock;
        _ownerUsage = ownerUsage;
        _accounts = accounts;
    }

    public async Task<IReadOnlyList<InvoiceDto>> ListAsync(Guid? ownerId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Invoices.AsNoTracking();
        if (ownerId is { } owner)
        {
            query = query.Where(i => i.OwnerId == owner);
        }

        if (from is { } f)
        {
            var start = f.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(i => i.IssuedAtUtc >= start);
        }

        if (to is { } t)
        {
            var end = t.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(i => i.IssuedAtUtc < end);
        }

        var invoices = await query.OrderByDescending(i => i.IssuedAtUtc).Take(500).ToListAsync(cancellationToken);
        if (invoices.Count == 0)
        {
            return [];
        }

        var ownerIds = invoices.Select(i => i.OwnerId).Distinct().ToList();
        IReadOnlyDictionary<Guid, IReadOnlyList<OwnerVenueInfo>> venueMap = new Dictionary<Guid, IReadOnlyList<OwnerVenueInfo>>();
        if (_ownerUsage != null && ownerIds.Count > 0)
        {
            venueMap = await _ownerUsage.GetOwnerVenuesBatchAsync(ownerIds, cancellationToken);
        }

        IReadOnlyDictionary<Guid, UserAccountSummary> accountMap = new Dictionary<Guid, UserAccountSummary>();
        if (_accounts != null && ownerIds.Count > 0)
        {
            accountMap = await _accounts.GetSummariesAsync(ownerIds, cancellationToken);
        }

        return invoices.Select(i =>
        {
            var venues = venueMap.GetValueOrDefault(i.OwnerId);
            var account = accountMap.GetValueOrDefault(i.OwnerId);
            var venueName = venues?.FirstOrDefault()?.Name;
            return ToDto(i, venueName, account?.FullName, account?.MaskedPhone);
        }).ToList();
    }

    /// <summary>One invoice. With <paramref name="ownerId"/> it must belong to that owner (owners cannot read others' invoices).</summary>
    public async Task<Result<InvoiceDto>> GetAsync(Guid invoiceId, Guid? ownerId, CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId && (ownerId == null || i.OwnerId == ownerId), cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<InvoiceDto>(SubscriptionErrors.InvoiceNotFound);
        }

        string? venueName = null;
        if (_ownerUsage != null)
        {
            var venues = await _ownerUsage.GetOwnerVenuesBatchAsync([invoice.OwnerId], cancellationToken);
            venueName = venues.GetValueOrDefault(invoice.OwnerId)?.FirstOrDefault()?.Name;
        }

        string? ownerName = null;
        string? ownerPhone = null;
        if (_accounts != null)
        {
            var accounts = await _accounts.GetSummariesAsync([invoice.OwnerId], cancellationToken);
            var account = accounts.GetValueOrDefault(invoice.OwnerId);
            ownerName = account?.FullName;
            ownerPhone = account?.MaskedPhone;
        }

        return Result.Success(ToDto(invoice, venueName, ownerName, ownerPhone));
    }

    public async Task<Result<byte[]>> PdfAsync(Guid invoiceId, Guid? ownerId, CancellationToken cancellationToken = default)
    {
        var invoice = await GetAsync(invoiceId, ownerId, cancellationToken);
        return invoice.IsSuccess ? Result.Success(InvoicePdf.Render(invoice.Value)) : Result.Failure<byte[]>(invoice.Error);
    }

    public async Task<Result<InvoiceDto>> VoidAsync(Guid invoiceId, string reason, Guid adminId, CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<InvoiceDto>(SubscriptionErrors.InvoiceNotFound);
        }

        var voided = invoice.Void(reason, adminId, _clock.UtcNow);
        if (voided.IsFailure)
        {
            return Result.Failure<InvoiceDto>(voided.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync(new AuditEntry("subscription.invoice_voided", "Invoice", invoice.Number, adminId, AuditActorType.Admin,
            new { Status = nameof(InvoiceStatus.Paid) }, new { Status = nameof(InvoiceStatus.Void) }, reason), cancellationToken);
        return await GetAsync(invoiceId, null, cancellationToken);
    }

    /// <summary>Totals for a period for matching against the bank/wallet statement: collected (paid invoices) and voided.</summary>
    public async Task<ReconciliationDto> ReconcileAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var invoices = await _dbContext.Invoices.AsNoTracking().Where(i => i.IssuedAtUtc >= start && i.IssuedAtUtc < end).ToListAsync(cancellationToken);
        var paymentIds = invoices.Where(i => i.Status == InvoiceStatus.Paid).Select(i => i.PaymentId).ToList();
        var methods = await _dbContext.Payments.AsNoTracking().Where(p => paymentIds.Contains(p.Id)).ToListAsync(cancellationToken);
        return new ReconciliationDto(
            from, to, invoices.FirstOrDefault()?.Currency ?? Money.DefaultCurrency, invoices.Count,
            invoices.Where(i => i.Status == InvoiceStatus.Paid).Sum(i => i.Amount),
            invoices.Where(i => i.Status == InvoiceStatus.Void).Sum(i => i.Amount),
            methods.GroupBy(p => p.Method).Select(g => new MethodTotal(g.Key.ToString(), g.Count(), g.Sum(p => p.Amount))).OrderByDescending(m => m.Amount).ToList());
    }

    public static InvoiceDto ToDto(Invoice i, string? venueName = null, string? ownerName = null, string? ownerPhone = null) => new(
        i.Id, i.Number, i.OwnerId, i.PlanCode, i.PlanName, i.Cycle.ToString(), i.Amount, i.Currency, i.IssuedAtUtc, i.PeriodStartUtc, i.PeriodEndUtc, i.Status.ToString(), i.VoidReason,
        venueName, ownerName, ownerPhone);
}

/// <summary>
/// Minimal one-page PDF (PDF 1.4, built-in Helvetica) so invoices download without a PDF library or embedded fonts. The
/// built-in fonts only cover Latin text, so the PDF uses the plan code and ASCII fields; the dashboard shows the Arabic names.
/// </summary>
public static class InvoicePdf
{
    public static byte[] Render(InvoiceDto invoice)
    {
        var c = CultureInfo.InvariantCulture;
        string venueLabel;
        if (!string.IsNullOrWhiteSpace(invoice.VenueName))
        {
            venueLabel = invoice.VenueName;
        }
        else if (!string.IsNullOrWhiteSpace(invoice.OwnerName))
        {
            venueLabel = invoice.OwnerName;
        }
        else
        {
            venueLabel = invoice.OwnerId.ToString("D");
        }

        var lines = new List<(int Size, string Text)>
        {
            (18, "MALABAK PLATFORM - SUBSCRIPTION INVOICE"),
            (12, $"Invoice Number: {invoice.Number}"),
            (12, $"Issued Date: {invoice.IssuedAtUtc.ToString("yyyy-MM-dd", c)} (UTC)"),
            (12, $"Status: {invoice.Status}{(invoice.VoidReason is null ? "" : " - " + invoice.VoidReason)}"),
            (12, $"Customer / Venue: {venueLabel}"),
            (12, string.IsNullOrWhiteSpace(invoice.OwnerName) ? "" : $"Owner Contact: {invoice.OwnerName}"),
            (12, ""),
            (12, $"Plan: {invoice.PlanName} [{invoice.PlanCode}] ({invoice.Cycle})"),
            (12, $"Billing Period: {invoice.PeriodStartUtc.ToString("yyyy-MM-dd", c)} to {invoice.PeriodEndUtc.ToString("yyyy-MM-dd", c)}"),
            (14, $"Amount Paid: {invoice.Amount.ToString("0.00", c)} {invoice.Currency}"),
            (12, ""),
            (10, "Official Subscription Invoice - Recorded by Malabak Administration."),
        };

        var content = new StringBuilder();
        // Top header banner (Navy)
        content.Append("0.03 0.09 0.20 rg\n");
        content.Append("40 730 515 65 re f\n");
        // Accent brand red bar
        content.Append("0.92 0.31 0.26 rg\n");
        content.Append("40 726 515 4 re f\n");
        content.Append("0 g\n");

        content.Append("BT\n");
        // Title in white over navy banner
        content.Append("/F1 18 Tf 1 1 1 rg 1 0 0 1 56 756 Tm (MALABAK PLATFORM - INVOICE) Tj\n");
        content.Append("0 g\n");

        var y = 690;
        foreach (var (size, text) in lines.Skip(1))
        {
            if (string.IsNullOrEmpty(text))
            {
                y -= 10;
                continue;
            }

            content.Append(c, $"/F1 {size} Tf 1 0 0 1 56 {y} Tm ({Escape(text)}) Tj\n");
            y -= size + 10;
        }

        content.Append("ET\n");
        // Divider line
        content.Append("0.85 0.88 0.92 RG 1 w 56 525 m 540 525 l S\n");

        var stream = Encoding.ASCII.GetBytes(content.ToString());

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };

        using var pdf = new MemoryStream();
        void Write(string s) => pdf.Write(Encoding.ASCII.GetBytes(s));
        var offsets = new List<long>();
        Write("%PDF-1.4\n");
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        offsets.Add(pdf.Position);
        Write(string.Create(c, $"5 0 obj\n<< /Length {stream.Length} >>\nstream\n"));
        pdf.Write(stream);
        Write("endstream\nendobj\n");

        var xref = pdf.Position;
        Write(string.Create(c, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets)
        {
            Write(string.Create(c, $"{offset:0000000000} 00000 n \n"));
        }

        Write(string.Create(c, $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return pdf.ToArray();
    }

    /// <summary>PDF string escaping; characters outside printable ASCII become '?' (built-in fonts cannot draw them).</summary>
    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '(' or ')' or '\\')
            {
                sb.Append('\\').Append(ch);
            }
            else
            {
                sb.Append(ch is >= ' ' and <= '~' ? ch : '?');
            }
        }

        return sb.ToString();
    }
}
