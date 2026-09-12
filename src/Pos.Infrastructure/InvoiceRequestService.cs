using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record InvoiceRequestSale(long Folio, Guid SaleId, decimal Total, DateTimeOffset CreatedAtUtc);
public sealed record CreateInvoiceRequestCommand(long SaleFolio, string ReceiverTaxId, string ReceiverName, string? ReceiverEmail, string? Notes);
public sealed record InvoiceRequestRow(Guid Id, long SaleFolio, decimal SaleTotal, DateTimeOffset SaleCreatedAtUtc, string ReceiverTaxId, string ReceiverName, string? ReceiverEmail, string Notes, string Status, DateTimeOffset RequestedAtUtc, string RequestedBy);

/// <summary>
/// Keeps a shared request history until a PAC is selected. It intentionally does
/// not create CFDI files, alter sales, or imply that a fiscal document exists.
/// </summary>
public sealed class InvoiceRequestService(PosDbContext database)
{
    public async Task<IReadOnlyList<InvoiceRequestRow>?> ListAsync(string token, string? query, CancellationToken cancellationToken)
    {
        if (await AuthorizedUserAsync(token, cancellationToken) is null) return null;
        var normalized = query?.Trim();
        var source = from request in database.InvoiceRequests.AsNoTracking()
                     join sale in database.Sales.AsNoTracking() on request.SaleId equals sale.Id
                     join user in database.Users.AsNoTracking() on request.RequestedByUserId equals user.Id
                     select new { Request = request, Sale = sale, User = user };
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var folio = long.TryParse(normalized, out var parsedFolio) ? parsedFolio : 0;
            source = source.Where(item => item.Sale.Folio == folio || item.Request.ReceiverTaxId.Contains(normalized) || item.Request.ReceiverName.Contains(normalized));
        }
        return await source
            .OrderByDescending(item => item.Request.RequestedAtUtc)
            .Select(item => new InvoiceRequestRow(item.Request.Id, item.Sale.Folio, item.Sale.Total, item.Sale.CreatedAtUtc, item.Request.ReceiverTaxId, item.Request.ReceiverName, item.Request.ReceiverEmail, item.Request.Notes, item.Request.Status, item.Request.RequestedAtUtc, item.User.DisplayName))
            .Take(300)
            .ToListAsync(cancellationToken);
    }

    public async Task<InvoiceRequestSale?> FindSaleAsync(string token, long folio, CancellationToken cancellationToken)
    {
        if (await AuthorizedUserAsync(token, cancellationToken) is null || folio <= 0) return null;
        return await database.Sales.AsNoTracking()
            .Where(item => item.Folio == folio && item.Status == "Completed")
            .Select(item => new InvoiceRequestSale(item.Folio, item.Id, item.Total, item.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<InvoiceRequestRow?> CreateAsync(string token, CreateInvoiceRequestCommand command, CancellationToken cancellationToken)
    {
        var user = await AuthorizedUserAsync(token, cancellationToken);
        if (user is null) return null;
        var taxId = command.ReceiverTaxId?.Trim().ToUpperInvariant() ?? string.Empty;
        var name = command.ReceiverName?.Trim() ?? string.Empty;
        var email = string.IsNullOrWhiteSpace(command.ReceiverEmail) ? null : command.ReceiverEmail.Trim();
        var notes = command.Notes?.Trim() ?? string.Empty;
        if (command.SaleFolio <= 0 || taxId.Length is < 12 or > 20 || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Selecciona una venta confirmada e indica RFC y nombre fiscal del receptor.");
        if (email?.Length > 200 || notes.Length > 500)
            throw new ArgumentException("Revisa la longitud del correo o de las notas.");

        var sale = await database.Sales.SingleOrDefaultAsync(item => item.Folio == command.SaleFolio && item.Status == "Completed", cancellationToken)
            ?? throw new KeyNotFoundException("No existe una venta confirmada con ese folio.");
        if (await database.InvoiceRequests.AnyAsync(item => item.SaleId == sale.Id, cancellationToken))
            throw new InvalidOperationException("Esta venta ya tiene una solicitud de factura registrada.");

        var now = DateTimeOffset.UtcNow;
        var request = new InvoiceRequestRecord
        {
            Id = Guid.NewGuid(),
            SaleId = sale.Id,
            RequestedByUserId = user.Id,
            ReceiverTaxId = taxId,
            ReceiverName = name,
            ReceiverEmail = email,
            Notes = notes,
            RequestedAtUtc = now,
            UpdatedAtUtc = now
        };
        database.InvoiceRequests.Add(request);
        await database.SaveChangesAsync(cancellationToken);
        return new(request.Id, sale.Folio, sale.Total, sale.CreatedAtUtc, request.ReceiverTaxId, request.ReceiverName, request.ReceiverEmail, request.Notes, request.Status, request.RequestedAtUtc, user.DisplayName);
    }

    private async Task<UserRecord?> AuthorizedUserAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ConfigureStore", cancellationToken) ? user : null;
    }
}
