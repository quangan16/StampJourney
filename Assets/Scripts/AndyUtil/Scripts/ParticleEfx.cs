using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AndyUtil
{
    public static class ParticleEfx
    {
        public static void PlayEfx(this ParticleSystem ps, bool shouldPlayInWorldSpace = false, bool shouldDestroyOnFinish = false)
        {
            if (shouldPlayInWorldSpace)
            {
                ps.transform.SetParent(null);
            }
            ps.Play();

            if (shouldDestroyOnFinish)
            {
                DestroyParticleWhenFinished(ps).Forget();
            }
        }

        private static async UniTaskVoid DestroyParticleWhenFinished(ParticleSystem ps)
        {
            if (ps == null) return;

            // Wait until the particle system is no longer playing
            while (ps != null && ps.isPlaying)
            {
                await UniTask.Yield();
            }

            // Destroy the GameObject (which contains the ParticleSystem)
            if (ps != null)
            {
                Object.Destroy(ps.gameObject);
            }
        }
    }
}