using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

public class ResetMotDePasseTests
{
    [Theory]
    [InlineData("{\"message\":\"Compte de connexion introuvable\"}", "Compte de connexion introuvable.")]
    [InlineData("{\"Message\":\"Le mot de passe est requis.\"}", "Le mot de passe est requis.")]
    [InlineData("<html>502</html>", "défaut")]
    [InlineData("", "défaut")]
    [InlineData("{\"message\":\"\"}", "défaut")]
    public void Message_auth_lisible(string body, string attendu) =>
        Assert.Equal(attendu, UserService.MessageAuth(body, "défaut"));
}
