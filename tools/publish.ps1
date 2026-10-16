# 本地构建发布脚本：生成免安装单文件 exe
# 用法：powershell -ExecutionPolicy Bypass -File tools\publish.ps1 [-OutDir ..\dist]

param([string]$OutDir = (Join-Path $PSScriptRoot '..\dist'))

dotnet publish (Join-Path $PSScriptRoot '..\src') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $OutDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "发布完成：$OutDir"
    Write-Host '注意：dist\data\ 为本地运行数据（数据库与图片），发布后可整目录拷贝迁移。'
} else {
    Write-Host "发布失败（退出码 $LASTEXITCODE）"
    exit 1
}
