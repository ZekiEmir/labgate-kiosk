using System.Diagnostics;
using System.Text.Json;
using Kiosk.Web.Models;
using Microsoft.AspNetCore.DataProtection;

namespace Kiosk.Web.Services;

public sealed record FaceReply(bool Ok, string? Error, double[]? Encoding, double? Distance, bool Match, string? Image);
public sealed class CameraGate { public SemaphoreSlim Lock { get; } = new(1, 1); }

public sealed class PythonFaceService(IConfiguration config, IWebHostEnvironment env,
    IDataProtectionProvider protection, CameraGate gate) : IFaceVerifier
{
    private readonly IDataProtector protector = protection.CreateProtector("Kiosk.FaceEncoding.v1");
    public static bool IsValidEncoding(double[]? values) => values is { Length: 128 }
        && values.All(x => double.IsFinite(x) && Math.Abs(x) <= 10);

    public byte[] ProtectEncoding(double[] values)
    {
        if (!IsValidEncoding(values)) throw new InvalidDataException("Geçersiz yüz vektörü");
        return protector.Protect(JsonSerializer.SerializeToUtf8Bytes(values));
    }

    private double[] UnprotectEncoding(byte[] values)
    {
        var decoded = JsonSerializer.Deserialize<double[]>(protector.Unprotect(values));
        return IsValidEncoding(decoded) ? decoded! : throw new InvalidDataException("Geçersiz yüz kaydı");
    }

    public async Task<FaceReply> CaptureAsync(string operation, double[]? encoding, CancellationToken ct)
    {
        if (!config.GetValue<bool>("Face:Enabled")) throw new InvalidOperationException("Yüz servisi etkin değil");
        if (operation is not ("snapshot" or "enroll" or "verify")) throw new ArgumentException("Geçersiz işlem");
        var tolerance = config.GetValue<double>("Face:Tolerance", 0.5);
        if (!double.IsFinite(tolerance) || tolerance <= 0 || tolerance > 0.6) throw new InvalidOperationException("Yüz eşiği 0 ile 0.6 arasında olmalı");
        if (!await gate.Lock.WaitAsync(0, ct)) throw new IOException("Kamera başka bir işlemde kullanılıyor");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process();
        try
        {
            var python = config["Face:PythonExecutable"] ?? "python";
            var script = Path.GetFullPath(Path.Combine(env.ContentRootPath, config["Face:ScriptPath"] ?? "../FaceService/face_worker.py"));
            process.StartInfo = new ProcessStartInfo(python) {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            process.StartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            process.StartInfo.ArgumentList.Add(script);
            process.StartInfo.ArgumentList.Add(operation);
            if (!process.Start()) throw new IOException("Python başlatılamadı");
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
                cameraIndex = config.GetValue<int>("Face:CameraIndex", 0), tolerance, encoding
            }).AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            var output = await stdout;
            await stderr; // Dependency stderr can contain local details; never expose it through MVC.
            if (process.ExitCode != 0 || output.Length > 2_000_000) throw new IOException("Python yüz servisi çalışamadı; kurulum kontrolünü çalıştırın");
            var reply = JsonSerializer.Deserialize<FaceReply>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new IOException("Yüz servisi geçersiz yanıt verdi");
            if (!reply.Ok) throw new IOException(ErrorMessage(reply.Error));
            if (operation == "enroll" && !IsValidEncoding(reply.Encoding)) throw new IOException("Geçersiz yüz vektörü");
            if (operation == "verify" && (reply.Distance is not { } d || !double.IsFinite(d) || d < 0 || reply.Match != (d <= tolerance)))
                throw new IOException("Geçersiz eşleşme sonucu");
            if (operation == "snapshot" && (reply.Image is null || reply.Image.Length > 1_500_000)) throw new IOException("Geçersiz kamera görüntüsü");
            return reply;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException("Kamera/yüz servisi zaman aşımı; erişim reddedildi");
        }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            catch (InvalidOperationException) { }
            finally { gate.Lock.Release(); }
        }
    }

    private static string ErrorMessage(string? code) => code switch {
        "no_face" => "Kamerada yüz bulunamadı", "multiple_faces" => "Kamerada yalnızca bir kişi olmalı",
        "camera_unavailable" => "Kamera açılamadı; kamera iznini ve diğer uygulamaları kontrol edin",
        "capture_failed" => "Kameradan görüntü alınamadı", "dependencies_missing" => "Python bağımlılıkları eksik; kurulum kontrolünü çalıştırın",
        _ => "Yüz servisi işlemi tamamlayamadı"
    };

    public async Task<bool> VerifyAsync(User user, CancellationToken cancellationToken)
    {
        if (user.Face_Encoding is not { Length: > 0 }) return false;
        var reference = UnprotectEncoding(user.Face_Encoding);
        var reply = await CaptureAsync("verify", reference, cancellationToken);
        return reply.Match;
    }
}
