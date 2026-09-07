namespace Pos.Infrastructure;

public static class SaleAmountRounding
{
    public static decimal Apply(decimal amount, bool enabled, string? mode)
    {
        var value = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (!enabled) return value;

        return string.Equals(mode, "Whole", StringComparison.OrdinalIgnoreCase)
            ? decimal.Ceiling(value)
            : decimal.Ceiling(value * 10m) / 10m;
    }
}
