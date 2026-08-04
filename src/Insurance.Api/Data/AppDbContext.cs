using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Insurance.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(e =>
        {
            e.HasIndex(u => u.EmailHash).IsUnique();
            // Never store actual email — force these Identity columns to stay empty
            e.Property(u => u.Email).HasMaxLength(0).IsRequired(false);
            e.Property(u => u.NormalizedEmail).HasMaxLength(0).IsRequired(false);
        });
    }
}
