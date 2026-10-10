using System.ComponentModel.DataAnnotations;
using Kiosk.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kiosk.Web.Controllers;
public sealed class RfidRequest
{
    [Required, StringLength(60)] public string Uid { get; set; } = "";
}
[ApiController, Route("api/rfid")]
public sealed class RfidController(AccessService access, IConfiguration config) : ControllerBase
{
    [HttpPost("read")]
    public async Task<IActionResult> Read(RfidRequest request, CancellationToken ct)
    {
        var key = config["Kiosk:DeviceApiKey"];
        if (string.IsNullOrWhiteSpace(key)) return StatusCode(503, new { accessGranted = false, led = "red", status = "Cihaz anahtarı yapılandırılmadı" });
        var supplied = Request.Headers["X-Device-Key"].ToString();
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(key), System.Text.Encoding.UTF8.GetBytes(supplied))) return Unauthorized(new { accessGranted = false, led = "red" });
        var uid = AccessService.NormalizeUid(request.Uid);
        if (uid is null) return BadRequest(new { accessGranted = false, led = "red", status = "UID 4, 7 veya 10 bayt hex olmalı" });
        // Client-supplied simulation/face flags never affect the hardware route.
        return Ok(await access.ReadAsync(uid, false, false, ct));
    }
}
