using System.Globalization;

namespace Yy.Language.Syntax;

public enum TokenKind
{
    Number,
    String,
    Identifier,
    Operator,
    Punctuation,
    NewLine,
    EndOfFile
}

public readonly record struct Token(
    TokenKind Kind,
    string Text,
    object? Value,
    int Line,
    int Column,
    int Offset)
{
    public bool IsWord(string word) =>
        Kind == TokenKind.Identifier && string.Equals(Text, word, StringComparison.Ordinal);

    public override string ToString() => $"{Kind}:{Text}";
}

public sealed class Lexer
{
    private static readonly string[] MultiCharacterOperators =
    [
        "??", "==", "!=", "<=", ">=", "&&", "||",
        "+=", "-=", "*=", "/=", "%="
    ];

    private readonly string _source;
    private readonly List<Token> _tokens = [];
    private int _offset;
    private int _line = 1;
    private int _column = 1;

    public Lexer(string source)
    {
        _source = source ?? string.Empty;
    }

    public IReadOnlyList<Token> Tokenize()
    {
        while (!IsAtEnd)
        {
            var token = NextToken();
            if (token.HasValue)
            {
                _tokens.Add(token.Value);
            }
        }

        _tokens.Add(new Token(TokenKind.EndOfFile, string.Empty, null, _line, _column, _offset));
        return _tokens;
    }

    private Token? NextToken()
    {
        var current = Peek();

        if (current is ' ' or '\t' or '\uFEFF')
        {
            Advance();
            return null;
        }

        if (current is '\r' or '\n')
        {
            var line = _line;
            var column = _column;
            var offset = _offset;
            ConsumeNewLine();
            return new Token(TokenKind.NewLine, "\n", null, line, column, offset);
        }

        if (current == '\'' || (current == '/' && Peek(1) == '/') || current == '#')
        {
            while (!IsAtEnd && Peek() is not '\r' and not '\n')
            {
                Advance();
            }

            return null;
        }

        if (current == '/' && Peek(1) == '*')
        {
            SkipBlockComment();
            return null;
        }

        if (current is '"' or '“')
        {
            return ReadString();
        }

        if (char.IsDigit(current))
        {
            return ReadNumber();
        }

        if (char.IsLetter(current) || current == '_')
        {
            return ReadIdentifier();
        }

        var lineStart = _line;
        var columnStart = _column;
        var offsetStart = _offset;

        foreach (var op in MultiCharacterOperators)
        {
            if (Matches(op))
            {
                for (var i = 0; i < op.Length; i++)
                {
                    Advance();
                }

                return new Token(TokenKind.Operator, op, op, lineStart, columnStart, offsetStart);
            }
        }

        if ("+-*/%<>=!?:".Contains(current))
        {
            Advance();
            return new Token(TokenKind.Operator, current.ToString(), current.ToString(), lineStart, columnStart, offsetStart);
        }

        if ("()[],{}.;".Contains(current))
        {
            Advance();
            return new Token(TokenKind.Punctuation, current.ToString(), current.ToString(), lineStart, columnStart, offsetStart);
        }

        throw new YyDiagnostic(
            $"无法识别字符“{current}”",
            lineStart,
            columnStart,
            "字符串内容需要放在英文或中文双引号中。");
    }

    private Token ReadString()
    {
        var opening = Peek();
        var closing = opening == '“' ? '”' : '"';
        var lineStart = _line;
        var columnStart = _column;
        var offsetStart = _offset;
        Advance();

        var builder = new System.Text.StringBuilder();
        while (!IsAtEnd && Peek() != closing)
        {
            var ch = Peek();
            if (ch is '\r' or '\n')
            {
                throw new YyDiagnostic("字符串没有结束引号", lineStart, columnStart);
            }

            if (ch == '\\')
            {
                Advance();
                if (IsAtEnd)
                {
                    throw new YyDiagnostic("字符串末尾存在未完成的转义符", lineStart, columnStart);
                }

                var escaped = Peek();
                builder.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    _ => escaped
                });
                Advance();
                continue;
            }

            builder.Append(ch);
            Advance();
        }

        if (IsAtEnd)
        {
            throw new YyDiagnostic("字符串没有结束引号", lineStart, columnStart);
        }

        Advance();
        return new Token(TokenKind.String, builder.ToString(), builder.ToString(), lineStart, columnStart, offsetStart);
    }

    private Token ReadNumber()
    {
        var lineStart = _line;
        var columnStart = _column;
        var offsetStart = _offset;
        var start = _offset;

        while (char.IsDigit(Peek()))
        {
            Advance();
        }

        if (Peek() == '.' && char.IsDigit(Peek(1)))
        {
            Advance();
            while (char.IsDigit(Peek()))
            {
                Advance();
            }
        }

        var text = _source[start.._offset];
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new YyDiagnostic($"数字“{text}”格式不正确", lineStart, columnStart);
        }

        return new Token(TokenKind.Number, text, value, lineStart, columnStart, offsetStart);
    }

    private Token ReadIdentifier()
    {
        var lineStart = _line;
        var columnStart = _column;
        var offsetStart = _offset;
        var start = _offset;

        Advance();
        while (char.IsLetterOrDigit(Peek()) || Peek() == '_')
        {
            Advance();
        }

        var text = _source[start.._offset];
        return new Token(TokenKind.Identifier, text, text, lineStart, columnStart, offsetStart);
    }

    private bool Matches(string value)
    {
        if (_offset + value.Length > _source.Length)
        {
            return false;
        }

        return _source.AsSpan(_offset, value.Length).SequenceEqual(value);
    }

    private void ConsumeNewLine()
    {
        if (Peek() == '\r')
        {
            AdvanceRaw();
            if (Peek() == '\n')
            {
                AdvanceRaw();
            }
        }
        else
        {
            AdvanceRaw();
        }

        _line++;
        _column = 1;
    }

    private void SkipBlockComment()
    {
        var lineStart = _line;
        var columnStart = _column;
        var depth = 0;

        while (!IsAtEnd)
        {
            if (Peek() == '/' && Peek(1) == '*')
            {
                depth++;
                Advance();
                Advance();
                continue;
            }

            if (Peek() == '*' && Peek(1) == '/')
            {
                depth--;
                Advance();
                Advance();
                if (depth == 0)
                {
                    return;
                }

                continue;
            }

            if (Peek() is '\r' or '\n')
            {
                ConsumeNewLine();
            }
            else
            {
                Advance();
            }
        }

        throw new YyDiagnostic("块注释没有结束标记“*/”", lineStart, columnStart);
    }

    private char Peek(int distance = 0)
    {
        var position = _offset + distance;
        return position >= _source.Length ? '\0' : _source[position];
    }

    private void Advance()
    {
        if (IsAtEnd)
        {
            return;
        }

        _offset++;
        _column++;
    }

    private void AdvanceRaw() => _offset++;

    private bool IsAtEnd => _offset >= _source.Length;
}

