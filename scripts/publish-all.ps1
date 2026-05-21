$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$rids = @('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64')
$outDir = 'dist'

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null

foreach ($rid in $rids) {
    Write-Host "Publishing $rid..."
    dotnet publish src/ContextOS.Mcp -c Release -r $rid `
        --self-contained true `
        -o "$outDir/$rid"

    if ($rid -like 'win-*') {
        Compress-Archive -Path "$outDir/$rid" -DestinationPath "$outDir/contextos-$rid.zip"
    } else {
        $src = Resolve-Path "$outDir/$rid"
        $dest = Resolve-Path $outDir
        & tar -czf "$dest/contextos-$rid.tar.gz" -C $dest $rid
    }
}

Get-ChildItem $outDir -File | Select-Object Name, @{N='Size';E={"{0:N0} KB" -f ($_.Length / 1KB)}} | Format-Table -AutoSize
