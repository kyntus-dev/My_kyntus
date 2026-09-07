using Planning.Application.PilotPlanning;

namespace Planning.UnitTests;

public class PlanningSenderAddressTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeEmailOrNull_empty_is_null(string? value) =>
        Assert.Null(PlanningSenderAddress.NormalizeEmailOrNull(value));

    [Theory]
    [InlineData("pas-un-email")]
    [InlineData("a@")]
    [InlineData("planning@")]
    public void NormalizeEmailOrNull_invalid_is_null(string value) =>
        Assert.Null(PlanningSenderAddress.NormalizeEmailOrNull(value));

    [Fact]
    public void NormalizeEmailOrNull_trims_valid_address() =>
        Assert.Equal("planning@entreprise.ma", PlanningSenderAddress.NormalizeEmailOrNull("  planning@entreprise.ma  "));

    [Fact]
    public void NormalizeEmailForSave_empty_is_null() =>
        Assert.Null(PlanningSenderAddress.NormalizeEmailForSave("  "));

    [Fact]
    public void NormalizeEmailForSave_invalid_throws() =>
        Assert.Throws<ArgumentException>(() => PlanningSenderAddress.NormalizeEmailForSave("not-an-email"));

    [Fact]
    public void Resolve_invalid_rh_address_falls_back_to_smtp()
    {
        var (address, _) = PlanningSenderAddress.Resolve(
            "pas-un-email",
            "RH",
            "ops@smtp.local",
            "user@smtp.local");

        Assert.Equal("ops@smtp.local", address);
    }

    [Fact]
    public void Resolve_admin_wins_over_smtp_env()
    {
        var (address, name) = PlanningSenderAddress.Resolve(
            "  planning@entreprise.ma ",
            "  Kyntus Planning ",
            "ops@smtp.local",
            "smtp-user@smtp.local");

        Assert.Equal("planning@entreprise.ma", address);
        Assert.Equal("Kyntus Planning", name);
    }

    [Fact]
    public void Resolve_falls_back_to_smtp_from_then_user()
    {
        var fromSmtp = PlanningSenderAddress.Resolve(null, "Kyntus Planning", "ops@smtp.local", "user@smtp.local");
        Assert.Equal("ops@smtp.local", fromSmtp.Address);
        Assert.Equal("Kyntus Planning", fromSmtp.DisplayName);

        var fromUser = PlanningSenderAddress.Resolve("  ", null, "  ", "user@smtp.local");
        Assert.Equal("user@smtp.local", fromUser.Address);
        Assert.Null(fromUser.DisplayName);
    }

    [Fact]
    public void Resolve_uses_local_fallback_when_nothing_set()
    {
        var (address, name) = PlanningSenderAddress.Resolve(null, null, null, null);
        Assert.Equal(PlanningSenderAddress.FallbackAddress, address);
        Assert.Null(name);
    }
}
