using System;
using TMPro;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameLab.Week4
{
    /// <summary>엔딩 Timeline이 끝나면 감사 문구와 이번 플레이 통계를 화면에 표시한다.</summary>
    public sealed class EndingStatisticsPresenter : MonoBehaviour
    {
        [SerializeField] private PlayableDirector playableDirector;
        [SerializeField] private TMP_FontAsset fontAsset;
        [Tooltip("Timeline 종료보다 통계 UI를 몇 초 먼저 표시할지 설정합니다. 값이 클수록 빨리 표시됩니다.")]
        [Min(0f)] [SerializeField] private float statisticsDisplayLeadTime = 4.5f;

        private GameObject statisticsCanvas;
        private TextMeshProUGUI statisticsText;
        private bool hasPresented;

        private void Awake()
        {
            if (playableDirector == null) playableDirector = GetComponent<PlayableDirector>();
            BuildInterface();
        }

        private void OnEnable()
        {
            if (playableDirector != null) playableDirector.stopped += HandleTimelineStopped;
        }

        private void OnDisable()
        {
            if (playableDirector != null) playableDirector.stopped -= HandleTimelineStopped;
        }

        private void Update()
        {
            if (hasPresented || playableDirector == null || playableDirector.duration <= 0d) return;

            // CLEAR 연출 뒤의 대기 시간을 Inspector에서 줄일 수 있도록 Timeline 끝보다 일찍 표시한다.
            double presentationTime = Math.Max(
                0d,
                playableDirector.duration - Math.Max(0f, statisticsDisplayLeadTime));
            if (playableDirector.time >= presentationTime - 0.02d)
            {
                PresentStatistics();
            }
        }

        private void HandleTimelineStopped(PlayableDirector director)
        {
            if (director == playableDirector) PresentStatistics();
        }

        private void PresentStatistics()
        {
            if (hasPresented || statisticsCanvas == null || statisticsText == null) return;

            hasPresented = true;
            GameplayStatistics.Snapshot snapshot = GameplayStatistics.GetSnapshot();
            statisticsText.text =
                $"1. 회전한 횟수    {snapshot.Rotations:N0}회\n" +
                $"2. 되돌린 횟수    {snapshot.Undos:N0}회\n" +
                $"3. 다시 시작한 횟수    {snapshot.Restarts:N0}회\n" +
                $"4. 클리어까지 걸린 시간    {FormatElapsedTime(snapshot.ElapsedSeconds)}";
            statisticsCanvas.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void LoadMainMenu()
        {
            SoundManager.Play(SoundEventId.UiClick);
            SceneManager.LoadScene("Opening_Title");
        }

        private void QuitGame()
        {
            SoundManager.Play(SoundEventId.UiClick);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static string FormatElapsedTime(double elapsedSeconds)
        {
            TimeSpan elapsed = TimeSpan.FromSeconds(Math.Max(0d, elapsedSeconds));
            if (elapsed.TotalHours >= 1d)
            {
                return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            }

            return $"{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds / 10:00}";
        }

        private void BuildInterface()
        {
            statisticsCanvas = new GameObject(
                "EndingStatisticsUI",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            statisticsCanvas.layer = LayerMask.NameToLayer("UI");

            Canvas canvas = statisticsCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 1;

            CanvasScaler scaler = statisticsCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Image backdrop = CreateImage("Backdrop", statisticsCanvas.transform, new Color(0.01f, 0.012f, 0.018f, 0.94f));
            StretchToParent(backdrop.rectTransform);

            Image panel = CreateImage("StatisticsPanel", statisticsCanvas.transform, new Color(0.08f, 0.07f, 0.065f, 0.97f));
            SetCenteredRect(panel.rectTransform, Vector2.zero, new Vector2(960f, 650f));
            Outline outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.72f, 0.28f, 0.65f);
            outline.effectDistance = new Vector2(3f, -3f);

            TextMeshProUGUI title = CreateText("ThankYou", panel.transform, 54f, FontStyles.Bold);
            title.text = "당신의 열정에 감사드립니다!";
            title.color = new Color(1f, 0.82f, 0.48f, 1f);
            SetCenteredRect(title.rectTransform, new Vector2(0f, 215f), new Vector2(870f, 105f));

            Image divider = CreateImage("Divider", panel.transform, new Color(1f, 0.72f, 0.28f, 0.75f));
            SetCenteredRect(divider.rectTransform, new Vector2(0f, 135f), new Vector2(760f, 3f));

            statisticsText = CreateText("Statistics", panel.transform, 37f, FontStyles.Normal);
            statisticsText.color = new Color(0.96f, 0.95f, 0.9f, 1f);
            statisticsText.lineSpacing = 24f;
            SetCenteredRect(statisticsText.rectTransform, new Vector2(0f, -65f), new Vector2(790f, 360f));

            Button mainMenuButton = CreateButton("MainMenuButton", panel.transform, "메인 메뉴");
            SetCenteredRect(mainMenuButton.GetComponent<RectTransform>(), new Vector2(-205f, -270f), new Vector2(340f, 78f));
            mainMenuButton.onClick.AddListener(LoadMainMenu);

            Button quitButton = CreateButton("QuitButton", panel.transform, "게임 종료");
            SetCenteredRect(quitButton.GetComponent<RectTransform>(), new Vector2(205f, -270f), new Vector2(340f, 78f));
            quitButton.onClick.AddListener(QuitGame);

            statisticsCanvas.SetActive(false);
        }

        private TextMeshProUGUI CreateText(
            string objectName,
            Transform parent,
            float fontSize,
            FontStyles style)
        {
            GameObject textObject = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.layer = LayerMask.NameToLayer("UI");
            textObject.transform.SetParent(parent, false);

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            if (fontAsset != null) text.font = fontAsset;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static Image CreateImage(string objectName, Transform parent, Color color)
        {
            GameObject imageObject = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.layer = LayerMask.NameToLayer("UI");
            imageObject.transform.SetParent(parent, false);

            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Button CreateButton(string objectName, Transform parent, string label)
        {
            GameObject buttonObject = new(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.layer = LayerMask.NameToLayer("UI");
            buttonObject.transform.SetParent(parent, false);

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.26f, 0.18f, 0.09f, 1f);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.83f, 0.5f, 1f);
            colors.pressedColor = new Color(0.82f, 0.58f, 0.24f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            TextMeshProUGUI buttonLabel = CreateText("Label", buttonObject.transform, 32f, FontStyles.Bold);
            buttonLabel.text = label;
            buttonLabel.color = new Color(1f, 0.94f, 0.82f, 1f);
            StretchToParent(buttonLabel.rectTransform);
            return button;
        }

        private static void StretchToParent(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private static void SetCenteredRect(RectTransform rectTransform, Vector2 position, Vector2 size)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = position;
            rectTransform.sizeDelta = size;
        }
    }
}
