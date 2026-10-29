$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$studio = Join-Path $root "src\Yy.Studio\Yy.Studio.csproj"
$output = Join-Path $root "artifacts\publish\win-x64-framework-dependent"

Write-Host "生成精简版（目标机器需要 .NET 10 Desktop Runtime）..."
dotnet publish $studio `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

$exe = Join-Path $output "YyStudio.exe"
if (Test-Path $exe) {
    $size = (Get-Item $exe).Length / 1MB
    Write-Host ("精简版：{0}（{1:N1} MB）" -f $exe, $size)
}
