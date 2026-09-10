namespace Pos.Desktop;

public sealed record CustomerView(
    Guid Id,
    string Name,
    string? Phone,
    string? Email,
    string? TaxId,
    decimal CreditLimit,
    bool CreditEnabled,
    bool IsActive,
    decimal Balance)
{
    public decimal AvailableCredit => Math.Max(0m, CreditLimit - Balance);
    public string ContactSummary => string.Join("  |  ", new[] { Phone, Email }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string CreditStatus => !IsActive ? "Inactivo" : CreditEnabled ? "Crédito activo" : "Sin crédito";
    public string BalanceDisplay => $"${Balance:N2}";
}

public sealed record CustomerEditorResult(string Name, string? Phone, string? Email, string? TaxId);

public sealed record CreditStatementView(Guid Id, string Type, decimal Amount, decimal BalanceBefore, decimal BalanceAfter, string Reason, DateTimeOffset CreatedAtUtc)
{
    public string DateDisplay => CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string TypeDisplay => Type == "Payment" ? "Abono" : Type;
    public string AmountDisplay => Amount.ToString("C2");
    public string BalanceDisplay => BalanceAfter.ToString("C2");
}

public enum CustomerDetailsAction
{
    None,
    Edit,
    ManageCredit,
    Deactivate
}
