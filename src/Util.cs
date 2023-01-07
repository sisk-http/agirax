using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;

namespace Sisk.Agirax
{
    internal class Util
    {
        internal static T? GetXmlInnerTextAs<T>(XmlNode? node) where T : IParsable<T>
        {
            bool ok = T.TryParse(GetXmlInnerText(node), null, out T? result);
            if (ok) return result; else return default;
        }

        internal static string? GetXmlInnerText(XmlNode? node, string? defaultValue = null)
        {
            return node?.InnerText ?? defaultValue;
        }

        internal static T? GetXmlAttributeAs<T>(XmlNode? node, string attributeName) where T : IParsable<T>
        {
            bool ok = T.TryParse(GetXmlAttribute(node, attributeName), null, out T? result);
            if (ok) return result; else return default;
        }

        internal static string? GetXmlAttribute(XmlNode? node, string attributeName, string? defaultValue = null)
        {
            return node?.Attributes?[attributeName]?.Value ?? defaultValue;
        }

        internal static string Combine(params string[] parts)
        {
            string[] newParts = parts.Select(p => p.Replace("\\", "/").Trim('/')).ToArray();
            string p = string.Join('/', newParts);
            return p;
        }

        internal static string EncodeParameterArgument(string original)
        {
            if (string.IsNullOrEmpty(original))
                return original;
            string value = Regex.Replace(original, @"(\\*)" + "\"", @"$1\$0");
            value = Regex.Replace(value, @"^(.*\s.*?)(\\*)$", "\"$1$2$2\"");
            return value;
        }

        internal static string IntToHumanSize(long i)
        {
            long absolute_i = (i < 0 ? -i : i);
            string suffix;
            double readable;
            if (absolute_i >= 0x40000000) // Gigabyte
            {
                suffix = "g";
                readable = (i >> 20);
            }
            else if (absolute_i >= 0x100000) // Megabyte
            {
                suffix = "m";
                readable = (i >> 10);
            }
            else if (absolute_i >= 0x400) // Kilobyte
            {
                suffix = "k";
                readable = i;
            }
            else
            {
                return i.ToString("0b"); // Byte
            }
            readable = (readable / 1024);
            return readable.ToString("0.##") + suffix;
        }

        internal static long? ParseSizeString(string? s)
        {
            if (s == null) return null;
            s = s.ToLower();
            string intPieceS = s.Substring(0, s.Length - 1);
            int intPiece = Int32.Parse(intPieceS);

            if (s.EndsWith("k"))
            {
                return intPiece * 1024;
            }
            else if (s.EndsWith("m"))
            {
                return intPiece * 1024 * 1024;
            }
            else if (s.EndsWith("g"))
            {
                return intPiece * 1024 * 1024 * 1024;
            }

            return intPiece;
        }
    }
}
