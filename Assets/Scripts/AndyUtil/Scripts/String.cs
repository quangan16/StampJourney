using System.Text;
using UnityEngine;

namespace AndyUtil
{
    public static class String
    {
        public static string FormatWithOnlyFirstLetterUpperCased(this string inputString)
        {
            if (inputString == null) return null;
            return char.ToUpper(inputString[0]) + inputString.Substring(1).ToLowerInvariant();

        }

        public static string SecondsToMMSS(this string inputString, float inputTimeInSecond)
        {
            if (inputString == null || inputTimeInSecond < 0f) return null;
            StringBuilder sb = new StringBuilder(inputString);
            sb.Append(Mathf.Floor(inputTimeInSecond / 60));
            sb.Append(":");
            sb.Append(Mathf.Floor(inputTimeInSecond % 60));
            return sb.ToString();
        }
    }
}
