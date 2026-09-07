using Planning.Application.DTOs.Planning;
using Planning.Application.PilotPlanning;
using Planning.Domain.Entities;

namespace Planning.UnitTests;

public class PlanningSmtpSettingsTests
{
    [Fact]
    public void Resolve_db_host_wins_over_env()
    {
        var conn = PlanningSmtpSettings.Resolve(
            " smtp.admin.ma ",
            465,
            "rh@admin.ma",
            "secret-db",
            true,
            "smtp.env.ma",
            587,
            "env-user",
            "secret-env",
            false);

        Assert.NotNull(conn);
        Assert.Equal("smtp.admin.ma", conn.Host);
        Assert.Equal(465, conn.Port);
        Assert.Equal("rh@admin.ma", conn.User);
        Assert.Equal("secret-db", conn.Password);
        Assert.True(conn.EnableSsl);
    }

    [Fact]
    public void Resolve_falls_back_to_env_when_db_host_empty()
    {
        var conn = PlanningSmtpSettings.Resolve(
            "  ",
            465,
            "ignored",
            "ignored",
            true,
            "smtp.env.ma",
            2525,
            "env-user",
            "secret-env",
            false);

        Assert.NotNull(conn);
        Assert.Equal("smtp.env.ma", conn.Host);
        Assert.Equal(2525, conn.Port);
        Assert.Equal("env-user", conn.User);
        Assert.Equal("secret-env", conn.Password);
        Assert.False(conn.EnableSsl);
    }

    [Fact]
    public void Resolve_null_when_neither_host_set()
    {
        Assert.Null(PlanningSmtpSettings.Resolve(null, 587, null, null, true, "  ", 587, null, null, true));
    }

    [Fact]
    public void ApplyPassword_blank_keeps_existing()
    {
        Assert.Equal("kept", PlanningSmtpSettings.ApplyPassword("kept", null));
        Assert.Equal("kept", PlanningSmtpSettings.ApplyPassword("kept", "  "));
        Assert.Equal("new-secret", PlanningSmtpSettings.ApplyPassword("kept", " new-secret "));
        Assert.Equal("abcdabcdabcdabcd", PlanningSmtpSettings.ApplyPassword("kept", " abcd abcd abcd abcd "));
    }

    [Fact]
    public void MapSendFailure_explains_gmail_app_password()
    {
        var mapped = PlanningSmtpSettings.MapSendFailure(
            "534: 5.7.9 Application-specific password required. InvalidSecondFactor gsmtp");
        Assert.Contains("mot de passe d’application", mapped, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gsmtp", mapped, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteToDto_never_exposes_password()
    {
        var entity = new PlanningAutoGenerateSettings
        {
            SmtpHost = "smtp.admin.ma",
            SmtpPort = 587,
            SmtpUser = "rh",
            SmtpPassword = "super-secret",
            SmtpEnableSsl = true,
        };
        var dto = new AutoGenerateSettingsDto();
        PlanningSmtpSettings.WriteToDto(entity, dto);

        Assert.Null(dto.SmtpPassword);
        Assert.True(dto.SmtpPasswordSet);
        Assert.Equal("smtp.admin.ma", dto.SmtpHost);
        Assert.Equal("rh", dto.SmtpUser);
    }

    [Fact]
    public void ApplyToEntity_blank_password_does_not_wipe()
    {
        var entity = new PlanningAutoGenerateSettings { SmtpPassword = "kept" };
        PlanningSmtpSettings.ApplyToEntity(entity, new AutoGenerateSettingsDto
        {
            SmtpHost = "smtp.admin.ma",
            SmtpPort = 587,
            SmtpUser = "rh",
            SmtpPassword = "",
            SmtpEnableSsl = false,
        });

        Assert.Equal("kept", entity.SmtpPassword);
        Assert.Equal("smtp.admin.ma", entity.SmtpHost);
        Assert.False(entity.SmtpEnableSsl);
    }
}
