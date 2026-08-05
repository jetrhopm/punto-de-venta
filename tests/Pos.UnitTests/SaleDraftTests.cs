using Pos.Domain;

namespace Pos.UnitTests;

public sealed class SaleDraftTests
{
    [Fact]
    public void AddsLinesAndCalculatesTheTotal()
    {
        var draft = new SaleDraft(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        draft.AddLine(Guid.NewGuid(), "Cafe molido", 2m, Money.From(75.50m));
        draft.AddLine(Guid.NewGuid(), "Manzana", 0.375m, Money.From(18.90m));

        Assert.Equal(2, draft.Lines.Count);
        Assert.Equal(158.09m, draft.Total.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveQuantities(decimal quantity)
    {
        var draft = new SaleDraft(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            draft.AddLine(Guid.NewGuid(), "Producto", quantity, Money.From(1m)));
    }
}
