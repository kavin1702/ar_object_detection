using CADInspection.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CADInspection.CadQr
{
    /// <summary>
    /// Builds the on-screen buttons and text in code (no prefab to wire up), then each frame copies
    /// the controller's state into the labels. The HUD never changes AR state itself: every button
    /// just calls a public method on <see cref="CadQrOverlayController"/>.
    ///
    /// Put it on the same "CadQrApp" GameObject as the controller. The scene needs an EventSystem
    /// (the editor tool adds one) so that taps reach the buttons.
    /// </summary>
    public class CadQrHUD : MonoBehaviour
    {
        [SerializeField] CadQrOverlayController m_Controller;

        static readonly Color k_Panel = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color k_Button = new Color(0.16f, 0.18f, 0.22f, 0.92f);
        static readonly Color k_Accent = new Color(0.05f, 0.65f, 0.3f, 0.95f);
        static readonly Color k_Locked = new Color(0.85f, 0.25f, 0.2f, 0.95f);

        Font m_Font;
        Text m_Status, m_Info, m_ModelName, m_LockLabel, m_GhostLabel, m_QrCaption, m_SavedPath;
        Image m_LockImage;
        Button m_LockButton, m_RotateButton;
        GameObject m_QrPanel;
        RawImage m_QrImage;

        void Awake()
        {
            if (m_Controller == null) m_Controller = GetComponent<CadQrOverlayController>();
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUi();
        }

        void Update()
        {
            if (m_Controller == null) return;

            m_Status.text = m_Controller.StatusMessage;

            CadModelEntry entry = m_Controller.SelectedEntry;
            CadModelInfo info = m_Controller.SelectedInfo;
            m_ModelName.text = entry != null ? entry.displayName : "(no model)";
            m_Info.text = info != null
                ? $"From CAD:  W {info.sizeMm.x:0.#}  x  H {info.sizeMm.y:0.#}  x  D {info.sizeMm.z:0.#} mm\n" +
                  $"{info.featureEdges.Count / 2} wire edges, {info.triangleCount} triangles, card {entry.markerWidthMeters * 1000f:0} mm, turn {m_Controller.MarkerYawDegrees:0} deg"
                : "";

            bool locked = m_Controller.IsLocked;
            m_LockLabel.text = locked ? "UNLOCK" : "LOCK & INSPECT";
            m_LockImage.color = locked ? k_Locked : k_Accent;
            m_LockButton.interactable = locked || m_Controller.CanLock;
            m_RotateButton.interactable = !locked;
            m_GhostLabel.text = m_Controller.GhostVisible ? "Ghost: ON" : "Ghost: OFF";
        }

        // ------------------------------------------------------------------
        // Button actions
        // ------------------------------------------------------------------

        void ShowQr()
        {
            CadModelEntry entry = m_Controller.SelectedEntry;
            if (entry == null) return;
            m_QrImage.texture = m_Controller.GetMarkerTexture(entry.id);
            m_QrCaption.text = $"{entry.displayName}\nPrint this card at exactly {entry.markerWidthMeters * 1000f:0} mm wide (outer black frame).\n" +
                               $"Stick it {Describe(entry.markerPlacement)}, arrow pointing {(entry.markerPlacement == MarkerPlacement.FrontCentre ? "up" : "to the back")}.";
            m_SavedPath.text = "";
            m_QrPanel.SetActive(true);
        }

        void SavePng()
        {
            string path = m_Controller.SaveSelectedMarkerPng();
            m_SavedPath.text = path != null ? "Saved: " + path : "Nothing to save";
        }

        static string Describe(MarkerPlacement p) => p switch
        {
            MarkerPlacement.FrontCentre => "in the centre of the FRONT face",
            MarkerPlacement.Custom => "at the custom position set for this model",
            _ => "in the centre of the TOP face"
        };

        // ------------------------------------------------------------------
        // UI construction (plain uGUI, portrait 1080 x 1920 reference)
        // ------------------------------------------------------------------

        void BuildUi()
        {
            var canvasGo = new GameObject("CadQrHUD_Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            RectTransform safe = NewRect("SafeArea", canvasGo.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // Top panel: status + numbers from the CAD.
            RectTransform top = Panel("TopPanel", safe, k_Panel);
            top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(0.5f, 1);
            top.sizeDelta = new Vector2(0, 300); top.anchoredPosition = Vector2.zero;
            Label(top, "CAD QR Overlay", 44, FontStyle.Bold, new Vector2(30, -20), new Vector2(-30, -80), TextAnchor.UpperLeft);
            m_Status = Label(top, "", 34, FontStyle.Normal, new Vector2(30, -85), new Vector2(-30, -190), TextAnchor.UpperLeft);
            m_Info = Label(top, "", 28, FontStyle.Normal, new Vector2(30, -195), new Vector2(-30, -290), TextAnchor.UpperLeft);
            m_Info.color = new Color(1f, 0.9f, 0.4f);

            // Bottom panel: three rows of buttons.
            RectTransform bottom = Panel("BottomPanel", safe, k_Panel);
            bottom.anchorMin = new Vector2(0, 0); bottom.anchorMax = new Vector2(1, 0); bottom.pivot = new Vector2(0.5f, 0);
            bottom.sizeDelta = new Vector2(0, 520); bottom.anchoredPosition = Vector2.zero;
            var column = bottom.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(24, 24, 24, 24);
            column.spacing = 18;
            column.childForceExpandHeight = true;
            column.childControlHeight = true;
            column.childControlWidth = true;

            RectTransform rowModel = Row(bottom);
            AddButton(rowModel, "<", () => m_Controller.PreviousModel(), 0.2f);
            m_ModelName = AddButton(rowModel, "", ShowQr, 1f).GetComponentInChildren<Text>();
            AddButton(rowModel, ">", () => m_Controller.NextModel(), 0.2f);

            RectTransform rowTools = Row(bottom);
            AddButton(rowTools, "Show QR card", ShowQr, 1f);
            m_RotateButton = AddButton(rowTools, "Turn 90", () => m_Controller.RotateAroundCard(), 0.7f);
            m_GhostLabel = AddButton(rowTools, "Ghost", () => m_Controller.ToggleGhost(), 0.8f).GetComponentInChildren<Text>();

            RectTransform rowLock = Row(bottom);
            m_LockButton = AddButton(rowLock, "LOCK & INSPECT", () => m_Controller.ToggleLock(), 1f);
            m_LockImage = m_LockButton.GetComponent<Image>();
            m_LockLabel = m_LockButton.GetComponentInChildren<Text>();
            m_LockLabel.fontSize = 46;
            m_LockLabel.fontStyle = FontStyle.Bold;

            BuildQrPanel(safe);
        }

        void BuildQrPanel(RectTransform parent)
        {
            RectTransform panel = Panel("QrPanel", parent, new Color(0.05f, 0.05f, 0.07f, 0.96f));
            Stretch(panel);
            m_QrPanel = panel.gameObject;

            var imageRect = NewRect("QrImage", panel);
            imageRect.anchorMin = imageRect.anchorMax = new Vector2(0.5f, 0.62f);
            imageRect.sizeDelta = new Vector2(900, 900);
            m_QrImage = imageRect.gameObject.AddComponent<RawImage>();

            m_QrCaption = Label(panel, "", 32, FontStyle.Normal, Vector2.zero, Vector2.zero, TextAnchor.UpperCenter);
            RectTransform cap = m_QrCaption.rectTransform;
            cap.anchorMin = new Vector2(0, 0.25f); cap.anchorMax = new Vector2(1, 0.37f);
            cap.offsetMin = new Vector2(40, 0); cap.offsetMax = new Vector2(-40, 0);

            m_SavedPath = Label(panel, "", 24, FontStyle.Italic, Vector2.zero, Vector2.zero, TextAnchor.UpperCenter);
            RectTransform sp = m_SavedPath.rectTransform;
            sp.anchorMin = new Vector2(0, 0.17f); sp.anchorMax = new Vector2(1, 0.24f);
            sp.offsetMin = new Vector2(40, 0); sp.offsetMax = new Vector2(-40, 0);

            RectTransform row = NewRect("Buttons", panel);
            row.anchorMin = new Vector2(0, 0.04f); row.anchorMax = new Vector2(1, 0.14f);
            row.offsetMin = new Vector2(40, 0); row.offsetMax = new Vector2(-40, 0);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 24; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandHeight = true;
            AddButton(row, "Save PNG", SavePng, 1f);
            AddButton(row, "Close", () => m_QrPanel.SetActive(false), 1f);

            m_QrPanel.SetActive(false);
        }

        RectTransform Row(RectTransform parent)
        {
            RectTransform row = NewRect("Row", parent);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 18;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            return row;
        }

        Button AddButton(RectTransform parent, string text, UnityEngine.Events.UnityAction onClick, float flexibleWidth)
        {
            RectTransform rect = NewRect("Button_" + text, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = k_Button;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = flexibleWidth;
            layout.minHeight = 110;

            Text label = Label(rect, text, 36, FontStyle.Bold, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return button;
        }

        Text Label(RectTransform parent, string text, int size, FontStyle style, Vector2 offsetTopLeft, Vector2 offsetBottomRight, TextAnchor anchor)
        {
            RectTransform rect = NewRect("Text", parent);
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = new Vector2(offsetTopLeft.x, offsetBottomRight.y);
            rect.offsetMax = new Vector2(offsetBottomRight.x, offsetTopLeft.y);
            var t = rect.gameObject.AddComponent<Text>();
            t.font = m_Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }

        static RectTransform Panel(string name, Transform parent, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        }
    }
}
