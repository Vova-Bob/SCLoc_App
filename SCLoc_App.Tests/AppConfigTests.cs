using Xunit;
using SCLOCUA;

namespace SCLoc_App.Tests;

public class AppConfigTests
{
    [Fact]
    public void LocalizationPath_IsNotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(AppConfig.LocalizationPath));
    }
}
