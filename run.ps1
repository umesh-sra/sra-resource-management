<#
.SYNOPSIS
  Starts the SRA-RMS API and Vue dev server, each in its own window.
.DESCRIPTION
  - API:  dotnet run --project src/SraRms.Api   -> http://localhost:5163 (Swagger at /swagger)
  - Web:  npm run dev in web/                   -> http://localhost:5173
  Prerequisites: .NET 9 SDK, Node.js, and PostgreSQL running on localhost:5432
  with the sra_rms database (see db/README.md). Close the spawned windows to stop.

  Re-running is safe: a tier that is already listening is left alone rather than
  started a second time (a duplicate would only fail to bind the port).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\run.ps1
#>

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$ApiPort = 5163
$WebPort = 5173
$DbPort  = 5432

function Test-Port([int]$Port) {
    [bool](Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

# Poll until the port accepts connections, so we open the browser when the app is
# actually ready instead of after a fixed guess. Returns $false on timeout.
function Wait-Port([int]$Port, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Port $Port) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

# --- sanity checks -----------------------------------------------------------
foreach ($tool in 'dotnet', 'npm') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Error "'$tool' not found on PATH. Install it and retry."
    }
}

if (-not (Test-Path (Join-Path $root 'src\SraRms.Api\appsettings.Development.json'))) {
    Write-Error "src\SraRms.Api\appsettings.Development.json is missing (holds the local DB connection string; it is git-ignored, so create it on new clones)."
}

# Warn (don't fail) if nothing is listening on the Postgres port.
if (-not (Test-Port $DbPort)) {
    Write-Warning "Nothing is listening on localhost:$DbPort - is PostgreSQL running? The API will start but every request will fail."
}

# First run: install front-end dependencies.
if (-not (Test-Path (Join-Path $root 'web\node_modules'))) {
    Write-Host 'Installing web dependencies (first run)...'
    Push-Location (Join-Path $root 'web')
    npm install
    Pop-Location
}

# --- start both tiers, each in its own window --------------------------------
if (Test-Port $ApiPort) {
    Write-Host "API already listening on http://localhost:$ApiPort - leaving it running."
} else {
    Write-Host "Starting API on http://localhost:$ApiPort ..."
    Start-Process powershell -ArgumentList '-NoExit', '-Command',
        "cd '$root'; dotnet run --project src\SraRms.Api"
}

if (Test-Port $WebPort) {
    Write-Host "Web dev server already listening on http://localhost:$WebPort - leaving it running."
} else {
    Write-Host "Starting web dev server on http://localhost:$WebPort ..."
    Start-Process powershell -ArgumentList '-NoExit', '-Command',
        "cd '$root\web'; npm run dev"
}

# The API compiles on first run, so give it the longer budget of the two.
Write-Host 'Waiting for the servers to come up...'
$apiUp = Wait-Port $ApiPort 90
$webUp = Wait-Port $WebPort 60

if (-not $apiUp) { Write-Warning "API did not start listening on port $ApiPort in time - check its window for build errors." }
if (-not $webUp) { Write-Warning "Web dev server did not start listening on port $WebPort in time - check its window." }

if ($webUp) {
    Start-Process "http://localhost:$WebPort"
}

Write-Host ''
Write-Host 'Servers are running in separate windows. Close those windows to stop.'
Write-Host "  API:     http://localhost:$ApiPort/swagger"
Write-Host "  Web app: http://localhost:$WebPort"
