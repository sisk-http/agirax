using System.Text.RegularExpressions;

namespace Sisk.Agirax
{
    internal class Util
    {
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
