using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StampJourney.UI
{
    /// <summary>
    /// Quản lý toàn bộ UI trong game: HUD, win screen, lose screen, combo popup.
    /// </summary>
    public class UIManager : SingletonMonoBehaviour<UIManager>
    {
        public IScreen currentActiveScreen;
        protected override void OnSingletonInitialized()
        {

        }

        public void ShowToast(string message)
        {
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;
            
            var toastGo = new GameObject("Toast");
            toastGo.transform.SetParent(canvas.transform, false);
            
            var rt = toastGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.2f);
            rt.anchorMax = new Vector2(0.5f, 0.2f);
            rt.sizeDelta = new Vector2(400, 100);
            
            var bg = toastGo.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.8f);
            
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(toastGo.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = message;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.fontSize = 36;
            
            var seq = DOTween.Sequence();
            seq.Append(rt.DOAnchorPosY(100, 0.3f).SetRelative(true).SetEase(Ease.OutQuad));
            seq.AppendInterval(1.5f);
            seq.Append(bg.DOFade(0, 0.3f));
            seq.Join(tmp.DOFade(0, 0.3f));
            seq.OnComplete(() => Destroy(toastGo));
        }
    }
}
