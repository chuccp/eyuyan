using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Yy.Studio.Core.Documentation;

namespace Yy.Studio.Avalonia.Features.Documentation;

public sealed partial class DocumentationWindow
{
    private static readonly IBrush BodyBrush = Brush.Parse("#20252B");
    private static readonly IBrush AccentBrush = Brush.Parse("#3574F0");
    private static readonly IBrush CardBrush = Brush.Parse("#F4F7FB");
    private static readonly IBrush CardBorderBrush = Brush.Parse("#E1E6ED");
    private static readonly FontFamily CodeFont =
        FontFamily.Parse("Menlo,Consolas,monospace");

    private static Control CreateDocumentationBlock(DocumentationBlock block) =>
        block.Kind switch
        {
            DocumentationBlockKind.Code => CreateCodeBlock(block.Text),
            DocumentationBlockKind.Api => CreateApiBlock(block.Text),
            DocumentationBlockKind.Note => CreateNoteBlock(block.Text),
            _ => CreateParagraphBlock(block.Text)
        };

    private static Control CreateParagraphBlock(string text)
    {
        var paragraph = new TextBlock
        {
            FontSize = 13,
            LineHeight = 22,
            TextWrapping = TextWrapping.Wrap,
            Foreground = BodyBrush,
            Margin = new Thickness(0, 0, 0, 14)
        };
        AddLines(paragraph, text);
        return paragraph;
    }

    private static Control CreateCodeBlock(string text)
    {
        var content = new TextBlock
        {
            FontFamily = CodeFont,
            FontSize = 12,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Foreground = BodyBrush
        };
        AddLines(content, text);
        return new Border
        {
            Background = CardBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 11, 14, 11),
            Margin = new Thickness(0, 0, 0, 14),
            Child = content
        };
    }

    private static Control CreateNoteBlock(string text)
    {
        var content = new TextBlock
        {
            FontSize = 13,
            LineHeight = 21,
            TextWrapping = TextWrapping.Wrap,
            Foreground = BodyBrush
        };
        var inlines = content.Inlines ??= new InlineCollection();
        inlines.Add(new Run("说明  ")
        {
            FontWeight = FontWeight.SemiBold,
            Foreground = AccentBrush
        });
        AddLines(content, text);
        return new Border
        {
            Background = CardBrush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 14),
            Child = content
        };
    }

    private static Control CreateApiBlock(string text)
    {
        var pieces = text.Split('\n', 2);
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = pieces[0],
            FontFamily = CodeFont,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = AccentBrush,
            TextWrapping = TextWrapping.Wrap
        });
        if (pieces.Length > 1)
        {
            var description = new TextBlock
            {
                FontSize = 12.5,
                LineHeight = 20,
                TextWrapping = TextWrapping.Wrap,
                Foreground = BodyBrush
            };
            AddLines(description, pieces[1]);
            content.Children.Add(description);
        }

        return new Border
        {
            Background = CardBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 14),
            Child = content
        };
    }

    private static void AddLines(TextBlock target, string text)
    {
        var inlines = target.Inlines ??= new InlineCollection();
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                inlines.Add(new LineBreak());
            }

            inlines.Add(new Run(lines[index]));
        }
    }
}
