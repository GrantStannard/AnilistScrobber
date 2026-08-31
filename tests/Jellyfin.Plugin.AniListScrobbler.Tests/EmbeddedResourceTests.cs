using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.AniListScrobbler.Tests;

public class EmbeddedResourceTests
{
    /// <summary>
    /// Plugin.GetPages builds this name from the namespace. If the csproj stops embedding the
    /// page, or it moves, Jellyfin serves a blank settings screen with no error anywhere.
    /// </summary>
    private const string ConfigPageResource = "Jellyfin.Plugin.AniListScrobbler.Configuration.configPage.html";

    [Fact]
    public void ConfigurationPage_IsEmbeddedUnderTheExpectedName()
    {
        var assembly = typeof(Plugin).Assembly;
        Assert.Contains(ConfigPageResource, assembly.GetManifestResourceNames());
    }

    [Fact]
    public void ConfigurationPage_ReferencesThePluginGuid()
    {
        var assembly = typeof(Plugin).Assembly;
        using var stream = assembly.GetManifestResourceStream(ConfigPageResource);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        var html = reader.ReadToEnd();

        // The page loads and saves configuration by GUID, so a mismatch with Plugin.Id would
        // silently read and write nothing.
        Assert.Contains("7254b521-24ce-4fb9-b8aa-ec40010a3f96", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AniListScrobbler/ValidateToken", html, StringComparison.Ordinal);
    }
}
