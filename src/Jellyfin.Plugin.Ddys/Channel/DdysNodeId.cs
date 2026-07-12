using System;
using System.Text;

namespace Jellyfin.Plugin.Ddys.Channel;

internal sealed class DdysNode
{
    public string Kind { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

internal static class DdysNodeId
{
    private const string Prefix = "ddys";

    public static string Create(string kind, string value)
    {
        return Prefix + "|" + Encode(kind ?? string.Empty) + "|" + Encode(value ?? string.Empty);
    }

    public static DdysNode Parse(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return new DdysNode();
        }

        try
        {
            var parts = id.Split('|');
            if (parts.Length != 3 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
            {
                return new DdysNode();
            }

            return new DdysNode
            {
                Kind = Decode(parts[1]),
                Value = Decode(parts[2])
            };
        }
        catch (FormatException)
        {
            return new DdysNode();
        }
        catch (ArgumentException)
        {
            return new DdysNode();
        }
    }

    private static string Encode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Decode(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = value.Replace('-', '+').Replace('_', '/');
        switch (text.Length % 4)
        {
            case 2:
                text += "==";
                break;
            case 3:
                text += "=";
                break;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(text));
    }
}
