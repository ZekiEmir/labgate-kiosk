using System.Net;
using System.Security.Cryptography;
using System.Text;
using Kiosk.Web.Data;
using Kiosk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Web.Controllers;
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FaceController(KioskDbContext db, PythonFaceService face, AccessService access,
    IConfiguration config, IWebHostEnvironment env) : Controller
{
    private bool IsLocal => HttpContext.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
    private bool CanTest => IsLocal && env.IsDevelopment() && config.GetValue<bool>("Kiosk:SimulationEnabled");

    public async Task<IActionResult> Index()
    {
        if (!IsLocal) return StatusCode(403);
        ViewBag.FaceEnabled = config.GetValue<bool>("Face:Enabled");
        ViewBag.Tolerance = config.GetValue<double>("Face:Tolerance", 0.5);
        return View(await db.Users.AsNoTracking().OrderBy(x => x.Id).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Snapshot(CancellationToken ct) => Perform(async () => {
        if (!CanTest) return StatusCode(403);
        var reply = await face.CaptureAsync("snapshot", null, ct);
        return Json(new { image = reply.Image, status = "Anlık kamera görüntüsü; fotoğraf kaydedilmedi" });
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Enroll(string uid, string adminKey, bool consent, bool replace, CancellationToken ct) => Perform(async () => {
        if (!CanTest) return StatusCode(403);
        var expected = config["Kiosk:EnrollmentKey"];
        if (string.IsNullOrWhiteSpace(expected)) return StatusCode(503, new { status = "Yüz kayıt anahtarı yapılandırılmadı" });
        if (string.IsNullOrEmpty(adminKey) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(adminKey)))
            return Unauthorized(new { status = "Yüz kayıt anahtarı yanlış" });
        if (!consent) return BadRequest(new { status = "Yüz kaydı onayı gerekli" });
        var normalized = AccessService.NormalizeUid(uid);
        if (normalized is null) return BadRequest(new { status = "Geçersiz UID" });
        var user = await db.Users.SingleOrDefaultAsync(x => x.Kart_UID == normalized, ct);
        if (user is null) return NotFound(new { status = "Kart kayıtlı değil" });
        if (user.Face_Encoding is { Length: > 0 } && !replace) return Conflict(new { status = "Yüz kaydı mevcut; değiştirme kutusunu işaretleyin" });
        var reply = await face.CaptureAsync("enroll", null, ct);
        user.Face_Encoding = face.ProtectEncoding(reply.Encoding!);
        await db.SaveChangesAsync(ct);
        return Json(new { status = $"{user.AdSoyad}: referans yüz kaydı oluşturuldu" });
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Verify(string uid, CancellationToken ct) => Perform(async () => {
        if (!CanTest) return StatusCode(403);
        var normalized = AccessService.NormalizeUid(uid);
        if (normalized is null) return BadRequest(new { status = "Geçersiz UID" });
        return Json(await access.ReadAsync(normalized, true, false, ct, cameraTest: true));
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Delete(string uid, string adminKey, CancellationToken ct) => Perform(async () => {
        if (!CanTest) return StatusCode(403);
        var expected = config["Kiosk:EnrollmentKey"];
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrEmpty(adminKey)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(adminKey)))
            return Unauthorized(new { status = "Yüz kayıt anahtarı yanlış veya eksik" });
        var normalized = AccessService.NormalizeUid(uid);
        if (normalized is null) return BadRequest(new { status = "Geçersiz UID" });
        var user = await db.Users.SingleOrDefaultAsync(x => x.Kart_UID == normalized, ct);
        if (user is null) return NotFound(new { status = "Kart kayıtlı değil" });
        user.Face_Encoding = null;
        await db.SaveChangesAsync(ct);
        return Json(new { status = "Referans yüz kaydı silindi" });
    });

    private static async Task<IActionResult> Perform(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ObjectResult(new { accessGranted = false, status = e is IOException or InvalidOperationException ? e.Message : "Python başlatılamadı; Python yolunu kontrol edin" }) { StatusCode = 503 };
        }
    }
}
