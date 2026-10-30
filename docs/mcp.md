# 易语 Studio 的 MCP 接口

易语 Studio 内置 MCP(Model Context Protocol)服务端,让 AI 客户端像用户一样操作 IDE:
新建程序、设计器放控件、属性夹改属性、创建事件子程序、写代码、F5 预览、点按钮、截图。
每个工具都调用 IDE 真实的界面操作代码路径(与点击菜单/组件箱/属性夹等价)。

## 连接方式

- **streamable HTTP**:IDE 启动后自动在 `127.0.0.1:37213` 监听(被占用则顺延至 37222)。
  实际端口写入 `%TEMP%\yystudio-mcp.port`。项目根 `.mcp.json` 已注册该端点。
- **stdio**:`YyStudio.exe --mcp-stdio` 以子进程方式拉起 IDE(GUI 一同显示),JSON-RPC 逐行收发。

## 工具一览(19 个)

| 工具 | 说明 |
| --- | --- |
| `state` | 读取文档/窗体/控件/预览现状 |
| `screenshot` | 离屏截取 IDE 主窗口或预览窗口为 PNG |
| `new_program` | 新建易语窗口程序(程序→新建→确定) |
| `create_project` | 从空目录创建完整工程并打开启动窗体 |
| `open_file` | 打开 .yyx/.yyc |
| `save_program` | 保存窗体三件套(path 形如 X:/目录/名.yyx) |
| `add_resource` | 复制图片/声音/文件到 `Resources` 并登记资源标识 |
| `add_control` | 设计器放控件(组件箱双击),可带名称/标题/位置/大小 |
| `set_property` | 属性夹编辑;target 为控件名或 `_窗口` |
| `rename_control` / `delete_control` | 重命名(含校验)/删除控件 |
| `create_event` | 创建官方命名事件子程序(被单击/创建完毕…) |
| `get_code` / `set_subprogram` | 读易语源码 / 写事件子程序语句体 |
| `delete_subprogram` | 删除当前用户代码中的冗余子程序 |
| `run_preview` | 运行窗体预览(F5) |
| `preview_click` | 预览中触发按钮事件,等待执行完成 |
| `preview_get_text` / `preview_close` | 读预览控件文本 / 关闭预览 |

## 请求示例

```json
{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"add_control","arguments":{"type":"按钮","x":608,"y":56,"name":"上一张按钮","text":"上一张","width":128,"height":44}}}
```

`set_subprogram` 的 body 为易语语句行(`\n` 分行),支持 变量/如果/否则/结束如果/赋值/调用 等,
由 IDE 自己的解析器转换为结构化模型,与代码编辑器同源。

## 示例工程

`examples/看图软件/` 是完全通过 MCP 接口构建的看图软件:
图片框 + 上一张/下一张 + 隐藏状态框(索引框/路径框),`取目录文件` 列图、循环切换,
事件命名对齐官方(`__启动窗口_创建完毕`、`_上一张按钮_被单击`)。

`examples/MCP资源演示/` 也是通过 MCP 从空白工程创建：两个图片资源导入
`Resources/images`，窗体标题栏和图片框使用 `@资源/图片/...` 标识，
按钮事件由 `set_subprogram` 写入并完成预览交互。
