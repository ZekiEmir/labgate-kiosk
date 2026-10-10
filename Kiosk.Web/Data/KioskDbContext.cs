using Kiosk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Web.Data;
public sealed class KioskDbContext(DbContextOptions<KioskDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(x => x.Kart_UID).IsUnique();
        b.Entity<User>().HasIndex(x => x.OgrenciNo).IsUnique();
        b.Entity<User>().Property(x => x.Kart_UID).HasMaxLength(20).IsRequired();
        b.Entity<User>().Property(x => x.AdSoyad).HasMaxLength(120).IsRequired();
        b.Entity<User>().Property(x => x.OgrenciNo).HasMaxLength(30).IsRequired();
        b.Entity<AccessLog>().HasKey(x => x.LogId);
        b.Entity<AccessLog>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AccessLog>().HasIndex(x => x.Giris_Zamani);
    }
    public async Task SeedDemoAsync()
    {
        if (await Users.AnyAsync()) return;
        Users.AddRange(
            new User { OgrenciNo = "20260001", AdSoyad = "Demo Öğrenci", Kart_UID = "A1B2C3D4", Yetki_Durumu = true },
            new User { OgrenciNo = "20260002", AdSoyad = "Yetkisiz Öğrenci", Kart_UID = "11223344", Yetki_Durumu = false });
        await SaveChangesAsync();
    }
}
