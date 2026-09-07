using Auth.Application.Push;

namespace Auth.Application.UnitTests;

public class DesktopPushPayloadSanitizerTests
{
    [Fact]
    public void Title_defaults_and_truncates()
    {
        Assert.Equal("Kyntus", DesktopPushPayloadSanitizer.SanitizeTitle("  "));
        Assert.Equal("Planning", DesktopPushPayloadSanitizer.SanitizeTitle(" Planning "));
        Assert.Equal(80, DesktopPushPayloadSanitizer.SanitizeTitle(new string('A', 120)).Length);
    }

    [Fact]
    public void Body_defaults_and_truncates()
    {
        Assert.Equal("Nouvelle notification.", DesktopPushPayloadSanitizer.SanitizeBody(null));
        Assert.Equal(140, DesktopPushPayloadSanitizer.SanitizeBody(new string('B', 200)).Length);
    }

    [Fact]
    public void Tag_allows_only_known_values()
    {
        Assert.Equal("planning", DesktopPushPayloadSanitizer.SanitizeTag("Planning"));
        Assert.Equal("prime", DesktopPushPayloadSanitizer.SanitizeTag("prime"));
        Assert.Equal("kyntus", DesktopPushPayloadSanitizer.SanitizeTag("salary"));
    }

    [Fact]
    public void Url_must_be_relative()
    {
        Assert.Equal("/mes-plannings", DesktopPushPayloadSanitizer.SanitizeUrl("/mes-plannings"));
        Assert.Equal("/", DesktopPushPayloadSanitizer.SanitizeUrl("https://evil.example/steal"));
        Assert.Equal("/", DesktopPushPayloadSanitizer.SanitizeUrl("//evil.example"));
        Assert.Equal("/", DesktopPushPayloadSanitizer.SanitizeUrl(""));
    }
}
