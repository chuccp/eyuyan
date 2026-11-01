using Yy.Sdk;
using Yy.Studio.Core.Documentation;

namespace Yy.Studio.Core.Tests;

internal static class DocumentationRegression
{
    public static void VerifyCatalog()
    {
        TestAssert.True(
            DocumentationCatalog.Topics.Count > YySdk.Packages.Count,
            "开发文档没有包含语言、SDK 和编辑器主题。");

        foreach (var section in new[] { "快速开始", "语言语法", "系统 SDK", "命令参考", "编辑器" })
        {
            TestAssert.True(
                DocumentationCatalog.Topics.Any(topic => topic.Section == section),
                $"开发文档缺少“{section}”主题。");
        }

        foreach (var package in YySdk.Packages)
        {
            TestAssert.True(
                DocumentationCatalog.Topics.Any(topic =>
                    topic.Section == "系统 SDK" && topic.Title == package.Name),
                $"开发文档缺少 SDK 包“{package.Name}”。");
        }

        TestAssert.True(
            DocumentationCatalog.Search("系统.界面").Any(topic =>
                topic.Title == YySdk.UiPackage),
            "开发文档搜索没有定位到界面 SDK 包。");
        TestAssert.True(
            DocumentationCatalog.Search("取控件").Any(topic =>
                topic.Blocks.Any(block => block.Text.Contains(
                    "取控件",
                    StringComparison.Ordinal))),
            "开发文档搜索没有覆盖内置命令说明。");
        TestAssert.True(
            DocumentationCatalog.Search("json").Any(topic =>
                topic.SearchText.Contains("JSON", StringComparison.Ordinal)),
            "开发文档搜索应忽略英文大小写。");
        TestAssert.True(
            DocumentationCatalog.Search("无分号的易语表达式").Any(topic =>
                topic.Title == "变量、常量与表达式"),
            "开发文档搜索没有覆盖主题摘要。");

        TestAssert.Equal(
            DocumentationCatalog.Topics.Count,
            DocumentationCatalog.Search(null).Count);
        TestAssert.Equal(
            DocumentationCatalog.Topics.Count,
            DocumentationCatalog.Search("   ").Count);
        TestAssert.True(
            ReferenceEquals(DocumentationCatalog.Topics, DocumentationCatalog.Search("  ")),
            "空白搜索应返回完整主题列表。");
        TestAssert.Equal(
            0,
            DocumentationCatalog.Search("完全不存在的文档主题 xyz").Count);

        var kinds = DocumentationCatalog.Topics
            .SelectMany(topic => topic.Blocks)
            .Select(block => block.Kind)
            .Distinct()
            .ToArray();
        foreach (var kind in Enum.GetValues<DocumentationBlockKind>())
        {
            TestAssert.True(
                kinds.Contains(kind),
                $"开发文档缺少 {kind} 内容块。");
        }

        var expressions = DocumentationCatalog.Topics.Single(topic =>
            topic.Title == "变量、常量与表达式");
        TestAssert.True(
            expressions.SearchText.Contains(expressions.Summary, StringComparison.Ordinal),
            "主题搜索文本应包含摘要。");
        TestAssert.True(
            expressions.SearchText.Contains(
                "变量 姓名 = \"世界\"",
                StringComparison.Ordinal),
            "主题搜索文本应包含代码块。");
        TestAssert.True(
            DocumentationCatalog.Topics.Any(topic =>
                topic.Title == "默认快捷键" &&
                topic.Blocks.Any(block => block.Text.Contains("Ctrl+Space", StringComparison.Ordinal))),
            "快捷键文档内容缺失。");
    }

    public static void VerifyCommands()
    {
        var commandTopics = DocumentationCatalog.Topics
            .Where(topic => topic.Section == "命令参考")
            .ToArray();
        TestAssert.True(commandTopics.Length >= 10, "命令参考主题过少。");

        var textCategory = StudioCatalog.Categories.Single(category =>
            category.Name == "文本操作");
        var textTopic = commandTopics.Single(topic => topic.Title == "文本操作");
        TestAssert.Equal(textCategory.Commands.Count, textTopic.Blocks.Count);
        TestAssert.True(
            textTopic.Blocks.All(block => block.Kind == DocumentationBlockKind.Api),
            "命令主题应使用 API 内容块。");

        var overview = commandTopics.Single(topic =>
            topic.Title == "内置命令总览");
        TestAssert.True(
            overview.Blocks[0].Text.Contains(
                $"文本操作：{textCategory.Commands.Count} 条命令",
                StringComparison.Ordinal),
            "命令总览没有统计文本操作。");

        var controlBuiltin = StudioCatalog.Builtins.Single(builtin =>
            builtin.Name == "取控件");
        var variableTopic = commandTopics.Single(topic => topic.Title == "变量操作");
        TestAssert.True(
            variableTopic.Blocks.Any(block => block.Text.Contains(
                controlBuiltin.Signature + "\n" + controlBuiltin.Description,
                StringComparison.Ordinal)),
            "变量操作主题没有包含取控件签名。");

        foreach (var builtin in StudioCatalog.Builtins)
        {
            TestAssert.True(
                DocumentationCatalog.Topics.Any(topic =>
                    topic.Section == "命令参考" &&
                    topic.Blocks.Any(block => block.Text.StartsWith(
                        builtin.Signature,
                        StringComparison.Ordinal))),
                $"内置命令“{builtin.Name}”没有出现在命令参考中。");
        }

        foreach (var category in StudioCatalog.Categories.Where(category =>
                     category.Commands.Count > 0))
        {
            TestAssert.True(
                DocumentationCatalog.Topics.Any(topic =>
                    topic.Section == "命令参考" && topic.Title == category.Name),
                $"命令分类“{category.Name}”没有文档主题。");
        }
    }
}
