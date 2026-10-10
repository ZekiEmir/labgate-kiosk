# Tasarım Uygulama Projesi — RFID kiosk, ilk aşama

Windows 11 için çalıştırılabilir ASP.NET Core MVC + SQLite EF Core projesi. Bu makinede bulunan .NET 10 SDK 10.0.401 ve EF Core 10.0.12 kullanıldı. Mevcut projenizin kaynakları bu çalışma alanında bulunmadığı için bağımsız teslim edildi. .NET 8 projesine doğrudan dosya kopyalarken paket/SDK sürümlerini karıştırmayın; en kolay yol bu projeyi ayrı klasörde çalıştırmaktır.

## Kurulum

1. .NET 10 SDK kurun: https://dotnet.microsoft.com/download/dotnet/10.0 . Editör zorunlu değil; Visual Studio veya VS Code kullanılabilir.
2. ZIP'i açın, PowerShell'de `Kiosk` klasörüne geçin.
3. Aşağıdaki komutlarla paketleri indirin ve veritabanını hazırlayın:

```powershell
dotnet restore .\Kiosk.Web\Kiosk.Web.csproj
dotnet build .\Kiosk.Web\Kiosk.Web.csproj --no-restore
dotnet run --project .\Kiosk.Web -- --init-db --seed-demo
```

`--init-db` teslim edilen EF migration'ını uygular; `EnsureCreated` kullanılmaz. `--seed-demo` yalnızca Users tablosu boşsa iki örnek kullanıcı ekler. Demo verisini gerçek kurulumda eklemeyin. Veritabanı dosyası çalışma dizinine göre oluşur; aynı dizinden komut verin veya aşağıdaki gibi mutlak yol kullanın:

```powershell
$env:ConnectionStrings__Kiosk = "Data Source=$PWD\kiosk.db"
dotnet run --project .\Kiosk.Web -- --init-db --seed-demo
```

4. Yerel simülasyonu başlatın:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Kiosk__SimulationEnabled = 'true'
# Kendi rastgele cihaz anahtarınızı seçin; gerçek anahtarı kaynak koduna yazmayın.
$env:Kiosk__DeviceApiKey = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet run --project .\Kiosk.Web --urls http://127.0.0.1:5080
```

Tarayıcıda http://127.0.0.1:5080 açın. Sanal okuyucuda UID girip yüz eşleşmesi kutusunu değiştirin. Son kayıtları görmek için “Kayıtları yenile” düğmesini kullanın. Sunucuyu Ctrl+C ile durdurun.

## Migration komutları

Başlangıç migration'ı pakette var; tekrar `InitialCreate` üretmeyin. CLI alternatifi:

```powershell
dotnet tool install dotnet-ef --version 10.0.12 --tool-path .\.tools
.\.tools\dotnet-ef.exe database update --project .\Kiosk.Web
# Daha sonra model değişirse:
.\.tools\dotnet-ef.exe migrations add YeniDegisiklik --project .\Kiosk.Web
.\.tools\dotnet-ef.exe database update --project .\Kiosk.Web
```

Başka bir veritabanına geçerken `ConnectionStrings__Kiosk` değerini hem migration hem sunucu için aynı tutun. Migration öncesi mevcut DB'yi yedekleyin. Resmî açıklama: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying

## PDF ile eşleştirme

- ESP32 + RC522 UID'yi `POST /api/rfid/read` ile JSON gönderir.
- `Users`: Id, OgrenciNo, AdSoyad, Kart_UID, Face_Encoding (nullable BLOB), Yetki_Durumu.
- `AccessLogs`: LogId, UserId, Giris_Zamani (UTC), Durum. Bilinmeyen kartı kaydetmek için UserId nullable; ayrıca Kart_UID, IsSimulation, AccessGranted eklenmiştir.
- Kart UID ve öğrenci numarası benzersizdir. UID büyük harfe çevrilir; 4, 7, 10 bayt hex kabul edilir. Aradaki boşluk, `:` ve `-` kaldırılır.
- Python/OpenCV/face_recognition bağlantısı bu aşamada uygulanmadı. `IFaceVerifier` gelecekte kamera ve Python servisi için bağlantı noktasıdır; mevcut `UnavailableFaceVerifier` her zaman false döner.
- PDF'deki %80 eşleşme eşiği taklit edilmez. Gerçek yüz servisi geliştirildiğinde yöntem, eşik ve kamera hata davranışı doğrulanmalıdır.

## Örnek veriler ve API

| UID | Öğrenci | Yetki | Gerçek erişim |
|---|---|---|---|
| A1B2C3D4 | 20260001 / Demo Öğrenci | Var | Reddedilir |
| 11223344 | 20260002 / Yetkisiz Öğrenci | Yok | Reddedilir |
| DEADBEEF | Kayıtlı değil | Yok | Reddedilir |

Sunucu çalışırken ikinci PowerShell penceresinde aynı anahtarı kullanın:

```powershell
$key = Read-Host 'Sunucuda kullandığınız cihaz anahtarı'
Invoke-RestMethod 'http://127.0.0.1:5080/api/rfid/read' -Method Post `
  -ContentType 'application/json' -Headers @{'X-Device-Key'=$key} `
  -Body '{"uid":"A1B2C3D4"}'
