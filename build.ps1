$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$tests = Join-Path $root "tests\Yy.Language.Tests\Yy.Language.Tests.csproj"
$nativeTests = Join-Path $root "tests\Yy.Native.Tests\Yy.Native.Tests.csproj"
$sdkTests = Join-Path $root "tests\Yy.Sdk.Tests\Yy.Sdk.Tests.csproj"
$backendTests = Join-Path $root "tests\Yy.Native.Backends.Tests\Yy.Native.Backends.Tests.csproj"
$studioTests = Join-Path $root "tests\Yy.Studio.Tests\Yy.Studio.Tests.csproj"
$compilerCli = Join-Path $root "tools\Yy.Cli\Yy.Cli.csproj"
$nativeSampleExe = Join-Path $root "artifacts\samples\原生入门.exe"
$nativeUiSampleExe = Join-Path $root "artifacts\samples\原生窗体.exe"
$studio = Join-Path $root "src\Yy.Studio\Yy.Studio.csproj"
$output = Join-Path $root "artifacts\publish\YyStudio"
$compilerOutput = Join-Path $root "artifacts\publish\yy"

Write-Host "运行语言内核测试..."
dotnet run --project $tests -c Release
Write-Host "运行原生后端测试..."
dotnet run --project $nativeTests -c Release
Write-Host "运行 SDK 包测试..."
dotnet run --project $sdkTests -c Release
Write-Host "运行原生后端测试..."
dotnet run --project $backendTests -c Release
$env:YY_COMPILE_SMOKE = "1"
try {
    dotnet run --project $studioTests -c Release
}
finally {
    Remove-Item Env:YY_COMPILE_SMOKE -ErrorAction SilentlyContinue
}

Write-Host "编译原生命令行示例..."
dotnet run --project $compilerCli -c Release -- `
    build `
    (Join-Path $root "examples\Sources\原生入门.yyc") `
    --target windows-x64 `
    -o `
    $nativeSampleExe

Write-Host "编译原生 Win32 窗体示例..."
dotnet run --project $compilerCli -c Release -- `
    build `
    (Join-Path $root "examples\原生窗体") `
    --target windows-x64 `
    -o `
    $nativeUiSampleExe

$macSample = Join-Path $root "artifacts\samples\原生入门.macos-arm64"
Write-Host "交叉编译 macOS 控制台示例..."
dotnet run --project $compilerCli -c Release -- `
    build `
    (Join-Path $root "examples\Sources\原生入门.yyc") `
    --target macos-arm64 `
    -o `
    $macSample

$macFormSample = Join-Path $root "artifacts\samples\原生窗体.macos-arm64"
Write-Host "交叉编译 macOS AppKit 窗体示例..."
dotnet run --project $compilerCli -c Release -- `
    build `
    (Join-Path $root "examples\原生窗体") `
    --target macos-arm64 `
    -o `
    $macFormSample

Write-Host "发布 Windows 桌面版..."
dotnet publish $studio `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

Write-Host "发布命令行编译器..."
dotnet publish $compilerCli `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $compilerOutput

Write-Host ""
Write-Host "发布完成："
Write-Host (Join-Path $output "YyStudio.exe")
Write-Host (Join-Path $compilerOutput "yy.exe")
Write-Host $nativeSampleExe
Write-Host $nativeUiSampleExe
Write-Host $macSample
Write-Host $macFormSample

