<#
.SYNOPSIS
    Drives every Smart-X gateway endpoint and reports pass/fail for each scenario.

.DESCRIPTION
    Boots the API (unless -BaseUrl points at an already running instance), registers sensors,
    exercises the validator rejection paths, pushes live and batched telemetry, uploads an
    attachment, then seeds a bulk load to show the jagged-array pipeline holding up under volume.

.EXAMPLE
    ./scripts/verify-api.ps1
    ./scripts/verify-api.ps1 -KeepRunning      # leave the API up afterwards to browse Swagger
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5199',
    [switch]$KeepRunning,
    [switch]$SkipLoadTest
)

$ErrorActionPreference = 'Stop'
$script:Passed = 0
$script:Failed = 0
$script:ApiProcess = $null

function Write-Section($text) {
    Write-Host ''
    Write-Host "--- $text " -ForegroundColor Cyan
}

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        $Body
    )

    $uri = "$BaseUrl$Path"
    $params = @{ Uri = $uri; Method = $Method; UseBasicParsing = $true; ErrorAction = 'Stop' }

    if ($null -ne $Body) {
        # -InputObject rather than the pipeline: piping an empty array sends zero objects and
        # would serialise to nothing at all, instead of the empty JSON array an empty polling
        # cycle needs to send.
        $params.Body = ConvertTo-Json -InputObject $Body -Depth 5 -Compress
        $params.ContentType = 'application/json'
    }

    try {
        $response = Invoke-WebRequest @params
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Content = $response.Content }
    }
    catch {
        $raw = $_.Exception.Response
        if ($null -eq $raw) { throw }

        $content = $_.ErrorDetails.Message
        if (-not $content -and $raw.GetResponseStream) {
            $reader = New-Object System.IO.StreamReader($raw.GetResponseStream())
            $content = $reader.ReadToEnd()
        }

        return [pscustomobject]@{ Status = [int]$raw.StatusCode; Content = $content }
    }
}

function Assert-That {
    param([string]$Name, [bool]$Condition, [string]$Detail)

    if ($Condition) {
        $script:Passed++
        Write-Host "  PASS  $Name" -ForegroundColor Green
    }
    else {
        $script:Failed++
        Write-Host "  FAIL  $Name" -ForegroundColor Red
        if ($Detail) { Write-Host "        $Detail" -ForegroundColor DarkGray }
    }
}

function Assert-Status {
    param([string]$Name, $Response, [int]$Expected)

    Assert-That -Name "$Name (expected HTTP $Expected)" `
        -Condition ($Response.Status -eq $Expected) `
        -Detail "got HTTP $($Response.Status): $($Response.Content)"
}

function Start-Api {
    $repoRoot = Split-Path $PSScriptRoot -Parent
    $project = Join-Path $repoRoot 'SmartX.Api/SmartX.Api.csproj'

    Write-Host "Starting API on $BaseUrl ..." -ForegroundColor Yellow
    $script:ApiProcess = Start-Process -FilePath 'dotnet' `
        -ArgumentList "run --project `"$project`" --urls $BaseUrl" `
        -PassThru -NoNewWindow `
        -RedirectStandardOutput (Join-Path $env:TEMP 'smartx-verify.log') `
        -RedirectStandardError (Join-Path $env:TEMP 'smartx-verify.err')

    for ($attempt = 1; $attempt -le 40; $attempt++) {
        Start-Sleep -Seconds 2
        try {
            Invoke-WebRequest -Uri "$BaseUrl/api/sensors" -UseBasicParsing -TimeoutSec 3 | Out-Null
            Write-Host "API is up." -ForegroundColor Yellow
            return
        }
        catch {
            # Still building or binding; keep waiting.
        }
    }

    throw "API did not become reachable at $BaseUrl."
}