```

Yanıt JSON nesnesidir; yalın `True` değildir. Alanlar: `logId`, `accessGranted`, `isSimulation`, `simulatedAccessGranted`, `led`, `status`, `displayName`. ESP32 JSON'u ayrıştırmalı; HTTP 200 almayı erişim izni saymamalıdır.

**Donanım kararı:** Yeşil LED yalnızca başarılı HTTP yanıtında `accessGranted == true`, `isSimulation == false` ve `led == "green"` birlikte doğrulanırsa yakılmalıdır. Başlangıç, zaman aşımı, bağlantı hatası, geçersiz JSON, HTTP 4xx/5xx ve eksik alanlarda kırmızı LED / kapalı kapı kullanılmalıdır. Bu teslimatta donanım API'si erişim vermez.

**Simülasyon kararı:** Yalnızca yerel loopback istemcisi + Development + SimulationEnabled ile MVC formu çalışır. Anti-forgery token zorunludur. Sanal yeşil ekran `simulatedAccessGranted` alanına dayanır; gerçek `accessGranted` false ve `led` red kalır. Donanım API'sine `simulation` veya `faceMatch` alanı göndermek sonucu değiştirmez. Simülasyon girişleri loglarda açıkça ayrılır.

Cihaz anahtarı yoksa 503, yanlışsa 401; geçersiz UID/JSON için 400 döner. Bu istekler AccessLogs'a yazılmaz; doğrulanmış ve geçerli kart okumaları hem başarı hem ret için yazılır. Kayıt yazılamazsa izin döndürülmez. Kiosk geçmişi yalnızca yerelden görüntülenir; yüz verisi API yanıtına eklenmez.

Gerçek donanım ağına açmadan önce TLS ve cihaz anahtarı aktarımını yapılandırın. Mevcut uygulama yerel HTTP simülasyonu içindir. LAN adresini AllowedHosts'a eklemek ve sunucuyu uygun arayüze bağlamak ayrı donanım kurulum aşamasıdır. Windows'ta MVC anti-forgery anahtarları App_Data/keys altında DPAPI ile korunur; bu klasörü paylaşmayın.

## Testler

SQLite bellekte gerçek migration ve sorgularla çalışan, ek test paketi gerektirmeyen doğrulama uygulaması:

```powershell
dotnet run --project .\Kiosk.Tests
```

HTTP testleri PowerShell 7 gerektirir. Sunucu yukarıdaki Development/simülasyon ayarlarıyla çalışırken:

```powershell
.\scripts\Test-Http.ps1 -DeviceKey $key
```

HTTP testi log kayıtları ekler. Ayrı bir test DB'si kullanmanız önerilir. Kapsam: cihaz anahtarı, geçersiz JSON/UID, bilinmeyen/yetkisiz kart, donanımda simülasyon bayrakları, MVC anti-forgery ve sanal başarı. Production veya SimulationEnabled=false iken sanal form gösterilmez ve Simulate uç noktası 403 verir.

## Dosyalar

`Models/Entities.cs`: modeller. `Data/KioskDbContext.cs`: şema ve seed. `Migrations/`: başlangıç migration'ı ve model snapshot. `Services/AccessService.cs`: UID ve güvenlik kararı. `Controllers/`: cihaz API'si ve yerel MVC. `Views/Kiosk/Index.cshtml`, `wwwroot/`: kiosk ekranı. `Kiosk.Tests/`, `scripts/`: doğrulamalar.
