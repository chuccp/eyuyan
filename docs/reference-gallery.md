# 界面与代码编辑参考

代码组织参考易语言和 Visual Basic 的连续代码窗口；应用外壳、项目目录、工具窗口和顶栏采用现代 IDE 设计，不直接复刻参考图片的整体视觉。

## 易语言连续代码体

来源：[易语言官方《图解易语言》第 1 天](https://www.dywt.com.cn/help/day/day1.htm)

![程序集、子程序与连续代码体](https://www.dywt.com.cn/help/day/day1.files/image056.png)

观察：

- 程序集和子程序声明是紧凑表头。
- 语句区域连续排版，没有逐语句横向网格。
- 折叠标记、缩进枝线和语法颜色共同表达代码层级。

来源：[第 2 天](https://www.dywt.com.cn/help/day/day2.htm)

![代码表、提示与底部输出](https://www.dywt.com.cn/help/day/day2.files/image025.png)

来源：[第 3 天](https://www.dywt.com.cn/help/day/day3.htm)

![参数表与代码体](https://www.dywt.com.cn/help/day/day3.files/image004.png)

![局部变量、代码与诊断](https://www.dywt.com.cn/help/day/day3.files/image015.png)

来源：[第 7 天](https://www.dywt.com.cn/help/day/day7.htm)

![局部变量类型选择](https://www.dywt.com.cn/help/day/day7.files/image001.png)

![子程序参数结构](https://www.dywt.com.cn/help/day/day7.files/image025.png)

## VB6 代码窗口

VB6 与易语言代码窗口的共同结构：

- 窗口顶部是“对象 / 过程”下拉框。
- 主体是连续代码编辑器，不是逐行表单。
- 设计器双击控件后，事件过程直接出现在代码窗口中。
- 代码行保持块缩进，声明和过程共享同一个编辑上下文。

来源：[Visual Basic 6 教程代码窗口](https://www.vbtutor.net/chinese/)

![VB6 表单、代码窗口和工程窗口](https://www.vbtutor.net/chinese/images/vb6ch_fig2.1.jpg)

来源：[VB6 中文版代码窗口截图](https://www.cnblogs.com/)

![VB6 代码窗口对象与过程选择](https://images2015.cnblogs.com/blog/91513/201605/91513-20160512232350812-919058017.png)

## 当前采用

- 主窗口使用单条自定义顶栏，菜单、标题和窗口按钮共享同一条操作栏。
- `.yyc` 默认打开易语言式代码窗口。
- 顶部显示对象和过程选择器。
- 易语言代码正文连续、不显示行号，支持补全、参数提示、F1 帮助和块缩进。
- “易语言代码”模式只把方法声明放进紧凑小表，执行语句保持连续。
- “纯代码”模式隐藏方法表，显示行号和折叠栏，直接编辑完整源码。
- 窗体设计器双击控件时，自动选择对应对象和事件过程。
