using ICSharpCode.AvalonEdit.Document;
using Yy.Studio.Structured;

internal static class FoldingRegression
{
    public static void VerifySortedFoldings()
    {
        var document = new TextDocument(
            """
            子程序 第一个()
                如果 真 则
                    输出("一")
                结束如果
            结束子程序
            子程序 第二个()
                计次循环 序号 从 1 到 2
                    输出(序号)
                结束循环
            结束子程序
            """);
        var foldings = EasyLanguageFoldingStrategy.CreateFoldings(document).ToArray();
        if (foldings.Length != 4)
        {
            throw new InvalidOperationException(
                $"经典视图折叠数量异常：期望 4，实际 {foldings.Length}。");
        }

        for (var index = 1; index < foldings.Length; index++)
        {
            if (foldings[index].StartOffset < foldings[index - 1].StartOffset)
            {
                throw new InvalidOperationException("经典视图折叠区域未按起点排序。");
            }
        }
    }
}
