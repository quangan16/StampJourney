using DG.Tweening;
using UnityEngine;

namespace AndyUtil
{
    public static class TweenHelper
    {
        public static Sequence Add(this Sequence sequence, Tween tween, AddType addType)
        {
            if (sequence == null || tween == null) return sequence;
            switch (addType)
            {
                case AddType.Append:
                    sequence.Append(tween);
                    break;
                case AddType.Join:
                    sequence.Join(tween);
                    break;
                default:
                    Logger.LogWarning("Invalid TweenAddType specified.");
                    break;
            }

            return sequence;
        }

        public static Sequence Add(this Sequence sequence, TweenCallback callback, AddType addType)
        {
            if (sequence == null || callback == null) return sequence;
            switch (addType)
            {
                case AddType.Append:
                    sequence.AppendCallback(callback);
                    break;
                case AddType.Join:
                    sequence.JoinCallback(callback);
                    break;
                default:
                    Logger.LogWarning("Invalid TweenAddType specified.");
                    break;
            }

            return sequence;
        }

        public enum AddType
        {
            Append,
            Join
        }
    }
}
