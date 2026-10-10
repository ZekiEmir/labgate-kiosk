using Kiosk.Web.Data;
using Kiosk.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Kiosk.Web.Controllers;
public sealed class KioskController(KioskDbContext db, AccessService access, IConfiguration config, IWebHostEnvironment env) : Controller
{
    private bool IsLocal => HttpContext.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
    public async Task<IActionResult> Index()
    {
        if (!IsLocal) return StatusCode(403);
        ViewBag.SimulationEnabled = env.IsDevelopment() && config.GetValue<bool>("Kiosk:SimulationEnabled");
        return View(await db.AccessLogs.AsNoTracking().Include(x => x.User).OrderByDescending(x => x.LogId).Take(20).ToListAsync());
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Simulate(string uid, bool faceMatch, CancellationToken ct)
    {
        if (!IsLocal || !env.IsDevelopment() || !config.GetValue<bool>("Kiosk:SimulationEnabled")) return StatusCode(403);
        var normalized = AccessService.NormalizeUid(uid);
        if (normalized is null) return BadRequest(new { status = "Geçersiz UID" });
        return Json(await access.ReadAsync(normalized, true, faceMatch, ct));
    }
}
