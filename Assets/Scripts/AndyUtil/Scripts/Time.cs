
using System.Collections;
using System.Text;
using UnityEngine;

namespace AndyUtil
{
    public static class TimeHelper
    {
        public static string FormatToMMSS(this float inputTimeInSecond)
        {
            if (inputTimeInSecond < 0f) return "00:00";
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("{0:D2}", Mathf.FloorToInt(inputTimeInSecond / 60));
            sb.Append(":");
            sb.AppendFormat("{0:D2}", Mathf.FloorToInt(inputTimeInSecond % 60));
            return sb.ToString();
        }

        public static string FormatToMMSS(this double inputTimeInSecond)
        {
            if (inputTimeInSecond < 0) return "00:00";
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("{0:D2}", Mathf.FloorToInt((float)(inputTimeInSecond / 60)));
            sb.Append(":");
            sb.AppendFormat("{0:D2}", Mathf.FloorToInt((float)(inputTimeInSecond % 60)));
            return sb.ToString();
        }

        public static IEnumerator WaitForSeconds(float seconds, System.Action onComplete)
        {
            yield return new WaitForSeconds(seconds);
            onComplete?.Invoke();
        }
    }


}
