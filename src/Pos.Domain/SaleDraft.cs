namespace Pos.Domain;

public sealed class SaleDraft
{
    private readonly List<SaleDraftLine> _lines = [];

    public SaleDraft(Guid id, Guid operationId, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("El identificador del borrador es obligatorio.", nameof(id));
        }

        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de operacion es obligatorio.", nameof(operationId));
        }

        Id = id;
        OperationId = operationId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid OperationId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyList<SaleDraftLine> Lines => _lines;

    public Money Total => _lines.Aggregate(Money.Zero, static (total, line) => total + line.Total);

    public void AddLine(Guid productId, string description, decimal quantity, Money unitPrice)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("El producto es obligatorio.", nameof(productId));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("La descripcion del producto es obligatoria.", nameof(description));
        }

        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "La cantidad debe ser mayor que cero.");
        }

        if (unitPrice.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "El precio no puede ser negativo.");
        }

        _lines.Add(new SaleDraftLine(Guid.NewGuid(), productId, description.Trim(), quantity, unitPrice));
    }
}

public sealed record SaleDraftLine(
    Guid Id,
    Guid ProductId,
    string Description,
    decimal Quantity,
    Money UnitPrice)
{
    public Money Total => UnitPrice * Quantity;
}
