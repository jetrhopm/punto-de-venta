using Pos.Domain;
using Pos.Infrastructure;

namespace Pos.UnitTests;

public sealed class MoneyTests
{
    [Fact]
    public void RoundsMidpointsAwayFromZeroToTwoDecimals()
    {
        var amount = Money.From(12.345m);

        Assert.Equal(12.35m, amount.Amount);
    }

    [Fact]
    public void CalculatesFractionalQuantityWithoutFloat()
    {
        var total = Money.From(18.90m) * 0.375m;

        Assert.Equal(7.09m, total.Amount);
    }

    [Theory]
    [InlineData(45.52, true, "Tenths", 45.60)]
    [InlineData(45.52, true, "Whole", 46.00)]
    [InlineData(45.52, false, "Tenths", 45.52)]
    public void AppliesConfiguredSaleRounding(decimal subtotal, bool enabled, string mode, decimal expected)
    {
        Assert.Equal(expected, SaleAmountRounding.Apply(subtotal, enabled, mode));
    }
}
