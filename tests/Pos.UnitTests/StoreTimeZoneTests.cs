using Pos.Infrastructure;
using Pos.Printing;
using System.Text;

namespace Pos.UnitTests;

public sealed class StoreTimeZoneTests
{
    [Fact]
    public void MexicoCityDayRangeUsesTheConfiguredStoreZone()
    {
        var zone = StoreTimeZone.Resolve("America/Mexico_City");
        var range = StoreTimeZone.DayRangeUtc(new DateOnly(2026, 9, 12), zone);

        Assert.Equal(new DateTimeOffset(2026, 9, 12, 6, 0, 0, TimeSpan.Zero), range.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 6, 0, 0, TimeSpan.Zero), range.ToUtc);
    }

    [Fact]
    public void TicketUsesTheAlreadyConvertedStoreTime()
    {
        var localMexicoTime = new DateTimeOffset(2026, 9, 12, 18, 45, 0, TimeSpan.FromHours(-6));
        var ticket = new TicketPdfData("Tienda", Guid.NewGuid(), localMexicoTime, [new TicketPdfLine("Producto", 1m, 10m, 10m)], 10m, 10m, 0m);

        var pdf = Encoding.Latin1.GetString(TicketPdfWriter.Create(ticket));

        Assert.Contains("FECHA: 12/09/2026 18:45:00", pdf);
    }
}
