# 1. Define o caminho de saída
$destinationPath = "D:\Users\almir.martinelli\Documents\_centralizados\AplicativosHelpers\LogFilterApp"

# 2. Garante que o script saiba onde está e suba para a raiz do projeto
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location "$scriptPath\.."

Write-Host "🚀 Iniciando o Deploy do LogFilterApp (Release)..." -ForegroundColor Cyan

# 3. Executa o Publish
# --self-contained false: assume que o PC tem o runtime do .NET instalado
# -p:PublishSingleFile=true: (Opcional) empacota tudo em um único .exe
dotnet publish -c Release -o $destinationPath --nologo

# 4. Feedback de sucesso ou erro
if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ Deploy concluído com sucesso!" -ForegroundColor Green
    Write-Host "📂 Local: $destinationPath" -ForegroundColor Gray
} else {
    Write-Host "`n❌ Ocorreu um erro durante a publicação." -ForegroundColor Red
}

Write-Host "`nPressione qualquer tecla para sair..."
$null = [Console]::ReadKey()