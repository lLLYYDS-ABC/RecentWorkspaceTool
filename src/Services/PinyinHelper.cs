using System.Text;

namespace RecentWorkspaceWidget.Services
{
    public static class PinyinHelper
    {
        public static string GetPinyinInitials(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in text)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(char.ToLower(c));
                }
                else if (c >= 0x4e00 && c <= 0x9fa5)
                {
                    char py = GetChineseCharInitial(c);
                    if (py != '\0') sb.Append(py);
                }
            }
            return sb.ToString();
        }

        private static char GetChineseCharInitial(char c)
        {
            try
            {
                byte[] bytes = Encoding.GetEncoding("GB2312").GetBytes(new char[] { c });
                if (bytes.Length == 2)
                {
                    int code = (bytes[0] << 8) + bytes[1];
                    if (code >= 0xB0A1 && code <= 0xB0C4) return 'a';
                    if (code >= 0xB0C5 && code <= 0xB2C0) return 'b';
                    if (code >= 0xB2C1 && code <= 0xB4ED) return 'c';
                    if (code >= 0xB4EE && code <= 0xB6E9) return 'd';
                    if (code >= 0xB6EA && code <= 0xB7A1) return 'e';
                    if (code >= 0xB7A2 && code <= 0xB8C0) return 'f';
                    if (code >= 0xB8C1 && code <= 0xB9FD) return 'g';
                    if (code >= 0xB9FE && code <= 0xBBF6) return 'h';
                    if (code >= 0xBBF7 && code <= 0xBFA5) return 'j';
                    if (code >= 0xBFA6 && code <= 0xC0AB) return 'k';
                    if (code >= 0xC0AC && code <= 0xC2E7) return 'l';
                    if (code >= 0xC2E8 && code <= 0xC4C2) return 'm';
                    if (code >= 0xC4C3 && code <= 0xC5B5) return 'n';
                    if (code >= 0xC5B6 && code <= 0xC5BD) return 'o';
                    if (code >= 0xC5BE && code <= 0xC6D9) return 'p';
                    if (code >= 0xC6DA && code <= 0xC8BA) return 'q';
                    if (code >= 0xC8BB && code <= 0xC8F5) return 'r';
                    if (code >= 0xC8F6 && code <= 0xCBF9) return 's';
                    if (code >= 0xCBFA && code <= 0xCDD9) return 't';
                    if (code >= 0xCDDA && code <= 0xCEF3) return 'w';
                    if (code >= 0xCEF4 && code <= 0xD1B8) return 'x';
                    if (code >= 0xD1B9 && code <= 0xD4D0) return 'y';
                    if (code >= 0xD4D1 && code <= 0xD7F9) return 'z';
                }
            }
            catch { }
            return '\0';
        }
    }
}
