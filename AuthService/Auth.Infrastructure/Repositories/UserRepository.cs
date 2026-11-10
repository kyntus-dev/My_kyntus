using Auth.Domain.Entities;
using Auth.Domain.Interfaces;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AuthDbContext _context;

    public UserRepository(AuthDbContext context) => _context = context;

    public async Task<User?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id, ct);

    // v38 : espaces autour de l'e-mail ignorés des deux côtés (saisie, comptes créés avec un espace).
    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var cle = (email ?? string.Empty).Trim().ToLower();
        return await _context.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email.Trim().ToLower() == cle, ct);
    }

    public async Task<User?> GetBySubjectIdAsync(Guid subjectId, CancellationToken ct = default) =>
        await _context.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.SubjectId == subjectId, ct);

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        await _context.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower(), ct);

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) =>
        await _context.Users.Include(u => u.Role).ToListAsync(ct);

    public async Task<User> CreateAsync(User user, CancellationToken ct = default)
    {
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.Users.AddAsync(user, ct);
        await _context.SaveChangesAsync(ct);
        return user;
    }

    public async Task<User> UpdateAsync(User user, CancellationToken ct = default)
    {
        user.UpdatedAt = DateTime.UtcNow;
        _context.Users.Update(user);
        await _context.SaveChangesAsync(ct);
        return user;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var user = await _context.Users.FindAsync([id], ct);
        if (user == null)
            return false;

        _context.Users.Remove(user);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> ExistsAsync(string email, CancellationToken ct = default) =>
        _context.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower(), ct);

    public Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default) =>
        _context.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower(), ct);
}
