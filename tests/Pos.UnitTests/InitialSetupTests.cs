using Pos.Infrastructure;

namespace Pos.UnitTests;

public sealed class InitialSetupTests
{
    [Theory]
    [InlineData("Minisúper")]
    [InlineData("Papelería")]
    [InlineData("Electrónica")]
    [InlineData("Dulcería")]
    [InlineData("Vinos y licores")]
    public void AcceptsBusinessTypesDisplayedDuringInitialSetup(string businessType)
    {
        Assert.True(InitialSetupService.IsSupportedBusinessType(businessType));
    }

    [Fact]
    public void RejectsAnUnknownBusinessType()
    {
        Assert.False(InitialSetupService.IsSupportedBusinessType("Giro inexistente"));
    }
}
