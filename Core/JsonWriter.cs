#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MVZ2CloudSaves;

/// <summary>轻量 JSON 序列化器（不依赖 System.Text.Json）。</summary>
internal sealed class JsonWriter
{
    private readonly StringBuilder sb = new StringBuilder();
    private readonly Stack<bool> first = new Stack<bool>();
    private bool needComma;

    public JsonWriter BeginObject()
    {
        BeforeValue();
        sb.Append('{');
        first.Push(true);
        needComma = false;
        return this;
    }

    public JsonWriter EndObject()
    {
        sb.Append('}');
        first.Pop();
        needComma = true;
        return this;
    }

    public JsonWriter BeginArray()
    {
        BeforeValue();
        sb.Append('[');
        first.Push(true);
        needComma = false;
        return this;
    }

    public JsonWriter EndArray()
    {
        sb.Append(']');
        first.Pop();
        needComma = true;
        return this;
    }

    public JsonWriter Key(string key)
    {
        BeforeValue();
        sb.Append('"').Append(Escape(key)).Append("\":");
        needComma = false;
        return this;
    }

    public JsonWriter Value(string? value)
    {
        BeforeValue();
        if (value == null)
            sb.Append("null");
        else
            sb.Append('"').Append(Escape(value)).Append('"');
        needComma = true;
        return this;
    }

    public JsonWriter Value(long value)
    {
        BeforeValue();
        sb.Append(value.ToString(CultureInfo.InvariantCulture));
        needComma = true;
        return this;
    }

    public JsonWriter Value(double value)
    {
        BeforeValue();
        sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        needComma = true;
        return this;
    }

    public JsonWriter Value(bool value)
    {
        BeforeValue();
        sb.Append(value ? "true" : "false");
        needComma = true;
        return this;
    }

    public JsonWriter Null()
    {
        BeforeValue();
        sb.Append("null");
        needComma = true;
        return this;
    }

    public JsonWriter Raw(string json)
    {
        BeforeValue();
        sb.Append(json);
        needComma = true;
        return this;
    }

    private void BeforeValue()
    {
        if (first.Count == 0)
            return;
        if (first.Peek())
        {
            first.Pop();
            first.Push(false);
            return;
        }
        if (needComma)
            sb.Append(',');
    }

    public override string ToString() => sb.ToString();

    public static string Escape(string s)
    {
        var b = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\b': b.Append("\\b"); break;
                case '\f': b.Append("\\f"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        b.Append(c);
                    break;
            }
        }
        return b.ToString();
    }
}
