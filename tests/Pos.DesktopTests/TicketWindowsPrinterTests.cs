using System.Threading;
using System.Windows;

namespace Pos.DesktopTests;

public sealed class TicketWindowsPrinterTests
{
    [Fact]
    public void CreateSample_UsesRequestedWidthAndFractionalQuantities()
    {
        var ticket = Pos.Desktop.TicketWindowsPrinter.CreateSample(58);

        Assert.Equal(58, ticket.WidthMm);
        Assert.Contains(ticket.Lines, line => line.Quantity == 0.750m);
        Assert.NotEmpty(ticket.Payments);
    }

    [Fact]
    public void CreateTicketVisual_BuildsThermalWidthWithoutErrors()
    {
        Exception? failure = null;
        double width = 0d;
        var thread = new Thread(() =>
        {
            try
            {
                var ticket = Pos.Desktop.TicketWindowsPrinter.CreateSample(80);
                var visual = Pos.Desktop.TicketWindowsPrinter.CreateTicketVisual(ticket, new("Consolas", 9d, false, 80));
                visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                width = visual.DesiredSize.Width;
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "La creación visual no terminó a tiempo.");

        Assert.Null(failure);
        Assert.InRange(width, 300d, 304d);
    }
}
