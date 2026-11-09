using System.Security.Claims;
using Auth.Application.Commands.AdminResetPassword;
using Auth.Application.Commands.Login;
using Auth.Application.Commands.RegisterFromPlanning;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth.Application.UnitTests;

/// <summary>
/// v38 : « e-mail ou mot de passe incorrect » après une réinitialisation. Chaque cause trouvée a son test.
/// </summary>
public class ConnexionApresReinitialisationTests
{
    private const string Mdp = "Nouveau-Mdp-2026!";
    private static readonly Guid Employe = Guid.Parse("5f0c8a52-1111-4a5e-9d0e-2b7a1c3d4e5f");

    [Fact]
    public async Task Reinitialisation_pose_le_mot_de_passe_sur_le_compte_de_l_email_de_connexion()
    {
        // Ancien compte avec le GUID et un ancien e-mail, compte actuel avec l'e-mail de l'application.
        var ancien = new User { Id = 1, Email = "ancien@kyntus.com", Username = "ancien@kyntus.com", SubjectId = Employe, PasswordHash = "x" };
        var actuel = new User { Id = 2, Email = "pilote@kyntus.com", Username = "pilote@kyntus.com", SubjectId = Guid.NewGuid(), PasswordHash = "y" };
        var repo = new Repo(ancien, actuel);

        await Reset(repo, new AdminResetPasswordDto { EmployeeId = Employe, Email = "pilote@kyntus.com", NewPassword = Mdp, Activer = true });

        Assert.Equal("hash:" + Mdp, actuel.PasswordHash);
        Assert.Equal(Employe, actuel.SubjectId);
        Assert.Equal("x", ancien.PasswordHash);
        var login = await Login(repo, "pilote@kyntus.com", Mdp);
        Assert.Equal(2, login.User.Id);
    }

    [Fact]
    public async Task Reinitialisation_reactive_un_compte_desactive_si_le_collaborateur_est_actif()
    {
        var u = new User { Id = 3, Email = "pilote@kyntus.com", Username = "pilote@kyntus.com", SubjectId = Employe, IsActive = false };
        var repo = new Repo(u);

        var r = await Reset(repo, new AdminResetPasswordDto { EmployeeId = Employe, Email = "pilote@kyntus.com", NewPassword = Mdp, Activer = true });

        Assert.True(u.IsActive);
        Assert.Contains(r.Corrections, c => c.Contains("réactivé"));
        Assert.Equal(3, (await Login(repo, "pilote@kyntus.com", Mdp)).User.Id);
    }

    [Fact]
    public async Task Reinitialisation_leve_le_verrouillage()
    {
        var u = new User { Id = 4, Email = "pilote@kyntus.com", Username = "pilote@kyntus.com", SubjectId = Employe, LockoutEnd = DateTime.UtcNow.AddMinutes(10), AccessFailedCount = 3 };
        var repo = new Repo(u);

        var r = await Reset(repo, new AdminResetPasswordDto { EmployeeId = Employe, Email = "pilote@kyntus.com", NewPassword = Mdp });

        Assert.Null(u.LockoutEnd);
        Assert.Contains(r.Corrections, c => c.Contains("verrouillé"));
    }

    [Fact]
    public async Task Reinitialisation_corrige_l_email_d_un_compte_retrouve_par_guid()
    {
        var u = new User { Id = 5, Email = "ancien@kyntus.com", Username = "ancien@kyntus.com", SubjectId = Employe };
        var repo = new Repo(u);

        var r = await Reset(repo, new AdminResetPasswordDto { EmployeeId = Employe, Email = "pilote@kyntus.com", NewPassword = Mdp });

        Assert.Equal("pilote@kyntus.com", u.Email);
        Assert.Contains(r.Corrections, c => c.Contains("E-mail de connexion corrigé"));
        Assert.Equal(5, (await Login(repo, "pilote@kyntus.com", Mdp)).User.Id);
    }

    [Fact]
    public async Task Connexion_ignore_les_espaces_autour_de_l_email_et_du_mot_de_passe()
    {
        var repo = new Repo(new User { Id = 6, Email = "pilote@kyntus.com", Username = "pilote@kyntus.com", PasswordHash = "hash:" + Mdp });

        Assert.Equal(6, (await Login(repo, "  pilote@kyntus.com ", Mdp + " ")).User.Id);
    }

