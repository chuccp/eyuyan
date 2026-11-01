#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
artifacts="$root/artifacts"
samples="$artifacts/samples"
cli_project="$root/tools/Yy.Cli/Yy.Cli.csproj"

echo "构建解决方案..."
dotnet build "$root/Eyuyan.slnx" -c Release

echo "运行语言内核测试..."
dotnet run --project "$root/tests/Yy.Language.Tests/Yy.Language.Tests.csproj" -c Release

echo "运行 SDK 包测试..."
dotnet run --project "$root/tests/Yy.Sdk.Tests/Yy.Sdk.Tests.csproj" -c Release

echo "运行原生后端测试..."
dotnet run --project "$root/tests/Yy.Native.Backends.Tests/Yy.Native.Backends.Tests.csproj" -c Release

mkdir -p "$samples"

echo "编译 Windows 控制台示例..."
dotnet run --project "$cli_project" -c Release -- \
    build "$root/examples/Sources/原生入门.yyc" \
    --target windows-x64 -o "$samples/原生入门.exe"

echo "编译 Windows 窗体示例..."
dotnet run --project "$cli_project" -c Release -- \
    build "$root/examples/原生窗体" \
    --target windows-x64 -o "$samples/原生窗体.exe"

echo "编译 macOS 控制台示例..."
dotnet run --project "$cli_project" -c Release -- \
    build "$root/examples/Sources/原生入门.yyc" \
    --target macos-arm64 -o "$samples/原生入门.macos-arm64"

echo "编译 macOS AppKit 窗体示例..."
dotnet run --project "$cli_project" -c Release -- \
    build "$root/examples/原生窗体" \
    --target macos-arm64 -o "$samples/原生窗体.macos-arm64"

host_rid=""
case "$(uname -s):$(uname -m)" in
    Darwin:arm64) host_rid="osx-arm64" ;;
    Darwin:x86_64) host_rid="osx-x64" ;;
    Linux:x86_64) host_rid="linux-x64" ;;
    Linux:aarch64) host_rid="linux-arm64" ;;
esac

if [[ -n "$host_rid" ]]; then
    echo "发布命令行编译器（${host_rid}）..."
    dotnet publish "$cli_project" -c Release -r "$host_rid" --self-contained false \
        -o "$artifacts/publish/yy-$host_rid"
fi

echo ""
echo "构建完成："
echo "  $samples/原生入门.exe"
echo "  $samples/原生窗体.exe"
echo "  $samples/原生入门.macos-arm64"
echo "  $samples/原生窗体.macos-arm64"
if [[ -n "$host_rid" ]]; then
    echo "  $artifacts/publish/yy-$host_rid/yy"
fi
