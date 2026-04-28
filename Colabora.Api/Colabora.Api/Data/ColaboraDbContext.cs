using Colabora.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Colabora.Api.Data
{
    public class ColaboraDbContext : DbContext
    {
        public ColaboraDbContext(DbContextOptions<ColaboraDbContext> options) : base(options) { }

        // ========= DbSet =========

        public DbSet<User> Users => Set<User>();

        // ✅ AuditLogs activado de nuevo, sin navegación a User para evitar columnas raras
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        public DbSet<UserSession> UserSessions => Set<UserSession>();
        public DbSet<LoginLockout> LoginLockouts => Set<LoginLockout>();

        public DbSet<Application> Applications => Set<Application>();
        public DbSet<ApplicationDocument> ApplicationDocuments => Set<ApplicationDocument>();
        public DbSet<ApplicationComment> ApplicationComments => Set<ApplicationComment>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            // ============================================
            // USERS  (AJUSTADO A TU TABLA REAL)
            // ============================================
            mb.Entity<User>(e =>
            {
                e.ToTable("Users");
                e.HasKey(x => x.Id);

                e.Property(x => x.Username)
                    .IsRequired()
                    .HasMaxLength(100);

                e.HasIndex(x => x.Username)
                    .IsUnique();

                e.Property(x => x.PasswordHash)
                    .IsRequired()
                    .HasMaxLength(400);

                e.Property(x => x.Role)
                    .IsRequired()
                    .HasMaxLength(50);

                e.Property(x => x.FirstName).HasMaxLength(200);
                e.Property(x => x.LastName).HasMaxLength(200);
                e.Property(x => x.Email).HasMaxLength(200);
                e.Property(x => x.Phone).HasMaxLength(50);
                e.Property(x => x.Dept).HasMaxLength(200);

                e.Property(x => x.IsActive).IsRequired();

                e.Property(x => x.CreatedAt).IsRequired();
                e.Property(x => x.LastLoginAt);
                e.Property(x => x.CreatedByUserId);
            });

            // ============================================
            // AUDIT LOGS  (MAPPED A [dbo].[AuditLogs])
            // ============================================
            mb.Entity<AuditLog>(e =>
            {
                e.ToTable("AuditLogs");          // misma tabla que mostraste en SQL
                e.HasKey(x => x.Id);

                e.Property(x => x.UserId);

                e.Property(x => x.Action)
                    .IsRequired()
                    .HasMaxLength(100);

                e.Property(x => x.Payload);      // texto / JSON, sin límite fijo

                e.Property(x => x.Ip)
                    .HasMaxLength(45);

                e.Property(x => x.UserAgent)
                    .HasMaxLength(256);

                e.Property(x => x.CreatedAt)
                    .IsRequired();

                e.HasIndex(x => x.UserId);
                e.HasIndex(x => x.CreatedAt);
            });

            // ============================================
            // USER SESSIONS
            // ============================================
            mb.Entity<UserSession>(e =>
            {
                e.ToTable("UserSessions");
                e.HasKey(x => x.Id);

                e.Property(x => x.IsActive).IsRequired();

                e.Property(x => x.CreatedAt).IsRequired();
                e.Property(x => x.LastSeenAt).IsRequired();
                e.Property(x => x.ExpiresAt).IsRequired();

                e.Property(x => x.ClientIp).HasMaxLength(45);
                e.Property(x => x.UserAgent).HasMaxLength(256);

                e.HasIndex(x => x.UserId);
                e.HasIndex(x => x.Jti).IsUnique();
                e.HasIndex(x => new { x.Jti, x.IsActive });
                e.HasIndex(x => new { x.UserId, x.IsActive });

                e.HasOne(us => us.User)
                    .WithMany()
                    .HasForeignKey(us => us.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ============================================
            // LOGIN LOCKOUTS
            // ============================================
            mb.Entity<LoginLockout>(e =>
            {
                e.ToTable("LoginLockouts");
                e.HasKey(x => x.Id);

                e.Property(x => x.Username).IsRequired().HasMaxLength(100);
                e.Property(x => x.Ip).IsRequired().HasMaxLength(45);

                e.HasIndex(x => new { x.Username, x.Ip }).IsUnique();
            });

            // ============================================
            // APPLICATIONS
            // ============================================
            mb.Entity<Application>(e =>
            {
                e.ToTable("Applications");
                e.HasKey(x => x.Id);

                e.Property(x => x.Status).HasMaxLength(30);

                // ⚠ Tu tabla NO tiene columna Folio → la ignoramos en el modelo
                e.Ignore(x => x.Folio);

                e.HasOne<User>()
                 .WithMany()
                 .HasForeignKey(x => x.CandidateUserId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => x.CandidateUserId);
                e.HasIndex(x => x.Status);
            });

            // ============================================
            // DOCUMENTOS
            // ============================================
            mb.Entity<ApplicationDocument>(e =>
            {
                e.ToTable("ApplicationDocuments");
                e.HasKey(x => x.Id);

                e.Property(x => x.Type).HasMaxLength(50);
                e.Property(x => x.FileName).HasMaxLength(260);
                e.Property(x => x.FilePath).HasMaxLength(260);
                e.Property(x => x.MimeType).HasMaxLength(120);
                e.Property(x => x.Status).HasMaxLength(20);
                e.Property(x => x.Notes).HasMaxLength(500);

                e.HasOne(d => d.Application)
                 .WithMany(a => a.Documents)
                 .HasForeignKey(d => d.ApplicationId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => x.ApplicationId);
                e.HasIndex(x => new { x.ApplicationId, x.Status });
            });

            // ============================================
            // COMMENTS
            // ============================================
            mb.Entity<ApplicationComment>(e =>
            {
                e.ToTable("ApplicationComments");
                e.HasKey(x => x.Id);

                e.Property(x => x.Text)
                 .IsRequired()
                 .HasMaxLength(1000);

                e.HasOne(c => c.Application)
                 .WithMany(a => a.Comments)
                 .HasForeignKey(c => c.ApplicationId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne<User>()
                 .WithMany()
                 .HasForeignKey(x => x.AuthorUserId)
                 .OnDelete(DeleteBehavior.SetNull);

                e.HasIndex(x => x.ApplicationId);
                e.HasIndex(x => x.CreatedAt);
            });

            base.OnModelCreating(mb);
        }
    }
}