    [Fact]
    public async Task Compte_desactive_avec_le_bon_mot_de_passe_le_dit_clairement()
    {
        var repo = new Repo(new User { Id = 7, Email = "pilote@kyntus.com", Username = "pilote@kyntus.com", PasswordHash = "hash:" + Mdp, IsActive = false });

        var bon = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Login(repo, "pilote@kyntus.com", Mdp));
        Assert.Contains("désactivé", bon.Message);
        var faux = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Login(repo, "pilote@kyntus.com", "autre"));
        Assert.Equal("Email ou mot de passe incorrect", faux.Message);
    }

    [Fact]
    public async Task Synchronisation_n_ecrase_jamais_le_mot_de_passe_avec_un_mot_de_passe_genere()
    {
        var u = new User { Id = 8, Email = "pilote@kyntus.com", Username = "pilote@kyntus.com", SubjectId = Employe, PasswordHash = "hash:" + Mdp, RoleId = 1 };
        var repo = new Repo(u);

        await Register(repo, new RegisterFromPlanningDto { Email = "pilote@kyntus.com", DefaultPassword = "Genere-Auto-99!", RoleName = "Pilote", EmployeeId = Employe, ImposerMotDePasse = false });

        Assert.Equal("hash:" + Mdp, u.PasswordHash);
    }

    [Fact]
    public async Task Synchronisation_corrige_l_email_au_lieu_de_creer_un_doublon()
    {
        var u = new User { Id = 9, Email = "ancien@kyntus.com", Username = "ancien@kyntus.com", SubjectId = Employe, PasswordHash = "hash:" + Mdp, RoleId = 1 };
        var repo = new Repo(u);

        var r = await Register(repo, new RegisterFromPlanningDto { Email = "pilote@kyntus.com", DefaultPassword = "Genere-Auto-99!", RoleName = "Pilote", EmployeeId = Employe, ImposerMotDePasse = false });

        Assert.Single(repo.Users);
        Assert.Equal(9, r.Id);
        Assert.Equal("pilote@kyntus.com", u.Email);
        Assert.Equal("hash:" + Mdp, u.PasswordHash);
    }

    private static Task<AdminResetPasswordResult> Reset(Repo repo, AdminResetPasswordDto dto) =>
        new AdminResetPasswordCommandHandler(repo, new Hasher(), NullLogger<AdminResetPasswordCommandHandler>.Instance)
            .Handle(new AdminResetPasswordCommand(dto), CancellationToken.None);

    private static Task<AuthResponseDto> Login(Repo repo, string email, string password) =>
        new LoginCommandHandler(repo, new Jwt(), new Hasher(), NullLogger<LoginCommandHandler>.Instance, new Audit())
            .Handle(new LoginCommand(new LoginDto { Email = email, Password = password }), CancellationToken.None);

    private static Task<RegisterFromPlanningResponseDto> Register(Repo repo, RegisterFromPlanningDto dto) =>
        new RegisterFromPlanningCommandHandler(repo, new Roles(), new Hasher(), new Subjects(), NullLogger<RegisterFromPlanningCommandHandler>.Instance)
            .Handle(new RegisterFromPlanningCommand(dto), CancellationToken.None);

    private sealed class Hasher : IPasswordHasher
    {
        public string HashPassword(string password) => "hash:" + password;
        public bool VerifyPassword(string hashedPassword, string providedPassword) => hashedPassword == "hash:" + providedPassword;
    }

    private sealed class Jwt : IJwtService
    {
        public string GenerateAccessToken(User user) => "jeton";
        public string GenerateRefreshToken() => "rafraichissement";
        public ClaimsPrincipal? ValidateToken(string token) => null;
        public int AccessTokenExpiresInSeconds => 3600;
    }

    private sealed class Audit : IAccessAuditWriter
    {
        public Task WriteAsync(string eventType, string email, int? userId, string? message, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AccessAuditRecord>> ListAsync(int take, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccessAuditRecord>>([]);
    }

    private sealed class Roles : IRoleRepository
    {
        private static readonly Role Pilote = new() { Id = 1, Name = "Pilote" };
        public Task<Role?> GetByIdAsync(int id, CancellationToken ct = default) => Task.FromResult<Role?>(Pilote);
        public Task<Role?> GetByNameAsync(string name, CancellationToken ct = default) => Task.FromResult<Role?>(Pilote);
    }

    private sealed class Subjects : ISubjectIdResolver
    {
        public Guid ResolveForEmail(string email) => Guid.NewGuid();
    }

    private sealed class Repo(params User[] users) : IUserRepository
    {
        public List<User> Users { get; } = [.. users];
        public Task<User?> GetByIdAsync(int id, CancellationToken ct = default) => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));
        public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
            Task.FromResult(Users.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));
        public Task<User?> GetBySubjectIdAsync(Guid subjectId, CancellationToken ct = default) => Task.FromResult(Users.FirstOrDefault(u => u.SubjectId == subjectId));
        public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) => Task.FromResult(Users.FirstOrDefault(u => u.Username == username));
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<User>>(Users);
        public Task<User> CreateAsync(User user, CancellationToken ct = default) { Users.Add(user); return Task.FromResult(user); }
        public Task<User> UpdateAsync(User user, CancellationToken ct = default) => Task.FromResult(user);
        public Task<bool> DeleteAsync(int id, CancellationToken ct = default) => Task.FromResult(Users.RemoveAll(u => u.Id == id) > 0);
        public Task<bool> ExistsAsync(string email, CancellationToken ct = default) => Task.FromResult(Users.Any(u => u.Email == email));
        public Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default) => Task.FromResult(Users.Any(u => u.Username == username));
    }
}
