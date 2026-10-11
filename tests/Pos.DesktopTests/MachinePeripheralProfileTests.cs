using Pos.Desktop;

namespace Pos.DesktopTests;

public sealed class MachinePeripheralProfileTests
{
    [Fact]
    public void CashDrawerProfileKeepsAValidMachineConfiguration()
    {
        var profile = new CashDrawerProfile(true, "Ticketera caja 2", "EpsonDrawer1", "COM2").Normalize();

        Assert.True(profile.Enabled);
        Assert.Equal("Ticketera caja 2", profile.PrinterName);
        Assert.Equal("EpsonDrawer1", profile.Model);
        Assert.Equal("COM2", profile.Port);
    }

    [Fact]
    public void ScaleProfileReplacesInvalidValuesWithSafeDefaults()
    {
        var profile = new ScaleProfile(true, " com3 ", 77, "Invalid", 6, "Three", "Text", "Onzas", 20).Normalize();

        Assert.True(profile.Enabled);
        Assert.Equal("COM3", profile.Port);
        Assert.Equal(9600, profile.BaudRate);
        Assert.Equal("None", profile.Parity);
        Assert.Equal(8, profile.DataBits);
        Assert.Equal("One", profile.StopBits);
        Assert.Equal("CRLF", profile.Terminator);
        Assert.Equal("Kilogramo", profile.Unit);
        Assert.Equal(1500, profile.ReadTimeoutMs);
        Assert.Equal("Generic", profile.Model);
    }

    [Fact]
    public void ScaleProfileKeepsTorreyPcr40Protocol()
    {
        var profile = new ScaleProfile(true, "com7", 115200, "None", 8, "One", "CR", "Kilogramo", 1500, "TorreyPcr40").Normalize();

        Assert.Equal("TorreyPcr40", profile.Model);
        Assert.Equal("COM7", profile.Port);
        Assert.Equal(115200, profile.BaudRate);
        Assert.Equal("CR", profile.Terminator);
    }

    [Fact]
    public void ScaleProfileKeepsRhinoBar6XProtocol()
    {
        var profile = new ScaleProfile(true, "com4", 9600, "None", 8, "One", "CRLF", "Kilogramo", 1500, "RhinoBar6X").Normalize();

        Assert.Equal("RhinoBar6X", profile.Model);
        Assert.Equal("COM4", profile.Port);
        Assert.Equal(9600, profile.BaudRate);
    }

    [Fact]
    public void ScaleReadingConvertsKilogramsToBulkGrams()
    {
        var quantity = ScaleReadingService.ConvertWeight(1.25m, "Kilogramo", "Gramo (granel)");

        Assert.Equal(1250m, quantity);
    }
}
