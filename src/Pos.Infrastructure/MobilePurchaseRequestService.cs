using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record MobilePurchaseSupplier(Guid? Id, string Name);
public sealed record MobilePurchaseProduct(Guid Id, string Code, string Description, string UnitOfMeasure);
public sealed record CreateMobilePurchaseRequestCommand(Guid OperationId, Guid? SupplierId, bool PayFromCashRegister, decimal? CashWithdrawn, IReadOnlyList<MobilePurchaseRequestLineCommand> Lines);
public sealed record MobilePurchaseRequestLineCommand(Guid ProductId, decimal Quantity, decimal UnitCost);
public sealed record MobilePurchaseRequestLineResult(Guid ProductId, string Code, string Description, string UnitOfMeasure, decimal Quantity, decimal UnitCost, decimal LineTotal);
public sealed record MobilePurchaseRequestResult(Guid Id, Guid OperationId, Guid? SupplierId, string Supplier, string CreatedBy, string DeviceName, string Status, decimal Total, bool ProposedPayFromCashRegister, decimal? ProposedCashWithdrawn, DateTimeOffset CreatedAtUtc, DateTimeOffset? ReviewedAtUtc, string? RejectionReason, IReadOnlyList<MobilePurchaseRequestLineResult> Lines);

public sealed class MobilePurchaseRequestService(PosDbContext database)
{
    public async Task<IReadOnlyList<MobilePurchaseSupplier>?> SuppliersAsync(string accessToken, string deviceToken, CancellationToken cancellationToken)
    {
        var actor = await AuthorizedActorAsync(accessToken, deviceToken, cancellationToken);
        if (actor is null) return null;
        return await database.Suppliers.AsNoTracking().OrderBy(item => item.Name)
            .Select(item => new MobilePurchaseSupplier(item.Id, item.Name)).ToListAsync(cancellationToken);
    }

