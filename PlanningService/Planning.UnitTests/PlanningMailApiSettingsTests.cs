using Planning.Application.DTOs.Planning;
using Planning.Application.PilotPlanning;
using Planning.Domain.Entities;

namespace Planning.UnitTests;

public class PlanningMailApiSettingsTests
{
    [Fact]
    public void NormalizeMode_defaults_to_smtp()
    {
        Assert.Equal(PlanningMailApiSettings.ModeSmtp, PlanningMailApiSettings.NormalizeMode(null));
        Assert.Equal(PlanningMailApiSettings.ModeSmtp, PlanningMailApiSettings.NormalizeMode("smtp"));
        Assert.Equal(PlanningMailApiSettings.ModeApiKey, PlanningMailApiSettings.NormalizeMode(" APIKEY "));
    }

    [Fact]
    public void NormalizeProvider_defaults_to_brevo()
    {
        Assert.Equal(PlanningMailApiSettings.ProviderBrevo, PlanningMailApiSettings.NormalizeProvider(null));
        Assert.Equal(PlanningMailApiSettings.ProviderSendGrid, PlanningMailApiSettings.NormalizeProvider("sendgrid"));
    }

    [Fact]
    public void WriteToDto_never_exposes_api_key()
    {
        var entity = new PlanningAutoGenerateSettings
        {
            MailSendMode = "ApiKey",
            MailApiProvider = "SendGrid",
            MailApiKey = "SG.super-secret",
        };
        var dto = new AutoGenerateSettingsDto();
        PlanningMailApiSettings.WriteToDto(entity, dto);

        Assert.Null(dto.MailApiKey);
        Assert.True(dto.MailApiKeySet);
        Assert.Equal("ApiKey", dto.MailSendMode);
        Assert.Equal("SendGrid", dto.MailApiProvider);
    }

    [Fact]
    public void ApplyToEntity_blank_key_does_not_wipe()
    {
        var entity = new PlanningAutoGenerateSettings { MailApiKey = "kept-key" };
        PlanningMailApiSettings.ApplyToEntity(entity, new AutoGenerateSettingsDto
        {
            MailSendMode = "ApiKey",
            MailApiProvider = "Brevo",
            MailApiKey = "  ",
        });

        Assert.Equal("kept-key", entity.MailApiKey);
        Assert.Equal("ApiKey", entity.MailSendMode);
        Assert.Equal("Brevo", entity.MailApiProvider);
    }

    [Fact]
    public void BuildRequest_brevo_uses_api_key_header_not_in_body()
    {
        var req = PlanningMailApiSettings.BuildRequest(
            "Brevo",
            " xkeysib-secret ",
            "rh@entreprise.ma",
            "Kyntus Planning",
            "pilot@entreprise.ma",
            "Planning S36",
            "<p>ok</p>");

        Assert.Equal("https://api.brevo.com/v3/smtp/email", req.Url);
        Assert.Equal("api-key", req.HeaderName);
        Assert.Equal("xkeysib-secret", req.HeaderValue);
        Assert.Contains("\"email\":\"rh@entreprise.ma\"", req.JsonBody);
        Assert.Contains("\"email\":\"pilot@entreprise.ma\"", req.JsonBody);
        Assert.Contains("htmlContent", req.JsonBody);
        Assert.DoesNotContain("xkeysib-secret", req.JsonBody);
    }

    [Fact]
    public void BuildRequest_sendgrid_uses_bearer_header()
    {
        var req = PlanningMailApiSettings.BuildRequest(
            "SendGrid",
            "SG.secret",
            "rh@entreprise.ma",
            null,
            "pilot@entreprise.ma",
            "Planning S36",
            "<p>ok</p>");

        Assert.Equal("https://api.sendgrid.com/v3/mail/send", req.Url);
        Assert.Equal("Authorization", req.HeaderName);
        Assert.Equal("Bearer SG.secret", req.HeaderValue);
        Assert.Contains("personalizations", req.JsonBody);
        Assert.Contains("text/html", req.JsonBody);
        Assert.DoesNotContain("SG.secret", req.JsonBody);
    }
}
