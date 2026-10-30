using Yy.Language;
using Yy.Language.Syntax;
using Yy.Sdk;

var failures = 0;
Run("SDK 包目录完整性", VerifyCatalog);
Run("SDK 包依赖图", VerifyDependencies);
Run("自举功能包", VerifyBootstrapPackages);
Run("未实现包禁止导入", VerifyPlannedPackageRejected);
Run("原生界面 SDK 契约", VerifyUiContract);
Run("系统 SDK 源码文件", VerifyPackageSources);
Run("结构体与接口语法", VerifyStructsAndInterfaces);

if (failures > 0)
{
    throw new Exception($"{failures} 项 SDK 测试失败。");
}

Console.WriteLine("全部 SDK 测试通过。");
return;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine("通过  " + name);
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"失败  {name}: {exception.Message}");
    }
}

static void VerifyCatalog()
{
    True(YySdk.Version == 1, "SDK 版本应为 1。");
    True(YySdk.Packages.Count >= 20, "SDK 包目录数量不足。");
    var names = new HashSet<string>(StringComparer.Ordinal);
    foreach (var package in YySdk.Packages)
    {
        True(YySdk.IsPackageName(package.Name), $"包名格式无效：{package.Name}");
        True(names.Add(package.Name), $"包名重复：{package.Name}");
        True(package.Members.Distinct(StringComparer.Ordinal).Count() == package.Members.Count,
            $"包“{package.Name}”包含重复成员。");
        True(package.Builtins.Distinct(StringComparer.Ordinal).Count() == package.Builtins.Count,
            $"包“{package.Name}”包含重复内置函数。");
        if (package.Status == YyPackageStatus.Planned)
        {
            True(!package.IsImportable, $"规划包“{package.Name}”不应可导入。");
            True(package.Builtins.Count == 0,
                $"规划包“{package.Name}”不能声明尚未实现的内置函数。");
        }
    }
}

static void VerifyDependencies()
{
    var packages = YySdk.Packages.ToDictionary(
        package => package.Name,
        StringComparer.Ordinal);
    var visiting = new HashSet<string>(StringComparer.Ordinal);
    var visited = new HashSet<string>(StringComparer.Ordinal);
    foreach (var package in YySdk.Packages)
    {
        foreach (var dependency in package.Dependencies)
        {
            True(packages.ContainsKey(dependency),
                $"包“{package.Name}”依赖不存在的包“{dependency}”。");
            True(dependency != package.Name,
                $"包“{package.Name}”不能依赖自身。");
        }

        Visit(package.Name);
    }

    return;

    void Visit(string name)
    {
        if (visited.Contains(name))
        {
            return;
        }

        True(visiting.Add(name), $"SDK 包依赖存在环：{name}");
        foreach (var dependency in packages[name].Dependencies)
        {
            Visit(dependency);
        }

        visiting.Remove(name);
        visited.Add(name);
    }
}

static void VerifyBootstrapPackages()
{
    string[] required =
    [
        YySdk.MemoryPackage,
        YySdk.EncodingPackage,
        YySdk.DiagnosticPackage,
        YySdk.SerializationPackage,
        YySdk.InteropPackage,
        YySdk.SecurityPackage,
        YySdk.NetworkPackage,
        YySdk.HttpPackage,
        YySdk.TlsPackage,
        YySdk.SocketPackage,
        YySdk.WebSocketPackage,
        YySdk.PackageManagerPackage,
        YySdk.BuildPackage,
        YySdk.CompilerToolchainPackage
    ];
    foreach (var name in required)
    {
        True(YySdk.FindPackage(name) is { Status: YyPackageStatus.Planned },
            $"缺少规划中的自举包：{name}");
    }

    True(YySdk.FindPackage(YySdk.ProcessPackage) is
        {
            Status: YyPackageStatus.Preview,
            IsImportable: true
        },
        "进程包应已升级为可导入预览包。");

    var http = YySdk.FindPackage(YySdk.HttpPackage)!;
    True(http.Dependencies.Contains(YySdk.NetworkPackage), "HTTP 包缺少网络基础依赖。");
    True(http.Dependencies.Contains(YySdk.TlsPackage), "HTTP 包缺少 TLS 依赖。");
    var tls = YySdk.FindPackage(YySdk.TlsPackage)!;
    True(tls.Dependencies.Contains(YySdk.SecurityPackage), "TLS 包缺少安全依赖。");
    True(tls.Dependencies.Contains(YySdk.InteropPackage), "TLS 包缺少动态库依赖。");
}

static void VerifyPlannedPackageRejected()
{
    try
    {
        YyRunner.CompileSource(
            """
            .程序集 规划包诊断
            导入包 "系统.网络.HTTP"
            输出("不应通过")
            """,
            "sdk-plan-test.yyc");
        throw new Exception("规划中的包不应允许导入。");
    }
    catch (YyDiagnostic diagnostic)
    {
        True(diagnostic.Message.Contains("尚未实现", StringComparison.Ordinal),
            $"规划包诊断不明确：{diagnostic.Message}");
    }
}

static void VerifyUiContract()
{
    True(!new YyNativeHandle(0).IsValid, "零句柄必须无效。");
    True(new YyNativeHandle(42).IsValid, "非零句柄必须有效。");
    True(YyUiValue.From("文本").Kind == YyUiValueKind.Text, "文本值类型错误。");
    True(YyUiValue.From(42L).IntegerValue == 42, "整数值类型错误。");
    True(YyUiValue.From(true).BooleanValue, "逻辑值类型错误。");
    True(
        YyUiValue.From(new YyNativeHandle(42)).HandleValue.Value == 42,
        "句柄值类型错误。");
    True(
        typeof(IYyContext).GetMethods().All(method =>
            method.ReturnType != typeof(object) &&
            method.GetParameters().All(parameter => parameter.ParameterType != typeof(object))),
        "界面 SDK 不能暴露 object 类型。");
    True(
        typeof(IYyUiHost).GetInterfaces().Contains(typeof(IYyContext)),
        "界面宿主必须实现上下文契约。");
}

static void VerifyPackageSources()
{
    var root = Path.Combine(Directory.GetCurrentDirectory(), "sdk", "Sources");
    foreach (var package in YySdk.Packages)
    {
        var relative = package.Name.Replace('.', Path.DirectorySeparatorChar) + ".yyc";
        var path = Path.Combine(root, relative);
        True(File.Exists(path), $"SDK 包缺少源码文件：{relative}");
        YyRunner.CompileSource(File.ReadAllText(path), path);
    }

    True(YySdk.DefaultPackages.Count == 4, "语言核心默认包数量应为 4。");
    True(YySdk.DefaultPackages.Contains(YySdk.CorePackage), "基础包应默认引用。");
    True(YySdk.DefaultPackages.Contains(YySdk.CollectionPackage), "集合包应默认引用。");
    True(!YySdk.DefaultPackages.Contains(YySdk.FilePackage), "文件包不应默认引用。");
}

static void VerifyStructsAndInterfaces()
{
    var output = new List<string>();
    var result = YyRunner.RunSource(
        """
        .程序集 结构体测试
        接口 可问候
            子程序 问候(): 文本
        结束接口
        结构体 学生 实现 可问候
            姓名: 文本
            年龄: 数字
        结束结构体
        变量 张三: 学生 = {姓名: "张三", 年龄: 18}
        输出(张三.姓名)
        """,
        "struct-test.yyc",
        output.Add);
    True(
        string.Equals(string.Join("\n", output), "张三", StringComparison.Ordinal),
        "结构体字段没有正确读取。");
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}
