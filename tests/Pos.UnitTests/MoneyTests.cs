using Pos.Domain;

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
}
