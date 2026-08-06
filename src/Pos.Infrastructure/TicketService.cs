using Microsoft.EntityFrameworkCore;
using Pos.Printing;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record TicketResult(byte[] Content, string FileName);

public sealed class TicketService(PosDbContext database)
{
    public async Task<TicketResult?> GenerateAsync(string token, Guid saleId, CancellationToken cancellationToken)
    {
        var user = await AuthorizedUserAsync(token, cancellationToken);
        if (user is null) return null;
        var sale = await database.Sales.AsNoTracking().SingleOrDefaultAsync(item => item.Id == saleId, cancellationToken);
        if (sale is null) throw new KeyNotFoundException("Venta no encontrada.");
        var lines = await (from line in database.SaleLines.AsNoTracking() join product in database.Products.AsNoTracking() on line.ProductId equals product.Id where line.SaleId == saleId select new TicketPdfLine(product.Description, line.Quantity, line.UnitPrice, line.LineTotal)).ToListAsync(cancellationToken);
        var payment = await database.Payments.AsNoTracking().SingleOrDefaultAsync(item => item.SaleId == saleId, cancellationToken);
        var store = await database.Stores.AsNoTracking().OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        var content = TicketPdfWriter.Create(new TicketPdfData(store.Name, sale.Id, sale.CreatedAtUtc, lines, sale.Total, payment?.Received ?? sale.Total, payment?.Change ?? 0m));
        var job = await database.PrintJobs.SingleOrDefaultAsync(item => item.SaleId == saleId && item.Status == "Pending", cancellationToken);
        if (job is not null) { job.Status = "Generated"; job.Attempts++; job.CompletedAtUtc = DateTimeOffset.UtcNow; await database.SaveChangesAsync(cancellationToken); }
        return new TicketResult(content, $"Ticket-{sale.Id:N}.pdf");
    }

    private async Task<UserRecord?> AuthorizedUserAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && (item.Code == "Sell" || item.Code == "ReprintTickets"), cancellationToken) ? user : null;
    }
}
