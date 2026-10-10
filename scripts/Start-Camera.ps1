param([Parameter(Mandatory=$true)][string]$PythonPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
if (-not (Test-Path -LiteralPath $PythonPath -PathType Leaf)) { throw 'Python yolu bulunamadi.' }
$env:Face__PythonExecutable = (Resolve-Path -LiteralPath $PythonPath).Path
$pythonDirectory = Split-Path $env:Face__PythonExecutable -Parent
if (Test-Path -LiteralPath (Join-Path $pythonDirectory 'conda-meta')) {
    # Conda native libraries need their environment directories when launched outside its prompt.
    $env:PATH = "$pythonDirectory;$pythonDirectory\Library\bin;$pythonDirectory\Scripts;$env:PATH"
    $env:CONDA_PREFIX = $pythonDirectory
    $env:DLL_SEARCH_MODIFICATION_ENABLE = '1'
}
$env:PYTHONNOUSERSITE = '1'
& $env:Face__PythonExecutable .\FaceService\check_install.py
if ($LASTEXITCODE -ne 0) { throw 'Python kurulumu tamamlanmadi. KAMERA-KURULUM.md dosyasini okuyun.' }
$env:ConnectionStrings__Kiosk = "Data Source=$projectRoot\kiosk.db"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Kiosk__SimulationEnabled = 'true'
$env:Kiosk__HardwareAccessEnabled = 'false'
$env:Face__Enabled = 'true'
$bytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes)
$rng.Dispose()
$env:Kiosk__DeviceApiKey = [BitConverter]::ToString($bytes).Replace('-', '')
$secret = Read-Host 'Yuz kayit anahtari belirleyin (en az 11 karakter; kayit formunda kullanacaksiniz)' -AsSecureString
$env:Kiosk__EnrollmentKey = (New-Object System.Net.NetworkCredential('', $secret)).Password
if ($env:Kiosk__EnrollmentKey.Length -lt 11) { throw 'En az 11 karakter kullanin.' }
try {
    dotnet build .\Kiosk.Web\Kiosk.Web.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Derleme basarisiz.' }
    dotnet run --project .\Kiosk.Web --no-build --no-restore -- --urls http://127.0.0.1:5080
} finally {
    Remove-Item Env:Kiosk__EnrollmentKey -ErrorAction SilentlyContinue
    Remove-Item Env:Kiosk__DeviceApiKey -ErrorAction SilentlyContinue
}