    public async Task<MobilePurchaseProduct?> FindProductAsync(string accessToken, string deviceToken, string code, CancellationToken cancellationToken)
    {
        var actor = await AuthorizedActorAsync(accessToken, deviceToken, cancellationToken);
        if (actor is null) return null;
        var normalized = ProductCatalogService.NormalizeCode(code ?? string.Empty);
        if (normalized.Length == 0) return null;
        return await database.Products.AsNoTracking()
            .Where(item => item.IsActive && !item.IsTemporary && item.NormalizedCode == normalized)
            .Select(item => new MobilePurchaseProduct(item.Id, item.Code, item.Description, item.UnitOfMeasure))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<MobilePurchaseRequestResult?> CreateAsync(string accessToken, string deviceToken, CreateMobilePurchaseRequestCommand command, CancellationToken cancellationToken)
    {
        var actor = await AuthorizedActorAsync(accessToken, deviceToken, cancellationToken);
        if (actor is null) return null;
        if (command.OperationId == Guid.Empty || command.Lines is null || command.Lines.Count is < 1 or > 150)
            throw new ArgumentException("Agrega entre 1 y 150 productos antes de enviar la solicitud.");
        if (command.Lines.Select(line => line.ProductId).Distinct().Count() != command.Lines.Count)
            throw new ArgumentException("Un producto aparece más de una vez. Suma sus piezas en una sola partida.");
        if (command.Lines.Any(line => line.ProductId == Guid.Empty || line.Quantity <= 0m || line.Quantity > 1_000_000m || line.UnitCost < 0m || line.UnitCost > 1_000_000_000m))
            throw new ArgumentException("Revisa las cantidades y los costos capturados.");
        if (command.CashWithdrawn is < 0m) throw new ArgumentException("El efectivo por retirar no puede ser negativo.");
        if (!command.PayFromCashRegister && command.CashWithdrawn is not null)
            throw new ArgumentException("No indiques un retiro si el pago no saldrá de caja.");

        var existing = await database.MobilePurchaseRequests.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (existing is not null)
        {
            if (existing.StoreId != actor.Device.StoreId || existing.UserId != actor.User.Id || existing.MobileDeviceId != actor.Device.Id)
                throw new InvalidOperationException("La operación ya existe y no pertenece a este teléfono.");
            return await RequestResultAsync(existing.Id, cancellationToken);
        }

        if (command.SupplierId is Guid supplierId && supplierId != Guid.Empty
            && !await database.Suppliers.AnyAsync(item => item.Id == supplierId, cancellationToken))
            throw new KeyNotFoundException("El proveedor seleccionado ya no existe.");

        var productIds = command.Lines.Select(line => line.ProductId).ToArray();
        var products = await database.Products.AsNoTracking()
            .Where(item => productIds.Contains(item.Id) && item.IsActive && !item.IsTemporary)
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (products.Count != productIds.Length) throw new KeyNotFoundException("Uno o más productos ya no existen o están inactivos.");

        var now = DateTimeOffset.UtcNow;
        var request = new MobilePurchaseRequestRecord
        {
            Id = Guid.NewGuid(), OperationId = command.OperationId, StoreId = actor.Device.StoreId,
            UserId = actor.User.Id, MobileDeviceId = actor.Device.Id,
            SupplierId = command.SupplierId is Guid id && id != Guid.Empty ? id : null,
            ProposedPayFromCashRegister = command.PayFromCashRegister,
            ProposedCashWithdrawn = command.PayFromCashRegister && command.CashWithdrawn is decimal withdrawn
                ? decimal.Round(withdrawn, 2, MidpointRounding.AwayFromZero) : null,
            Status = "Pending", CreatedAtUtc = now
        };
        foreach (var line in command.Lines)
        {
            var product = products[line.ProductId];
            var quantity = decimal.Round(line.Quantity, 3, MidpointRounding.AwayFromZero);
            var unitCost = decimal.Round(line.UnitCost, 2, MidpointRounding.AwayFromZero);
            var lineTotal = decimal.Round(quantity * unitCost, 2, MidpointRounding.AwayFromZero);
            request.Total += lineTotal;
            database.MobilePurchaseRequestLines.Add(new MobilePurchaseRequestLineRecord
            {
                Id = Guid.NewGuid(), RequestId = request.Id, ProductId = product.Id,
                Code = product.Code, Description = product.Description, UnitOfMeasure = product.UnitOfMeasure,
                Quantity = quantity, UnitCost = unitCost, LineTotal = lineTotal
            });
        }
        request.Total = decimal.Round(request.Total, 2, MidpointRounding.AwayFromZero);
        database.MobilePurchaseRequests.Add(request);
        await database.SaveChangesAsync(cancellationToken);
        return await RequestResultAsync(request.Id, cancellationToken);
    }

    public async Task<MobilePurchaseRequestResult?> GetMyRequestAsync(string accessToken, string deviceToken, Guid requestId, CancellationToken cancellationToken)
    {
        var actor = await AuthorizedActorAsync(accessToken, deviceToken, cancellationToken);
        if (actor is null) return null;
        var request = await database.MobilePurchaseRequests.AsNoTracking()
            .Where(item => item.Id == requestId && item.StoreId == actor.Device.StoreId && item.UserId == actor.User.Id && item.MobileDeviceId == actor.Device.Id)
            .Select(item => item.Id).SingleOrDefaultAsync(cancellationToken);
        return request == Guid.Empty ? null : await RequestResultAsync(request, cancellationToken);
    }

    public async Task<MobilePurchaseRequestResult?> GetForDesktopReviewAsync(Guid requestId, CancellationToken cancellationToken)
    {
        if (!await database.MobilePurchaseRequests.AnyAsync(item => item.Id == requestId, cancellationToken)) return null;
        return await RequestResultAsync(requestId, cancellationToken);
    }

    private async Task<MobilePurchaseRequestResult> RequestResultAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var row = await (from request in database.MobilePurchaseRequests.AsNoTracking()
                         join user in database.Users.AsNoTracking() on request.UserId equals user.Id
                         join device in database.MobileDevices.AsNoTracking() on request.MobileDeviceId equals device.Id
                         join supplierRecord in database.Suppliers.AsNoTracking() on request.SupplierId equals supplierRecord.Id into supplierRows
                         from supplier in supplierRows.DefaultIfEmpty()
                         where request.Id == requestId
                         select new { request, user, device, Supplier = supplier == null ? "Sin proveedor" : supplier.Name })
            .SingleAsync(cancellationToken);
        var lines = await database.MobilePurchaseRequestLines.AsNoTracking().Where(item => item.RequestId == requestId)
            .OrderBy(item => item.Description)
            .Select(item => new MobilePurchaseRequestLineResult(item.ProductId, item.Code, item.Description, item.UnitOfMeasure, item.Quantity, item.UnitCost, item.LineTotal))
            .ToListAsync(cancellationToken);
        return new MobilePurchaseRequestResult(row.request.Id, row.request.OperationId, row.request.SupplierId, row.Supplier,
            row.user.DisplayName, row.device.Name, row.request.Status, row.request.Total, row.request.ProposedPayFromCashRegister,
            row.request.ProposedCashWithdrawn, row.request.CreatedAtUtc, row.request.ReviewedAtUtc, row.request.RejectionReason, lines);
    }

    private async Task<MobileActor?> AuthorizedActorAsync(string accessToken, string deviceToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(deviceToken)) return null;
        var accessHash = Hash(accessToken);
        var deviceHash = Hash(deviceToken);
        var actor = await (from session in database.MobileSessions.AsNoTracking()
                           join device in database.MobileDevices.AsNoTracking() on session.MobileDeviceId equals device.Id
                           join user in database.Users.AsNoTracking() on session.UserId equals user.Id
                           where session.TokenHash == accessHash && session.RevokedAtUtc == null && session.ExpiresAtUtc > DateTimeOffset.UtcNow
                              && device.DeviceTokenHash == deviceHash && device.IsActive && user.IsActive
                           select new MobileActor(user, device)).SingleOrDefaultAsync(cancellationToken);
        return actor;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record MobileActor(UserRecord User, MobileDeviceRecord Device);
}
