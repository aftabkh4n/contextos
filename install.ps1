#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Detect architecture
if ($env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
    Write-Host "Unsupported architecture: $env:PROCESSOR_ARCHITECTURE. Only x64 (AMD64) is supported." -ForegroundColor Red
    exit 1
}

$rid = 'win-x64'
$installDir = "$env:LOCALAPPDATA\Programs\contextos"
$binPath = "$installDir\$rid"
$exe = "$binPath\contextos.exe"
$url = 'https://github.com/aftabkh4n/contextos/releases/latest/download/contextos-win-x64.zip'
$tmpZip = "$env:TEMP\contextos-install.zip"

Write-Host "Installing ContextOS ($rid)..."

# Kill any running contextos process so the DLLs are not locked during extraction
Get-Process -Name contextos -ErrorAction SilentlyContinue | Stop-Process -Force

# Download
Write-Host "Downloading $url..."
try {
    Invoke-WebRequest -Uri $url -OutFile $tmpZip
} catch {
    Write-Host "Download failed: $url" -ForegroundColor Red
    exit 1
}

# Extract
Write-Host "Extracting..."
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Expand-Archive -Path $tmpZip -DestinationPath $installDir -Force
Remove-Item $tmpZip -ErrorAction SilentlyContinue

# Add to user PATH permanently
$currentUserPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$pathDirs = $currentUserPath -split ';' | Where-Object { $_ -ne '' }
if ($pathDirs -notcontains $binPath) {
    [Environment]::SetEnvironmentVariable('Path', "$currentUserPath;$binPath", 'User')
    Write-Host "Added to PATH. Restart PowerShell for PATH changes to take effect."
} else {
    Write-Host "$binPath is already in PATH."
}

# Register with Claude Code
$claudeCmd = Get-Command claude -ErrorAction SilentlyContinue
if ($null -ne $claudeCmd) {
    # Remove existing registration so upgrades work cleanly.
    # Prints "not found" on fresh install -- that is harmless.
    & claude mcp remove contextos
    & claude mcp add --scope user contextos -- $exe
    Write-Host "Registered with Claude Code."
} else {
    Write-Host "Claude Code CLI not found. After installing it, run:"
    Write-Host "  claude mcp add --scope user contextos -- $exe"
}

# Selftest (non-fatal)
$ErrorActionPreference = 'Continue'
& $exe --selftest
if ($LASTEXITCODE -eq 0) {
    Write-Host "ContextOS OK"
} else {
    Write-Host "Selftest failed. Check ~/.contextos/logs/ or run: contextos --selftest"
}

$ProgressPreference = 'Continue'

Write-Host ""
Write-Host "ContextOS installed successfully."
Write-Host ""
Write-Host "Next step: open Claude Code in any git repo and ask:"
Write-Host "  'What was I working on?'"
Write-Host ""
Write-Host "Docs: https://github.com/aftabkh4n/contextos"
