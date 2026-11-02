using System.Text;

namespace Yy.Compiler.Convert;

/// <summary>.e 记录:帧类型码 + 文本。</summary>
internal sealed record ERecord(int Offset, ERecordKind Kind, string Text);

/// <summary>.e 记录类别(由帧类型码判定,不做文本猜测)。</summary>
internal enum ERecordKind
{
    /// <summary>子程序名(帧类型码 01 19)。</summary>
    Procedure,

    /// <summary>程序集名(帧类型码 01 52)。</summary>
    Assembly,

    /// <summary>变量/参数行(帧类型码 01 03)。</summary>
    Variable,

    /// <summary>文本串(帧类型码 1a:标题、属性值、语句文本、备注)。</summary>
    Text
}

/// <summary>
/// CNWTEPRG(.e)记录读取器。
/// .e 为带类型码的记录流:记录以 [int32 长度][GBK 文本] 帧存储,
/// 帧前的类型码标识字段类别(01 19 子程序、01 03 变量行、01 52 程序集、1a 文本串)。
/// </summary>
internal static class ERecordReader
{
    private static readonly byte[] ProcedureTag = [0x00, 0x01, 0x19];
    private static readonly byte[] VariableTag = [0x01, 0x03, 0x00];
    private static readonly byte[] AssemblyTag = [0x01, 0x52];

    static ERecordReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static IReadOnlyList<ERecord> Read(byte[] data)
    {
        var records = new List<ERecord>();
        var index = 8;
        while (index + 4 <= data.Length)
        {
            // 子程序记录:00 01 19 + 8 字节标志 + [长度][名称]。
            if (index >= 3 &&
                data.AsSpan(index - 3, 3).SequenceEqual(ProcedureTag) &&
                IsZero(data.AsSpan(index, 8)))
            {
                if (TryReadText(data, index + 8, out var name, out var next))
                {
                    records.Add(new ERecord(index - 3, ERecordKind.Procedure, name));
                    index = next;
                    continue;
                }
            }

            // 程序集记录:01 52 + 4 字节 + [长度][名称]。
            if (index >= 2 &&
                data.AsSpan(index - 2, 2).SequenceEqual(AssemblyTag) &&
                TryReadText(data, index + 4, out var assembly, out var afterAssembly) &&
                assembly.Length <= 40)
            {
                records.Add(new ERecord(index - 2, ERecordKind.Assembly, assembly));
                index = afterAssembly;
                continue;
            }

            // 变量/参数行:[长度][01 03 …][名称]。
            var length = BitConverter.ToInt32(data, index);
            if (length >= 8 && length <= 512 && index + 4 + length <= data.Length &&
                data.AsSpan(index + 4, 3).SequenceEqual(VariableTag))
            {
                var payload = data.AsSpan(index + 4, length);
                var name = DecodeGbk(payload[7..].TrimEnd((byte)0).TrimEnd((byte)0x20));
                if (name is { Length: > 0 } &&
                    name.All(character => char.IsLetterOrDigit(character) || character is '_' or '·'))
                {
                    records.Add(new ERecord(index, ERecordKind.Variable, name));
                    index += 4 + length;
                    continue;
                }
            }

            // 文本串:[长度][GBK]。按帧出现顺序保留(标题、属性值、语句文本、备注都在其中)。
            if (TryReadText(data, index, out var text, out var afterText))
            {
                records.Add(new ERecord(index, ERecordKind.Text, text));
                index = afterText;
                continue;
            }

            index++;
        }

        return records;
    }

    private static bool TryReadText(byte[] data, int offset, out string text, out int next)
    {
        text = string.Empty;
        next = offset;
        if (offset + 4 > data.Length)
        {
            return false;
        }

        var length = BitConverter.ToInt32(data, offset);
        if (length < 2 || length > 4096 || offset + 4 + length > data.Length)
        {
            return false;
        }

        var decoded = DecodeGbk(data.AsSpan(offset + 4, length).TrimEnd((byte)0));
        if (decoded is null || decoded.Length == 0 ||
            !decoded.Any(character => character > ''))
        {
            return false;
        }

        text = decoded;
        next = offset + 4 + length;
        return true;
    }

    private static string? DecodeGbk(ReadOnlySpan<byte> chunk)
    {
        try
        {
            var text = Encoding.GetEncoding(
                "GBK",
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback).GetString(chunk);
            return text.All(character =>
                !char.IsControl(character) || character is '\r' or '\n' or '\t')
                ? text.Trim()
                : null;
        }
        catch (EncoderFallbackException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool IsZero(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }
}
