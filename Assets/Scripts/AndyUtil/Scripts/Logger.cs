namespace AndyUtil
{
    public static class Logger
    {
        public static bool isLogEnabled = false;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void SetLogConfig()
        {
#if UNITY_EDITOR
            isLogEnabled = true;
#else
            isLogEnabled = false;
#endif
        }

        public static void Log(string msg, params object[] attrs)
        {
            if (!isLogEnabled)
                return;
            UnityEngine.Debug.Log(string.Format(msg, attrs));
        }

        public static void LogError(string msg, params object[] attrs)
        {
            if (!isLogEnabled)
                return;
            UnityEngine.Debug.LogError(string.Format(msg, attrs));
        }

        public static void LogWarning(string msg, params object[] attrs)
        {
            if (!isLogEnabled)
                return;
            UnityEngine.Debug.LogWarning(string.Format(msg, attrs));
        }
    }
}