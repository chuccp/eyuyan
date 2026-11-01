# 原生后端架构

`Yy.Native` 把“语言语义”和“平台目标”分成两层。语言、工程、SDK 和类型规则只维护一份；每个操作系统/架构组合通过独立后端接入，不需要在编译器、CLI 或 IDE 中散布平台条件。

## 分层

```text
Yy.Language            词法、解析、AST、诊断
        ↓
Yy.Native/Semantics    平台无关语义：类型推断、对象形状、栈槽、值布局、内置命令目录
        ↓
Yy.Native/Backends     后端注册表 + 各平台代码生成与可执行文件写出
        ├─ Windows/    x64、PE32+、Win32
        └─ MacOS/      ARM64、Mach-O、libSystem
```

`Yy.Compiler` 只负责工程、窗体和资源模型；`Yy.Cli` 与 IDE 都通过 `NativeCompiler` 进入同一套后端选择。

## 后端契约

| 类型 | 职责 |
|---|---|
| `INativeBackend` | 声明名称和 `Targets`，接收 `NativeBackendRequest` 并返回 `NativeBuildResult` |
| `NativeBackendRegistry` | 按目标 ID 选择后端；未注册目标给出明确诊断，不静默回退 |
| `NativeBackendRequest` | 已解析的 AST、源码名、目标、输出路径、窗体模型 |
| `NativeCompiler` | 装载程序、解析目标、分派后端；不包含任何平台分支 |

新增平台时只需要：

1. 在 `Yy.Compiler/Build/YyBuildTarget.cs` 登记目标；
2. 实现一个 `INativeBackend`，包括代码生成器和镜像写出器；
3. 在 `NativeBackendRegistry` 注册；
4. 增加后端测试、CLI 帮助和文档。

未完成的目标必须保持“未注册”状态，构建时报 `目标“...”的原生后端尚未实现`，不能回退到其他平台或托管运行时。

## 共享语义

| 文件 | 内容 |
|---|---|
| `Semantics/NativeTypeAnalyzer.cs` | 表达式、数组元素、函数返回值和声明类型推断；平台界面成员通过注入钩子扩展 |
| `Semantics/NativeContext.cs` | 局部变量的栈槽编号、循环标签、对象/数组类型状态 |
| `Semantics/NativeObjectShape.cs` | 固定形状对象的字段顺序和字段类型 |
| `Semantics/NativeBuiltinCatalog.cs` | 语言核心内置命令名称与结果类型 |
| `Semantics/NativeRuntimeLayout.cs` | 文本、数组、对象的值布局和换行约定 |
| `Semantics/NativeDataBuffer.cs` | 常量文本、零填充缓冲和标签重定位 |

Windows 与 macOS 后端共用以上模型。平台特有的界面属性（例如 `参数`、`结果`、控件文本）由 Windows 后端通过 `NativeTypeAnalyzer` 的 `platformInfer` 钩子接入，通用语义不出现平台分支。

## 值布局

所有后端必须使用同一套内存布局：

```text
文本   [i64 长度][UTF-8 字节...][0]
数组   [i64 元素数][元素0][元素1]...
对象   [i64 字段数][字段0][字段1]...
整数   64 位有符号整数
逻辑   0 或 1
```

文本字节使用 UTF-8，长度不含结尾 0。数组和对象元素固定占 8 字节。该约定使文件、资源、控件绑定和未来第三方互操作共享同一套运行时模型。

## 平台后端职责

| 职责 | Windows | macOS |
|---|---|---|
| 目标 | `windows-x64` | `macos-arm64` |
| 指令集 | x64 | AArch64 |
| 调用约定 | Win64 + 自定义内部帧 | AAPCS64 + x29/x30 帧 |
| 系统接口 | PE 导入表连接 `kernel32/user32/gdiplus` | dyld 绑定 `libSystem`；窗体另加载 `libobjc/Foundation/AppKit` |
| 可执行格式 | PE32+ | Mach-O 64 位 |
| 签名 | 无 | 内嵌 ad-hoc CodeDirectory |
| 窗体 | Win32 已实现 | AppKit 常用控件已实现 |

后端只负责上述平台差异；表达式求值、变量槽、控制流、数组/对象、文本、文件和进程语义与共享层保持一致。

## 验证

- `tests/Yy.Native.Backends.Tests` 验证后端注册表、Mach-O 结构、内嵌签名和 Windows 交叉编译；在 Apple Silicon macOS 上还会真实运行生成的可执行文件。
- Windows 原生回归仍由 `tests/Yy.Native.Tests` 执行，覆盖 PE、Win32 窗口和控件事件。
- 涉及新平台时必须增加真实可执行文件测试，不能只检查源码或文件头。
