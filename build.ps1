param (
    [switch]$RunTests = $true,
    [string]$OutputDir = "./publish"
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  NertyDb - Compilando Executavel Unico Portable " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

# Set local dotnet path if present
if (Test-Path "$env:LOCALAPPDATA\dotnet\dotnet.exe") {
    $env:DOTNET_ROOT = "$env:LOCALAPPDATA\dotnet"
    $env:PATH = "$env:LOCALAPPDATA\dotnet;$env:PATH"
}

# 1. Run Tests
if ($RunTests) {
    Write-Host "`n[1/3] Executando testes automatizados..." -ForegroundColor Yellow
    dotnet test ./tests/NertyDb.Tests/NertyDb.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Falha nos testes unitarios. Build abortado."
    }
    Write-Host "Testes concluidos com sucesso!" -ForegroundColor Green
}

# 2. Publish Single-File Self-Contained Binary
Write-Host "`n[2/3] Publicando executavel unico (Single-File Self-Contained x64)..." -ForegroundColor Yellow
dotnet publish ./src/NertyDb/NertyDb.csproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha na publicacao do executavel."
}

# 3. Summary
Write-Host "`n[3/3] Sucesso!" -ForegroundColor Green
$exePath = Join-Path $OutputDir "NertyDb.exe"
if (Test-Path $exePath) {
    $item = Get-Item $exePath
    $sizeMb = [math]::Round($item.Length / 1MB, 2)
    Write-Host ("Executavel gerado em: {0} ({1} MB)" -f $exePath, $sizeMb) -ForegroundColor Cyan
    Write-Host "Para executar, basta dar dois cliques em NertyDb.exe (100% portable, sem instalador)." -ForegroundColor Green
}
