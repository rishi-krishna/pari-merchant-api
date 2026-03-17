using PaRiMerchant.Application.Abstractions;
using Xunit;

namespace PaRiMerchant.Tests.Unit;

public sealed class MaskingTests
{
    [Fact]
    public void Phone_Masks_All_But_Last_Four()
    {
        var masked = Masking.Phone("9000001234");
        Assert.Equal("******1234", masked);
    }

    [Fact]
    public void AccountNumber_Masks_All_But_Last_Four()
    {
        var masked = Masking.AccountNumber("12345678901234");
        Assert.Equal("**********1234", masked);
    }

    [Fact]
    public void Pan_Masks_Middle_Characters()
    {
        var masked = Masking.Pan("ABCDE1234F");
        Assert.Equal("AB******4F", masked);
    }
}
