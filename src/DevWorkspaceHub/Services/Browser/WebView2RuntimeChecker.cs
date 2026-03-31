using Microsoft.Web.WebView2.Core;

namespace DevWorkspaceHub.Services.Browser;

public enum WebView2Availability
{
    Available,
    OutdatedVersion,
    NotInstalled,
    Error
}

public static class WebView2RuntimeChecker
{
    private static readonly Version MinimumVersion = new("128.0.2739.0");

    public static WebView2Availability Check()
    {
        try
        {
            var versionString = CoreWebView2Environment.GetAvailableBrowserVersionString();

            if (string.IsNullOrEmpty(versionString))
                return WebView2Availability.NotInstalled;

            var installed = new Version(versionString);

            return installed >= MinimumVersion
                ? WebView2Availability.Available
                : WebView2Availability.OutdatedVersion;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return WebView2Availability.NotInstalled;
        }
        catch (Exception)
        {
            return WebView2Availability.Error;
        }
    }
}
