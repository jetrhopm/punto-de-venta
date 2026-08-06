using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record CustomerCommand(string Name, string? Phone, string? Email, string? TaxId, decimal CreditLimit, bool CreditEnabled);
public sealed record CustomerResult(Guid Id, string Name, string? Phone, string? Email, string? TaxId, decimal CreditLimit, bool CreditEnabled, bool IsActive, decimal Balance);
public sealed record CreditPaymentCommand(Guid OperationId, Guid CustomerId, decimal Amount, string Reason);
public sealed record CreditPaymentResult(Guid TransactionId, Guid CustomerId, decimal Amount, decimal BalanceBefore, decimal BalanceAfter);
public sealed record CreditStatementItem(Guid Id, string Type, decimal Amount, decimal BalanceBefore, decimal BalanceAfter, string Reason, DateTimeOffset CreatedAtUtc);

public sealed class CustomerCreditService(PosDbContext database)
{
    public async Task<IReadOnlyList<CustomerResult>?> ListAsync(string token, string? query, CancellationToken cancellationToken)
    {
        if (await GetUserAsync(token, "ManageCustomersAndCredit", cancellationToken) is null) return null;
        var search = (query ?? string.Empty).Trim().ToUpperInvariant();
        var customers = await database.Customers.AsNoTracking().Where(item => item.IsActive && (search.Length == 0 || item.Name.ToUpper().Contains(search) || (item.Phone ?? "").Contains(search))).OrderBy(item => item.Name).Take(100).ToListAsync(cancellationToken);
        var balances = await database.CreditTransactions.AsNoTracking().GroupBy(item => item.CustomerId).Select(group => new { CustomerId = group.Key, Balance = group.Sum(item => item.Amount) }).ToDictionaryAsync(item => item.CustomerId, item => item.Balance, cancellationToken);
        return customers.Select(item => ToResult(item, balances.GetValueOrDefault(item.Id))).ToArray();
    }

    public async Task<CustomerResult?> CreateAsync(string token, CustomerCommand command, CancellationToken cancellationToken)
    {
        if (await GetUserAsync(token, "ManageCustomersAndCredit", cancellationToken) is null) return null;
        Validate(command);
        var customer = new CustomerRecord { Id = Guid.NewGuid(), Name = command.Name.Trim(), Phone = Clean(command.Phone), Email = Clean(command.Email), TaxId = Clean(command.TaxId), CreditLimit = decimal.Round(command.CreditLimit, 2), CreditEnabled = command.CreditEnabled, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        database.Customers.Add(customer); await database.SaveChangesAsync(cancellationToken);
        return ToResult(customer, 0m);
    }

    public async Task<CustomerResult?> UpdateAsync(string token, Guid customerId, CustomerCommand command, CancellationToken cancellationToken)
    {
        if (await GetUserAsync(token, "ManageCustomersAndCredit", cancellationToken) is null) return null;
        Validate(command);
        var customer = await database.Customers.SingleOrDefaultAsync(item => item.Id == customerId, cancellationToken) ?? throw new KeyNotFoundException("Cliente no encontrado.");
        var balance = await BalanceAsync(customerId, cancellationToken);
        if (command.CreditLimit < balance) throw new InvalidOperationException("El limite no puede ser menor que el saldo actual.");
        customer.Name = command.Name.Trim(); customer.Phone = Clean(command.Phone); customer.Email = Clean(command.Email); customer.TaxId = Clean(command.TaxId); customer.CreditLimit = decimal.Round(command.CreditLimit, 2); customer.CreditEnabled = command.CreditEnabled;
        await database.SaveChangesAsync(cancellationToken); return ToResult(customer, balance);
    }

    public async Task<CreditPaymentResult?> ApplyPaymentAsync(string token, CreditPaymentCommand command, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(token, "ManageCustomersAndCredit", cancellationToken);
        if (user is null) return null;
        if (command.OperationId == Guid.Empty || command.Amount <= 0m || string.IsNullOrWhiteSpace(command.Reason)) throw new ArgumentException("El abono requiere operacion, importe positivo y motivo.");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await database.CreditTransactions.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new CreditPaymentResult(existing.Id, existing.CustomerId, -existing.Amount, existing.BalanceBefore, existing.BalanceAfter);
        var customer = await database.Customers.SingleOrDefaultAsync(item => item.Id == command.CustomerId && item.IsActive, cancellationToken) ?? throw new KeyNotFoundException("Cliente no encontrado.");
        var before = await BalanceAsync(customer.Id, cancellationToken);
        if (command.Amount > before) throw new InvalidOperationException("El abono no puede ser mayor que el saldo pendiente.");
        var shift = await database.Shifts.SingleOrDefaultAsync(item => item.UserId == user.Id && item.Status == "Open", cancellationToken) ?? throw new InvalidOperationException("El usuario no tiene un turno abierto.");
        var amount = decimal.Round(-command.Amount, 2);
        var record = new CreditTransactionRecord { Id = Guid.NewGuid(), CustomerId = customer.Id, UserId = user.Id, OperationId = command.OperationId, Type = "Payment", Amount = amount, BalanceBefore = before, BalanceAfter = before + amount, Reason = command.Reason.Trim(), CreatedAtUtc = DateTimeOffset.UtcNow };
        database.CreditTransactions.Add(record); database.CashMovements.Add(new CashMovementRecord { Id = Guid.NewGuid(), ShiftId = shift.Id, Type = "In", Amount = -amount, Reason = $"Abono cliente: {customer.Name}", CreatedAtUtc = record.CreatedAtUtc });
        await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new CreditPaymentResult(record.Id, record.CustomerId, command.Amount, record.BalanceBefore, record.BalanceAfter);
    }

    public async Task<IReadOnlyList<CreditStatementItem>?> StatementAsync(string token, Guid customerId, CancellationToken cancellationToken)
    {
        if (await GetUserAsync(token, "ManageCustomersAndCredit", cancellationToken) is null) return null;
        return await database.CreditTransactions.AsNoTracking().Where(item => item.CustomerId == customerId).OrderByDescending(item => item.CreatedAtUtc).Select(item => new CreditStatementItem(item.Id, item.Type, item.Amount, item.BalanceBefore, item.BalanceAfter, item.Reason, item.CreatedAtUtc)).Take(200).ToListAsync(cancellationToken);
    }

    private async Task<decimal> BalanceAsync(Guid customerId, CancellationToken cancellationToken) => await database.CreditTransactions.Where(item => item.CustomerId == customerId).SumAsync(item => item.Amount, cancellationToken);
    private async Task<UserRecord?> GetUserAsync(string token, string permission, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission, cancellationToken) ? user : null;
    }
    private static CustomerResult ToResult(CustomerRecord item, decimal balance) => new(item.Id, item.Name, item.Phone, item.Email, item.TaxId, item.CreditLimit, item.CreditEnabled, item.IsActive, balance);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Validate(CustomerCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 160 || command.CreditLimit < 0m) throw new ArgumentException("Nombre y limite de credito son obligatorios y validos.");
        if (command.Phone?.Length > 40 || command.Email?.Length > 160 || command.TaxId?.Length > 20) throw new ArgumentException("Los datos de contacto exceden su longitud permitida.");
    }
}
