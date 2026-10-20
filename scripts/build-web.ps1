# Build the Vue console and copy the output into the server's wwwroot.
# Usage: powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Push-Location (Join-Path $root "web")
try {
    Write-Host ">> npm install"
    npm.cmd install --no-fund --no-audit
    if ($LASTEXITCODE -ne 0) { throw "npm install failed" }

    Write-Host ">> vite build"
    npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed (type-check errors?)" }
}
finally {
    Pop-Location
}

$dest = Join-Path $root "src\server\TimeBank.Server\wwwroot"
Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $root "web\dist\*") $dest -Recurse -Force

Write-Host "Web UI built into $dest"
