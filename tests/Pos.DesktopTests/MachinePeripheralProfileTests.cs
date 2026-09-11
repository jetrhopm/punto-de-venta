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
    }
}
