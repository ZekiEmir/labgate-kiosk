using Kiosk.Web.Data;
using Kiosk.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
var keys = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));
if (OperatingSystem.IsWindows()) keys.ProtectKeysWithDpapi();
builder.Services.AddDbContext<KioskDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Kiosk")));
builder.Services.AddScoped<AccessService>();
builder.Services.AddSingleton<CameraGate>();
builder.Services.AddScoped<PythonFaceService>();
builder.Services.AddScoped<IFaceVerifier>(sp => sp.GetRequiredService<PythonFaceService>());
var app = builder.Build();
if (args.Contains("--init-db"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<KioskDbContext>();
    await db.Database.MigrateAsync();
    if (args.Contains("--seed-demo")) await db.SeedDemoAsync();
    return;
}
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    context.Response.StatusCode = 500;
    await context.Response.WriteAsJsonAsync(new { accessGranted = false, led = "red", status = "İşlem tamamlanamadı; erişim reddedildi" });
}));
app.UseStaticFiles();
app.MapControllerRoute("default", "{controller=Kiosk}/{action=Index}/{id?}");
app.Run();
