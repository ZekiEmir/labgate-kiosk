using Kiosk.Web.Data;
using Kiosk.Web.Models;
using Kiosk.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Hosting;
using Kiosk.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Net;

using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var db = new KioskDbContext(new DbContextOptionsBuilder<KioskDbContext>().UseSqlite(connection).Options);
await db.Database.MigrateAsync();
await db.SeedDemoAsync();
await db.SeedDemoAsync();
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); checks++; }
Check(await db.Users.CountAsync() == 2, "Migration and idempotent demo seed");
Check(AccessService.NormalizeUid("a1:b2:c3:d4") == "A1B2C3D4", "UID normalization");
Check(AccessService.NormalizeUid("zz112233") is null && AccessService.NormalizeUid("1234") is null, "Invalid UID rejection");
var service = new AccessService(db, new UnavailableFaceVerifier());
var hardware = await service.ReadAsync("A1B2C3D4", false, true, default);
Check(!hardware.AccessGranted && !hardware.SimulatedAccessGranted && hardware.Led == "red", "Hardware denies even with simulated face flag");
var success = await service.ReadAsync("A1B2C3D4", true, true, default);
Check(!success.AccessGranted && success.SimulatedAccessGranted && success.Led == "red" && success.IsSimulation, "Simulated success never grants hardware access");
var mismatch = await service.ReadAsync("A1B2C3D4", true, false, default);
Check(!mismatch.SimulatedAccessGranted, "Simulated face mismatch");
var unauthorized = await service.ReadAsync("11223344", true, true, default);
Check(!unauthorized.SimulatedAccessGranted, "Unauthorized card denied");
var unknown = await service.ReadAsync("DEADBEEF", false, false, default);
Check(!unknown.AccessGranted && (await db.AccessLogs.FindAsync(unknown.LogId))!.UserId is null, "Unknown card audited with nullable FK");
var user = await db.Users.SingleAsync(x => x.Kart_UID == "A1B2C3D4");
user.Face_Encoding = [1, 2, 3]; await db.SaveChangesAsync();
var unavailable = await service.ReadAsync(user.Kart_UID, false, false, default);
Check(!unavailable.AccessGranted, "Stored face data alone grants nothing");
var enabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Kiosk:HardwareAccessEnabled"] = "true" }).Build();
var failure = await new AccessService(db, new ThrowingVerifier(), enabled).ReadAsync(user.Kart_UID, false, false, default);
Check(!failure.AccessGranted && failure.Status.Contains("hatası"), "Face service error defaults to deny");
Check(await db.AccessLogs.CountAsync() == 7, "Every accepted read persisted");
var matched = new AccessService(db, new MatchingVerifier());
var safe = await matched.ReadAsync(user.Kart_UID, false, false, default);
Check(!safe.AccessGranted, "Matching verifier cannot override default hardware lock");
var camera = await matched.ReadAsync(user.Kart_UID, true, false, default, cameraTest: true);
Check(camera.SimulatedAccessGranted && !camera.AccessGranted && camera.Led == "red" && camera.IsSimulation, "Real camera test remains separate from hardware");
var missingReference = await matched.ReadAsync("DEADBEEF", true, false, default, cameraTest: true);
Check(!missingReference.SimulatedAccessGranted, "Unknown camera test denied");
Check(!PythonFaceService.IsValidEncoding([1,2,3]) && !PythonFaceService.IsValidEncoding(Enumerable.Repeat(double.NaN,128).ToArray()), "Malformed and nonfinite face vectors rejected");
Check(PythonFaceService.IsValidEncoding(new double[128]), "128 finite vector values accepted");
var testPython = Environment.GetEnvironmentVariable("KIOSK_TEST_PYTHON");
if (!string.IsNullOrWhiteSpace(testPython))
{
    var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Face:Enabled"]="true", ["Face:PythonExecutable"]=testPython,
        ["Face:ScriptPath"]=Path.Combine(AppContext.BaseDirectory,"fixture_worker.py"), ["Face:Tolerance"]="0.5",
        ["Kiosk:SimulationEnabled"]="true", ["Kiosk:EnrollmentKey"]="test-enrollment-key"
    }).Build();
    var gate = new CameraGate();
    var python = new PythonFaceService(settings, new TestEnvironment(), new EphemeralDataProtectionProvider(), gate);
    var capture = await python.CaptureAsync("enroll", null, default);
    Check(capture.Encoding?.Length == 128, "Python process enrollment protocol");
    user.Face_Encoding = python.ProtectEncoding(capture.Encoding!);
    Check(!user.Face_Encoding.SequenceEqual(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(capture.Encoding)), "Reference face protected at rest");
    Check(await python.VerifyAsync(user, default), "Protected reference roundtrip through Python protocol");
    user.Face_Encoding[0] ^= 1;
    try { await python.VerifyAsync(user, default); throw new Exception("Tampered vector accepted"); }
    catch (System.Security.Cryptography.CryptographicException) { checks++; Console.WriteLine("PASS: Tampered reference rejected"); }
    var invalidSettings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Face:Enabled"]="true", ["Face:PythonExecutable"]=testPython,
        ["Face:ScriptPath"]=Path.Combine(AppContext.BaseDirectory,"fixture_worker.py"), ["Face:Tolerance"]="0.9"
    }).Build();
    try { await new PythonFaceService(invalidSettings,new TestEnvironment(),new EphemeralDataProtectionProvider(),new CameraGate()).CaptureAsync("verify",new double[128], default); throw new Exception("Unsafe tolerance accepted"); }
    catch (InvalidOperationException) { checks++; Console.WriteLine("PASS: Unsafe tolerance rejected"); }
    await gate.Lock.WaitAsync();
    try { await python.CaptureAsync("snapshot",null,default); throw new Exception("Concurrent camera capture accepted"); }
    catch (IOException) { checks++; Console.WriteLine("PASS: Concurrent camera capture rejected"); }
    finally { gate.Lock.Release(); }
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Loopback;
    var controller = new FaceController(db,python,new AccessService(db,python),settings,new TestEnvironment()) {
        ControllerContext = new ControllerContext { HttpContext = context }
    };
    Check(await controller.Enroll(user.Kart_UID,"wrong",true,true,default) is UnauthorizedObjectResult, "Enrollment rejects wrong administrative key");
    Check(await controller.Enroll(user.Kart_UID,"test-enrollment-key",false,true,default) is BadRequestObjectResult, "Enrollment requires consent");
    Check(await controller.Enroll("DEADBEEF","test-enrollment-key",true,true,default) is NotFoundObjectResult, "Enrollment rejects unknown card");
    Check(await controller.Enroll(user.Kart_UID,"test-enrollment-key",true,false,default) is ConflictObjectResult, "Existing reference requires explicit replacement");
    Check(await controller.Enroll(user.Kart_UID,"test-enrollment-key",true,true,default) is JsonResult, "Enrollment protects and persists captured vector");
    Check(await python.VerifyAsync(user,default), "Persisted enrollment decrypts for verification");
    var testDecision = await controller.Verify(user.Kart_UID, default) as JsonResult;
    Check(testDecision?.Value is AccessDecision { IsSimulation:true, SimulatedAccessGranted:true, AccessGranted:false }, "Camera controller never grants hardware access");
    context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
    Check(await controller.Enroll(user.Kart_UID,"test-enrollment-key",true,true,default) is StatusCodeResult { StatusCode:403 }, "Remote enrollment forbidden");
    context.Connection.RemoteIpAddress = IPAddress.Loopback;
    Check(await controller.Delete(user.Kart_UID,"wrong",default) is UnauthorizedObjectResult, "Deleting reference requires key");
    Check(await controller.Delete(user.Kart_UID,"test-enrollment-key",default) is JsonResult && user.Face_Encoding is null, "Authorized deletion removes reference");
}
else Console.WriteLine("SKIP: Python protocol checks; set KIOSK_TEST_PYTHON to a Python executable.");
db.Users.Add(new User { OgrenciNo = "OTHER", AdSoyad = "Duplicate", Kart_UID = user.Kart_UID });
try { await db.SaveChangesAsync(); throw new Exception("Duplicate UID accepted"); }
catch (DbUpdateException) { checks++; Console.WriteLine("PASS: Unique UID database constraint"); }
Console.WriteLine($"{checks} checks passed.");
sealed class ThrowingVerifier : IFaceVerifier
{
    public Task<bool> VerifyAsync(User user, CancellationToken cancellationToken) => throw new IOException("Camera offline");
}
sealed class MatchingVerifier : IFaceVerifier
{
    public Task<bool> VerifyAsync(User user, CancellationToken cancellationToken) => Task.FromResult(true);
}
sealed class TestEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Kiosk.Tests";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
