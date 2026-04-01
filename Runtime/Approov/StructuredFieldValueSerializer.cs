using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Approov
{
    internal readonly struct StructuredFieldParameter
    {
        public StructuredFieldParameter(string key, object value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; }

        public object Value { get; }
    }

    internal readonly struct StructuredFieldItem
    {
        public StructuredFieldItem(object value, IReadOnlyList<StructuredFieldParameter> parameters = null)
        {
            Value = value;
            Parameters = parameters;
        }

        public object Value { get; }

        public IReadOnlyList<StructuredFieldParameter> Parameters { get; }
    }

    internal static class StructuredFieldValueSerializer
    {
        public static string SerializeDictionary(IReadOnlyList<System.Collections.Generic.KeyValuePair<string, StructuredFieldItem>> entries)
        {
            StringBuilder builder = new();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                System.Collections.Generic.KeyValuePair<string, StructuredFieldItem> entry = entries[i];
                builder.Append(entry.Key);
                builder.Append('=');
                builder.Append(SerializeItem(entry.Value));
            }

            return builder.ToString();
        }

        public static string SerializeItem(StructuredFieldItem item)
        {
            StringBuilder builder = new();
            builder.Append(SerializeBareItem(item.Value));
            AppendParameters(builder, item.Parameters);
            return builder.ToString();
        }

        public static string SerializeInnerList(IReadOnlyList<StructuredFieldItem> items, IReadOnlyList<StructuredFieldParameter> parameters)
        {
            StringBuilder builder = new();
            builder.Append('(');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(SerializeItem(items[i]));
            }

            builder.Append(')');
            AppendParameters(builder, parameters);
            return builder.ToString();
        }

        public static string SerializeString(string value)
        {
            return SerializeBareItem(value);
        }

        private static string SerializeBareItem(object value)
        {
            switch (value)
            {
                case null:
                    return string.Empty;
                case bool boolean:
                    return boolean ? "?1" : "?0";
                case int intValue:
                    return intValue.ToString(CultureInfo.InvariantCulture);
                case long longValue:
                    return longValue.ToString(CultureInfo.InvariantCulture);
                case string text:
                    return SerializeText(text);
                case byte[] bytes:
                    return ":" + Convert.ToBase64String(bytes) + ":";
                case IReadOnlyList<StructuredFieldItem> innerList:
                    return SerializeInnerList(innerList, null);
                default:
                    throw new InvalidOperationException("Unsupported structured field value type: " + value.GetType());
            }
        }

        private static string SerializeText(string value)
        {
            StringBuilder builder = new();
            builder.Append('"');
            foreach (char ch in value ?? string.Empty)
            {
                if (ch < 0x20 || ch > 0x7E)
                {
                    throw new FormatException("Invalid character in structured field string");
                }

                if (ch == '"' || ch == '\\')
                {
                    builder.Append('\\');
                }

                builder.Append(ch);
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static void AppendParameters(StringBuilder builder, IReadOnlyList<StructuredFieldParameter> parameters)
        {
            if (parameters == null)
            {
                return;
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                StructuredFieldParameter parameter = parameters[i];
                builder.Append(';');
                builder.Append(parameter.Key);

                if (parameter.Value is bool boolean && boolean)
                {
                    continue;
                }

                builder.Append('=');
                builder.Append(SerializeBareItem(parameter.Value));
            }
        }
    }
}
