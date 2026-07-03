<#
.SYNOPSIS
  Starts the SRA-RMS API and Vue dev server, each in its own window.
.DESCRIPTION
  - API:  dotnet run --project src/SraRms.Api   -> http://localhost:5163 (Swagger at /swagger)
  - Web:  npm run dev in web/                   -> http://localhost:5173
  Prerequisites: .NET 9 SDK, Node.js, and PostgreSQL running on localhost:5432
  with the sra_rms database (see db/README.md). Close the spawned windows to stop.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\run.ps1
#>

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

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
if (-not (Get-NetTCPConnection -LocalPort 5432 -State Listen -ErrorAction SilentlyContinue)) {
    Write-Warning 'Nothing is listening on localhost:5432 - is PostgreSQL running?'
}

# First run: install front-end dependencies.
if (-not (Test-Path (Join-Path $root 'web\node_modules'))) {
    Write-Host 'Installing web dependencies (first run)...'
    Push-Location (Join-Path $root 'web')
    npm install
    Pop-Location
}

# --- start both tiers, each in its own window --------------------------------
Write-Host 'Starting API on http://localhost:5163 ...'
Start-Process powershell -ArgumentList '-NoExit', '-Command',
    "cd '$root'; dotnet run --project src\SraRms.Api"

Write-Host 'Starting web dev server on http://localhost:5173 ...'
Start-Process powershell -ArgumentList '-NoExit', '-Command',
    "cd '$root\web'; npm run dev"

Start-Sleep -Seconds 8
Start-Process 'http://localhost:5173'

Write-Host ''
Write-Host 'Both servers launched in separate windows. Close those windows to stop.'
Write-Host '  API:     http://localhost:5163/swagger'
Write-Host '  Web app: http://localhost:5173'
