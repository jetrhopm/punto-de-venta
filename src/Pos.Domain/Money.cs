namespace Pos.Domain;

/// <summary>
/// Monetary amount used by the POS. Values are always stored with two decimal places.
/// </summary>
public readonly record struct Money
{
    public Money(decimal amount)
    {
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    public decimal Amount { get; }

    public static Money Zero => new(0m);

    public static Money From(decimal amount) => new(amount);

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);

    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);

    public static Money operator *(Money value, decimal quantity) => new(value.Amount * quantity);

    public override string ToString() => Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
