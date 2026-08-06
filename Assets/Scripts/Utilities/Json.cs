using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TirumalaAR.Utilities
{
    public enum JsonType { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// Minimal allocation-conscious JSON DOM.
    /// Unity's JsonUtility cannot represent GeoJSON at all — "coordinates" is an array of arrays
    /// of numbers, and JsonUtility has no concept of nested collections or heterogeneous values.
    /// This recursive-descent parser is used for the route file; the landmark file still goes
    /// through JsonUtility because its schema maps cleanly onto a serialisable class.
    /// </summary>
    public sealed class JsonNode
    {
        public JsonType Type { get; private set; }

        double m_Number;
        bool m_Bool;
        string m_String;
        List<JsonNode> m_Array;
        Dictionary<string, JsonNode> m_Object;

        public static readonly JsonNode Null = new JsonNode { Type = JsonType.Null };

        // --- Accessors -------------------------------------------------------------------

        public int Count => Type switch
        {
            JsonType.Array => m_Array.Count,
            JsonType.Object => m_Object.Count,
            _ => 0
        };

        public JsonNode this[int index] =>
            Type == JsonType.Array && index >= 0 && index < m_Array.Count ? m_Array[index] : Null;

        public JsonNode this[string key] =>
            Type == JsonType.Object && m_Object.TryGetValue(key, out var node) ? node : Null;

        public bool Has(string key) => Type == JsonType.Object && m_Object.ContainsKey(key);

        public IEnumerable<JsonNode> Items => (IEnumerable<JsonNode>)m_Array ?? Array.Empty<JsonNode>();

        public IEnumerable<KeyValuePair<string, JsonNode>> Members =>
            (IEnumerable<KeyValuePair<string, JsonNode>>)m_Object ?? Array.Empty<KeyValuePair<string, JsonNode>>();

        public double AsDouble(double fallback = 0d) => Type switch
        {
            JsonType.Number => m_Number,
            JsonType.String when double.TryParse(m_String, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) => v,
            _ => fallback
        };

        public float AsFloat(float fallback = 0f) => (float)AsDouble(fallback);

        public int AsInt(int fallback = 0) => Type == JsonType.Number ? (int)Math.Round(m_Number) : fallback;

        public bool AsBool(bool fallback = false) => Type switch
        {
            JsonType.Bool => m_Bool,
            JsonType.Number => Math.Abs(m_Number) > double.Epsilon,
            _ => fallback
        };

        public string AsString(string fallback = null) => Type switch
        {
            JsonType.String => m_String,
            JsonType.Number => m_Number.ToString(CultureInfo.InvariantCulture),
            JsonType.Bool => m_Bool ? "true" : "false",
            _ => fallback
        };

        // --- Parsing ---------------------------------------------------------------------

        public static JsonNode Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("JSON input was empty.");

            var index = 0;
            var node = ParseValue(text, ref index);
            SkipWhitespace(text, ref index);

            if (index < text.Length)
                throw new FormatException($"Unexpected trailing content at offset {index}.");

            return node;
        }

        public static bool TryParse(string text, out JsonNode node, out string error)
        {
            try
            {
                node = Parse(text);
                error = null;
                return true;
            }
            catch (Exception e)
            {
                node = Null;
                error = e.Message;
                return false;
            }
        }

        static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        static JsonNode ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);

            if (i >= s.Length)
                throw new FormatException("Unexpected end of JSON input.");

            return s[i] switch
            {
                '{' => ParseObject(s, ref i),
                '[' => ParseArray(s, ref i),
                '"' => new JsonNode { Type = JsonType.String, m_String = ParseString(s, ref i) },
                't' or 'f' => ParseBool(s, ref i),
                'n' => ParseNull(s, ref i),
                _ => ParseNumber(s, ref i)
            };
        }

        static JsonNode ParseObject(string s, ref int i)
        {
            var node = new JsonNode { Type = JsonType.Object, m_Object = new Dictionary<string, JsonNode>() };
            i++; // consume '{'

            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return node;
            }

            while (i < s.Length)
            {
                SkipWhitespace(s, ref i);

                if (s[i] != '"')
                    throw new FormatException($"Expected an object key at offset {i}.");

                var key = ParseString(s, ref i);

                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new FormatException($"Expected ':' after key '{key}' at offset {i}.");
                i++;

                node.m_Object[key] = ParseValue(s, ref i);

                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                    break;

                if (s[i] == ',')
                {
                    i++;

                    // Tolerate a trailing comma before '}' — hand-edited data files often carry one.
                    SkipWhitespace(s, ref i);
                    if (i < s.Length && s[i] == '}')
                    {
                        i++;
                        return node;
                    }

                    continue;
                }

                if (s[i] == '}')
                {
                    i++;
                    return node;
                }

                throw new FormatException($"Expected ',' or '}}' at offset {i}.");
            }

            throw new FormatException("Unterminated JSON object.");
        }

        static JsonNode ParseArray(string s, ref int i)
        {
            var node = new JsonNode { Type = JsonType.Array, m_Array = new List<JsonNode>() };
            i++; // consume '['

            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return node;
            }

            while (i < s.Length)
            {
                node.m_Array.Add(ParseValue(s, ref i));

                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                    break;

                if (s[i] == ',')
                {
                    i++;

                    // Same trailing-comma tolerance as objects.
                    SkipWhitespace(s, ref i);
                    if (i < s.Length && s[i] == ']')
                    {
                        i++;
                        return node;
                    }

                    continue;
                }

                if (s[i] == ']')
                {
                    i++;
                    return node;
                }

                throw new FormatException($"Expected ',' or ']' at offset {i}.");
            }

            throw new FormatException("Unterminated JSON array.");
        }

        static string ParseString(string s, ref int i)
        {
            i++; // consume opening quote
            var builder = new StringBuilder();

            while (i < s.Length)
            {
                var c = s[i++];

                if (c == '"')
                    return builder.ToString();

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (i >= s.Length)
                    break;

                var escape = s[i++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length)
                            throw new FormatException("Truncated \\u escape.");
                        builder.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default:
                        throw new FormatException($"Unknown escape '\\{escape}' at offset {i}.");
                }
            }

            throw new FormatException("Unterminated JSON string.");
        }

        static JsonNode ParseBool(string s, ref int i)
        {
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0)
            {
                i += 4;
                return new JsonNode { Type = JsonType.Bool, m_Bool = true };
            }

            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0)
            {
                i += 5;
                return new JsonNode { Type = JsonType.Bool, m_Bool = false };
            }

            throw new FormatException($"Invalid literal at offset {i}.");
        }

        static JsonNode ParseNull(string s, ref int i)
        {
            if (string.CompareOrdinal(s, i, "null", 0, 4) != 0)
                throw new FormatException($"Invalid literal at offset {i}.");

            i += 4;
            return Null;
        }

        static JsonNode ParseNumber(string s, ref int i)
        {
            var start = i;

            if (i < s.Length && (s[i] == '-' || s[i] == '+'))
                i++;

            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' ||
                                    ((s[i] == '-' || s[i] == '+') && (s[i - 1] == 'e' || s[i - 1] == 'E'))))
                i++;

            var slice = s.Substring(start, i - start);
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"Invalid number '{slice}' at offset {start}.");

            return new JsonNode { Type = JsonType.Number, m_Number = value };
        }
    }
}