function Stop-Api {
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
        Where-Object { $_.CommandLine -like '*SmartX.Api*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
}

# --- boot -------------------------------------------------------------------------------------

$alreadyRunning = $false
try {
    Invoke-WebRequest -Uri "$BaseUrl/api/sensors" -UseBasicParsing -TimeoutSec 3 | Out-Null
    $alreadyRunning = $true
    Write-Host "Using the API already running at $BaseUrl." -ForegroundColor Yellow
}
catch {
    Start-Api
}

$environmentalId = 'A4:CF:12:01:AA:01'
$powerId = 'A4:CF:12:01:BB:02'
$actuatorId = 'A4:CF:12:01:CC:03'

try {
    # --- registration -------------------------------------------------------------------------

    Write-Section 'Sensor registration'

    $register = Invoke-Api POST '/api/sensors' @{
        macAddress = $environmentalId; facilityName = 'Facility A'
        zoneName = 'Zone 1'; subZoneName = 'Sub-Zone B'; category = 'Environmental'
    }
    Assert-Status 'Register environmental sensor' $register 201

    $lookup = Invoke-Api GET "/api/sensors/$environmentalId"
    Assert-Status 'Look the sensor up by id' $lookup 200

    $duplicate = Invoke-Api POST '/api/sensors' @{
        macAddress = $environmentalId; facilityName = 'Facility A'
        zoneName = 'Zone 1'; category = 'Environmental'
    }
    Assert-Status 'Reject a duplicate MAC address' $duplicate 400

    $power = Invoke-Api POST '/api/sensors' @{
        macAddress = $powerId; facilityName = 'Facility A'
        zoneName = 'Zone 2'; category = 'PowerConsumption'
    }
    Assert-Status 'Register power sensor' $power 201

    $actuator = Invoke-Api POST '/api/sensors' @{
        macAddress = $actuatorId; facilityName = 'Facility A'
        zoneName = 'Zone 2'; category = 'Actuator'
    }
    Assert-Status 'Register actuator sensor' $actuator 201

    # --- recursive validator ------------------------------------------------------------------

    Write-Section 'Recursive deployment validation'

    $blank = Invoke-Api POST '/api/sensors' @{
        macAddress = 'A4:CF:12:01:DD:04'; facilityName = '   '
        zoneName = 'Zone 9'; category = 'Environmental'
    }
    Assert-Status 'Reject a blank facility name' $blank 400
    Assert-That -Name 'Blank-name violation names the offending path' `
        -Condition ($blank.Content -like '*blank name*') -Detail $blank.Content

    $collision = Invoke-Api POST '/api/sensors' @{
        macAddress = 'Sub-Zone B'; facilityName = 'Facility A'
        zoneName = 'Zone 1'; category = 'Environmental'
    }
    Assert-Status 'Reject a MAC colliding with a sibling sub-zone' $collision 400
    Assert-That -Name 'Collision violation reports the duplicate siblings' `
        -Condition ($collision.Content -like '*sibling nodes named*') -Detail $collision.Content

    $tree = Invoke-Api GET '/api/deployment-tree'
    Assert-Status 'Fetch the deployment tree' $tree 200
    Assert-That -Name 'Rejected registrations left no orphan branch' `
        -Condition (($tree.Content -notlike '*Zone 9*') -and ($tree.Content -notlike '*"Sub-Zone B","nodeType":"Sensor"*')) `
        -Detail $tree.Content

    # --- live telemetry -----------------------------------------------------------------------

    Write-Section 'Live telemetry across all three type parameters'

    $envReading = Invoke-Api POST '/api/telemetry/environmental' @{ sensorId = $environmentalId; value = 22.4 }
    Assert-Status 'Accept a float soil-moisture reading' $envReading 200

    $powerReading = Invoke-Api POST '/api/telemetry/power' @{ sensorId = $powerId; value = 1450 }
    Assert-Status 'Accept an int wattage reading' $powerReading 200

    $valveReading = Invoke-Api POST '/api/telemetry/actuator' @{ sensorId = $actuatorId; value = $true }
    Assert-Status 'Accept a bool valve state' $valveReading 200

    $mismatch = Invoke-Api POST '/api/telemetry/environmental' @{ sensorId = $powerId; value = 9.9 }
    Assert-Status 'Reject a reading sent to the wrong category' $mismatch 400

    $ghost = Invoke-Api POST '/api/telemetry/environmental' @{ sensorId = 'NOT-REGISTERED'; value = 1.0 }
    Assert-Status 'Reject a reading from an unregistered sensor' $ghost 404

    # --- jagged batches -----------------------------------------------------------------------

    Write-Section 'Jagged-array batching and flattening'

    # The dropped cycle sits between the two populated ones, so the gap between the surviving
    # timestamps proves the empty cycle still consumed a slot on the time axis.
    Assert-Status 'Accept a 3-reading polling cycle' (Invoke-Api POST "/api/telemetry/power/$powerId/batches" @(120, 135, 128)) 200

    $dropped = Invoke-Api POST "/api/telemetry/power/$powerId/batches" @()
    Assert-Status 'Accept a dropped (empty) polling cycle' $dropped 200
    Assert-That -Name 'Dropped cycle contributes no readings' `
        -Condition ($dropped.Content -like '*"readingsAccepted":0*') -Detail $dropped.Content

    Assert-Status 'Accept a 5-reading polling cycle' (Invoke-Api POST "/api/telemetry/power/$powerId/batches" @(140, 152, 149, 151, 147)) 200

    $history = Invoke-Api GET "/api/telemetry/power/$powerId/history"
    Assert-Status 'Fetch flattened history' $history 200

    $packets = $history.Content | ConvertFrom-Json
    Assert-That -Name 'Flattening counts readings (8), not batches (3)' `
        -Condition ($packets.Count -eq 8) -Detail "got $($packets.Count)"

    $stamps = $packets | Select-Object -ExpandProperty timestamp -Unique
    Assert-That -Name 'Readings in one cycle share a timestamp (2 distinct across 8)' `
        -Condition ($stamps.Count -eq 2) -Detail "got $($stamps.Count) distinct timestamps"

    $gap = ([datetime]$stamps[1] - [datetime]$stamps[0]).TotalSeconds
    Assert-That -Name 'Dropped cycle held its slot (10s gap across the skipped cycle)' `
        -Condition ([math]::Abs($gap - 10) -lt 0.01) -Detail "gap was $gap seconds"

    # --- attachments --------------------------------------------------------------------------

    Write-Section 'Media and log attachments'

    $sample = Join-Path $env:TEMP 'smartx-node-config.txt'
    Set-Content -Path $sample -Value "wifi_ssid=SmartX-Mesh`npoll_interval_ms=5000" -Encoding ascii

    $upload = curl.exe -s -o - -w '%{http_code}' -X POST "$BaseUrl/api/sensors/$environmentalId/attachments" -F "file=@$sample"
    Assert-That -Name 'Upload a config file to a registered sensor (expected HTTP 200)' `
        -Condition ($upload -match '200$') -Detail $upload

    $orphanUpload = curl.exe -s -o NUL -w '%{http_code}' -X POST "$BaseUrl/api/sensors/NOT-REGISTERED/attachments" -F "file=@$sample"
    Assert-That -Name 'Reject an upload for an unregistered sensor (expected HTTP 404)' `
        -Condition ($orphanUpload -eq '404') -Detail $orphanUpload

    # --- bulk load ----------------------------------------------------------------------------

    if (-not $SkipLoadTest) {
        Write-Section 'Bulk seeding under load'

        $cycles = 200
        $readingsPerCycle = 50
        $seedTimer = [Diagnostics.Stopwatch]::StartNew()

        for ($cycle = 0; $cycle -lt $cycles; $cycle++) {
            $values = 1..$readingsPerCycle | ForEach-Object { Get-Random -Minimum 800 -Maximum 2400 }
            Invoke-Api POST "/api/telemetry/power/$powerId/batches" $values | Out-Null
        }

        $seedTimer.Stop()

        $loadTimer = [Diagnostics.Stopwatch]::StartNew()
        $bulk = Invoke-Api GET "/api/telemetry/power/$powerId/history"
        $loadTimer.Stop()

        $bulkPackets = $bulk.Content | ConvertFrom-Json
        $expected = 8 + ($cycles * $readingsPerCycle)

        Assert-That -Name "Flattened $expected packets from $($cycles + 3) variable-length cycles" `
            -Condition ($bulkPackets.Count -eq $expected) -Detail "got $($bulkPackets.Count)"

        Write-Host ("        seeded in {0:N0} ms, flattened and returned in {1:N0} ms" -f `
            $seedTimer.ElapsedMilliseconds, $loadTimer.ElapsedMilliseconds) -ForegroundColor DarkGray
    }
}
finally {
    Write-Section 'Summary'
    Write-Host "  Passed: $script:Passed" -ForegroundColor Green
    Write-Host "  Failed: $script:Failed" -ForegroundColor $(if ($script:Failed -gt 0) { 'Red' } else { 'Green' })

    if ($KeepRunning -or $alreadyRunning) {
        Write-Host ''
        Write-Host "API still running. Swagger UI: $BaseUrl/swagger" -ForegroundColor Yellow
    }
    else {
        Stop-Api
        Write-Host ''
        Write-Host 'API stopped.' -ForegroundColor Yellow
    }
}

if ($script:Failed -gt 0) { exit 1 }
