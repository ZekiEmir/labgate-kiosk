param([string]$BaseUrl = 'http://127.0.0.1:5080', [Parameter(Mandatory=$true)][string]$DeviceKey)
$ErrorActionPreference = 'Stop'
function Assert($Condition, $Name) { if (-not $Condition) { throw $Name }; Write-Host "PASS: $Name" }
function Read-Card($Body, $Key = $DeviceKey) {
    Invoke-WebRequest "$BaseUrl/api/rfid/read" -Method Post -ContentType 'application/json' -Headers @{'X-Device-Key'=$Key} -Body $Body -SkipHttpErrorCheck
}
$r = Read-Card '{"uid":"a1:b2:c3:d4","simulation":true,"faceMatch":true}'
$d = $r.Content | ConvertFrom-Json
Assert ($r.StatusCode -eq 200 -and !$d.accessGranted -and !$d.isSimulation -and $d.led -eq 'red') 'Hardware ignores simulation flags'
Assert ((Read-Card '{"uid":"A1B2C3D4"}' 'wrong').StatusCode -eq 401) 'Wrong device key rejected'
Assert ((Read-Card '{"uid":"ZZZZ"}').StatusCode -eq 400) 'Malformed UID rejected'
Assert ((Read-Card '{}').StatusCode -eq 400) 'Missing UID rejected'
Assert ((Read-Card 'invalid-json').StatusCode -eq 400) 'Malformed JSON rejected'
foreach ($uid in @('11223344','DEADBEEF')) {
    $d = (Read-Card ('{"uid":"' + $uid + '"}')).Content | ConvertFrom-Json
    Assert (!$d.accessGranted) "Denied card $uid"
}
$page = Invoke-WebRequest $BaseUrl -SessionVariable kioskSession
$token = [regex]::Match($page.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
Assert ($token.Length -gt 0) 'Simulation form and anti-forgery token rendered'
$sim = Invoke-RestMethod "$BaseUrl/Kiosk/Simulate" -Method Post -WebSession $kioskSession -Body @{uid='A1B2C3D4';faceMatch='true';__RequestVerificationToken=$token}
Assert ($sim.isSimulation -and $sim.simulatedAccessGranted -and !$sim.accessGranted -and $sim.led -eq 'red') 'Local simulation succeeds without hardware grant'
$blocked = Invoke-WebRequest "$BaseUrl/Kiosk/Simulate" -Method Post -Body @{uid='A1B2C3D4';faceMatch='true'} -SkipHttpErrorCheck
Assert ($blocked.StatusCode -eq 400) 'Simulation rejects missing anti-forgery token'
