using Microsoft.EntityFrameworkCore;
using Pos.Printing;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record TicketResult(byte[] Content, string FileName);

public sealed class TicketService(PosDbContext database)
{
    public async Task<Guid?> LatestSaleIdAsync(string token, CancellationToken cancellationToken)
    {
        var viewer = await AuthorizedUserAsync(token, cancellationToken);
        if (viewer is null) return null;
        return await database.Sales.AsNoTracking()
            .Where(item => database.Shifts.Any(shift => shift.Id == item.ShiftId && shift.RegisterId == viewer.RegisterId))
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TicketResult?> GenerateAsync(string token, Guid saleId, CancellationToken cancellationToken)
    {
        var ticket = await GetDataAsync(token, saleId, cancellationToken);
        if (ticket is null) return null;
        var content = TicketPdfWriter.Create(ticket);
        var job = await database.PrintJobs.SingleOrDefaultAsync(item => item.SaleId == saleId && item.Status == "Pending", cancellationToken);
        if (job is not null) { job.Status = "Generated"; job.Attempts++; job.CompletedAtUtc = DateTimeOffset.UtcNow; await database.SaveChangesAsync(cancellationToken); }
        return new TicketResult(content, $"Ticket-{saleId:N}.pdf");
    }

    public async Task<TicketPdfData?> GetDataAsync(string token, Guid saleId, CancellationToken cancellationToken)
    {
        var viewer = await AuthorizedUserAsync(token, cancellationToken);
        if (viewer is null) return null;
        var sale = await database.Sales.AsNoTracking().SingleOrDefaultAsync(item => item.Id == saleId &&
            database.Shifts.Any(shift => shift.Id == item.ShiftId && shift.RegisterId == viewer.RegisterId), cancellationToken);
        if (sale is null) throw new KeyNotFoundException("Venta no encontrada.");
        var lines = await (from line in database.SaleLines.AsNoTracking()
                           join product in database.Products.AsNoTracking() on line.ProductId equals product.Id
                           where line.SaleId == saleId
                           select new TicketPdfLine(
                               product.Description,
                               line.Quantity,
                               line.OriginalUnitPrice > 0m ? line.OriginalUnitPrice : line.UnitPrice,
                               line.LineTotal,
                               line.DiscountTotal,
                               line.PromotionName)).ToListAsync(cancellationToken);
        var payments = await database.Payments.AsNoTracking().Where(item => item.SaleId == saleId).Select(item => new TicketPdfPayment(item.Method, item.Amount, item.Received, item.Change)).ToListAsync(cancellationToken);
        var shift = await database.Shifts.AsNoTracking().SingleAsync(item => item.Id == sale.ShiftId, cancellationToken);
        var register = await database.Registers.AsNoTracking().SingleAsync(item => item.Id == shift.RegisterId, cancellationToken);
        var cashier = await database.Users.AsNoTracking().SingleAsync(item => item.Id == shift.UserId, cancellationToken);
        var store = await database.Stores.AsNoTracking().SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        var shiftIds = await database.Shifts.AsNoTracking()
            .OrderBy(item => item.OpenedAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var shiftNumber = shiftIds.FindIndex(id => id == shift.Id) + 1L;
        var subtotal = decimal.Round(lines.Sum(line => line.Total), 2, MidpointRounding.AwayFromZero);
        return new TicketPdfData(
            store.Name,
            store.LegalName,
            store.TaxId,
            store.Address,
            store.Phone,
            store.TicketHeader,
            store.TicketFooter,
            80,
            sale.Id,
            shift.Id,
            register.Name,
            cashier.DisplayName,
            sale.CreatedAtUtc,
            lines,
            payments,
            sale.Total,
            store.CurrencySymbol,
            sale.Folio,
            shiftNumber,
            subtotal);
    }

    public async Task<bool?> MarkPrintedAsync(string token, Guid saleId, CancellationToken cancellationToken)
    {
        var viewer = await AuthorizedUserAsync(token, cancellationToken);
        if (viewer is null) return null;
        var belongsToRegister = await database.Sales.AsNoTracking().AnyAsync(item => item.Id == saleId &&
            database.Shifts.Any(shift => shift.Id == item.ShiftId && shift.RegisterId == viewer.RegisterId), cancellationToken);
        if (!belongsToRegister) return false;
        var job = await database.PrintJobs.SingleOrDefaultAsync(item => item.SaleId == saleId && item.Status != "Printed", cancellationToken);
        if (job is null) return false;
        job.Status = "Printed";
        job.Attempts++;
        job.CompletedAtUtc = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<TicketViewer?> AuthorizedUserAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session?.RegisterId is not Guid registerId) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && (item.Code == "Sell" || item.Code == "ReprintTickets"), cancellationToken)
            ? new TicketViewer(user.Id, registerId)
            : null;
    }

    private sealed record TicketViewer(Guid UserId, Guid RegisterId);
}
