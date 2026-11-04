using Auth.Application.Commands.AdminResetPassword;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth.Application.UnitTests;

public class AdminResetPasswordCommandHandlerTests
{
    private const string Mdp = "Nouveau-Mdp-2026!";
    private static readonly Guid GuidFarah = Guid.Parse("c81a0010-6e4f-47af-8208-2e08f5e5a163");

    [Fact]
    public async Task Compte_retrouve_par_identifiant_auth_quand_le_guid_a_derive()
    {
        // Compte Auth créé avec un SubjectId calculé : le GUID du Planning ne le retrouve pas ;
        // l'identifiant Auth connu du Planning, si (même e-mail), et le GUID est réaligné.
        var repo = new Repo(new User { Id = 7, Email = "farah.mannad@kyntus.com", Username = "farah.mannad@kyntus.com", SubjectId = Guid.NewGuid() });
        var handler = new AdminResetPasswordCommandHandler(repo, new Hasher(), NullLogger<AdminResetPasswordCommandHandler>.Instance);

        await handler.Handle(new AdminResetPasswordCommand(new AdminResetPasswordDto
        {
            EmployeeId = GuidFarah, AuthUserId = 7, Email = "FARAH.MANNAD@kyntus.com", NewPassword = Mdp,
        }), CancellationToken.None);

        var u = repo.Users.Single();
        Assert.Equal("hash:" + Mdp, u.PasswordHash);
        Assert.Equal(GuidFarah, u.SubjectId);
    }

    [Fact]
    public async Task Identifiant_auth_perime_n_ecrase_jamais_le_compte_d_un_autre()
    {
        var autre = new User { Id = 7, Email = "autre@kyntus.com", Username = "autre@kyntus.com", SubjectId = Guid.NewGuid(), PasswordHash = "inchangé" };
        var repo = new Repo(autre);
        var handler = new AdminResetPasswordCommandHandler(repo, new Hasher(), NullLogger<AdminResetPasswordCommandHandler>.Instance);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(new AdminResetPasswordCommand(new AdminResetPasswordDto
        {
            EmployeeId = GuidFarah, AuthUserId = 7, Email = "farah.mannad@kyntus.com", NewPassword = Mdp,
        }), CancellationToken.None));
        Assert.Equal("inchangé", autre.PasswordHash);
    }

    [Fact]
    public async Task Compte_trouve_par_email_recoit_le_guid_employe()
    {
        var u = new User { Id = 3, Email = "farah.mannad@kyntus.com", Username = "farah.mannad@kyntus.com", SubjectId = Guid.NewGuid() };
        var repo = new Repo(u);
        var handler = new AdminResetPasswordCommandHandler(repo, new Hasher(), NullLogger<AdminResetPasswordCommandHandler>.Instance);

        await handler.Handle(new AdminResetPasswordCommand(new AdminResetPasswordDto
        {
            EmployeeId = GuidFarah, Email = "farah.mannad@kyntus.com", NewPassword = Mdp,
        }), CancellationToken.None);

        Assert.Equal(GuidFarah, u.SubjectId);
        Assert.Equal("hash:" + Mdp, u.PasswordHash);
    }

    private sealed class Hasher : IPasswordHasher
    {
        public string HashPassword(string password) => "hash:" + password;
        public bool VerifyPassword(string hashedPassword, string providedPassword) => hashedPassword == "hash:" + providedPassword;
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
