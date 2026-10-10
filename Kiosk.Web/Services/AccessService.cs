using System.Text.RegularExpressions;
using Kiosk.Web.Data;
using Kiosk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Web.Services;
public interface IFaceVerifier
{
    Task<bool> VerifyAsync(User user, CancellationToken cancellationToken);
}
// Camera/Python integration is intentionally pending. No confidence score is fabricated.
public sealed class UnavailableFaceVerifier : IFaceVerifier
{
    public Task<bool> VerifyAsync(User user, CancellationToken cancellationToken) => Task.FromResult(false);
}
public sealed record AccessDecision(int LogId, bool AccessGranted, bool IsSimulation,
    bool SimulatedAccessGranted, string Led, string Status, string? DisplayName);

public sealed class AccessService(KioskDbContext db, IFaceVerifier face, IConfiguration? config = null)
{
    public static string? NormalizeUid(string? value)
    {
        if (value is null || value.Length > 60 || !Regex.IsMatch(value, @"\A[0-9a-fA-F :\-]+\z")) return null;
        var uid = value.Replace(" ", "").Replace(":", "").Replace("-", "").ToUpperInvariant();
        return uid.Length is 8 or 14 or 20 ? uid : null;
    }
    public async Task<AccessDecision> ReadAsync(string uid, bool simulation, bool simulatedFaceMatch, CancellationToken ct, bool cameraTest = false)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Kart_UID == uid, ct);
        var status = user is null ? "Kart kayıtlı değil" : !user.Yetki_Durumu ? "Giriş yetkisi yok" : "Yüz kaydı/doğrulaması hazır değil: erişim reddedildi";
        var granted = false;
        var simulatedGranted = false;
        if (user is { Yetki_Durumu: true })
        {
            if (simulation && !cameraTest)
            {
                simulatedGranted = simulatedFaceMatch;
                status = simulatedFaceMatch ? "SİMÜLASYON: başarılı" : "SİMÜLASYON: yüz uyuşmazlığı";
            }
            else
            {
                try
                {
                    if (user.Face_Encoding is { Length: > 0 })
                    {
                        // Hardware remains off until an explicit deployment configuration enables it.
                        if (cameraTest || config?.GetValue<bool>("Kiosk:HardwareAccessEnabled") == true)
                        {
                            var match = await face.VerifyAsync(user, ct);
                            simulatedGranted = simulation && match;
                            granted = !simulation && match;
                            status = simulation ? (match ? "KAMERA TESTİ: yüz eşleşti" : "KAMERA TESTİ: yüz uyuşmazlığı")
                                : match ? "Başarılı" : "Yüz uyuşmazlığı nedeniyle reddedildi";
                        }
                        else status = "Donanım erişimi kapalı: erişim reddedildi";
                    }
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    status = "Yüz servisi hatası: erişim reddedildi";
                }
            }
        }
        var log = new AccessLog { UserId = user?.Id, Kart_UID = uid, Giris_Zamani = DateTime.UtcNow,
            Durum = status, IsSimulation = simulation, AccessGranted = granted };
        db.AccessLogs.Add(log);
        await db.SaveChangesAsync(ct); // Return a decision only after audit persistence succeeds.
        return new(log.LogId, granted, simulation, simulatedGranted, granted ? "green" : "red", status, user?.AdSoyad);
    }
}
