using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartData.Core.BinarySerialization;

/// <summary>
/// Converts binary serialized data directly to JSON string.
/// </summary>
public static class BinaryToJson
{
    public static string Convert(byte[] data, int? maxValueLength = null)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinarySerializationReader(stream, leaveOpen: true);
        var sb = new System.Text.StringBuilder();
        WriteValue(reader, sb, maxValueLength);
        return sb.ToString();
    }

    private static void WriteValue(BinarySerializationReader reader, System.Text.StringBuilder sb, int? maxValueLength)
    {
        var type = reader.PeekType();
        switch (type)
        {
            case SerializedType.Nil:
                reader.Skip();
                sb.Append("null");
                break;
            case SerializedType.Boolean:
                sb.Append(reader.ReadBoolean() ? "true" : "false");
                break;
            case SerializedType.Integer:
                sb.Append(reader.ReadInt64());
                break;
            case SerializedType.Float:
                sb.Append(reader.ReadDouble());
                break;
            case SerializedType.String:
                var str = reader.ReadString() ?? "";
                var escaped = EscapeString(str);
                sb.Append('"');
                if (maxValueLength.HasValue && escaped.Length > maxValueLength.Value)
                {
                    sb.Append(escaped.AsSpan(0, maxValueLength.Value));
                    sb.Append("...");
                }
                else
                {
                    sb.Append(escaped);
                }
                sb.Append('"');
                break;
            case SerializedType.Binary:
                var bin = reader.ReadBinary();
                var base64 = System.Convert.ToBase64String(bin);
                sb.Append('"');
                if (maxValueLength.HasValue && base64.Length > maxValueLength.Value)
                {
                    sb.Append(base64.AsSpan(0, maxValueLength.Value));
                    sb.Append("...");
                }
                else
                {
                    sb.Append(base64);
                }
                sb.Append('"');
                break;
            case SerializedType.Array:
                WriteArray(reader, sb, maxValueLength);
                break;
            case SerializedType.Map:
            case SerializedType.Command:
            case SerializedType.Key:
                WriteMap(reader, sb, maxValueLength);
                break;
            default:
                sb.Append($"\"<unknown:{type}>\"");
                reader.Skip();
                break;
        }
    }

    private static void WriteArray(BinarySerializationReader reader, System.Text.StringBuilder sb, int? maxValueLength)
    {
        var count = reader.ReadArrayHeader();
        sb.Append('[');
        if (count < 0)
        {
            // Unbounded array
            bool first = true;
            while (!reader.IsEnd())
            {
                if (!first) sb.Append(',');
                first = false;
                WriteValue(reader, sb, maxValueLength);
            }
            reader.ReadEnd();
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteValue(reader, sb, maxValueLength);
            }
        }
        sb.Append(']');
    }

    private static void WriteMap(BinarySerializationReader reader, System.Text.StringBuilder sb, int? maxValueLength)
    {
        var count = reader.ReadMapHeader();
        sb.Append('{');
        if (count < 0)
        {
            // Unbounded map
            bool first = true;
            while (!reader.IsEnd())
            {
                if (!first) sb.Append(',');
                first = false;
                var key = reader.ReadKey();
                sb.Append('"');
                sb.Append(EscapeString(key));
                sb.Append("\":");
                WriteValue(reader, sb, maxValueLength);
            }
            reader.ReadEnd();
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                var key = reader.ReadKey();
                sb.Append('"');
                sb.Append(EscapeString(key));
                sb.Append("\":");
                WriteValue(reader, sb, maxValueLength);
            }
        }
        sb.Append('}');
    }

    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }
}
