using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SP2FuselageSplitter
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class FuselageSplitterPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "codex.sp2.fuselagesplitter";
        public const string PluginName = "SP2 Fuselage Splitter";
        public const string PluginVersion = "0.3.5";

        private const string DesignerTypeName = "Assets.Scripts.Design.Designer";

        private ConfigEntry<int> _pieces;
        private ConfigEntry<bool> _deleteOriginal;
        private string _status = "select a fuselage part";

        private bool _injected;
        private bool _panelVisible;
        private float _injectTimer;
        private int _injectAttempts;
        private bool _injectDiagnosticsLogged;
        private GameObject _splitterSection;
        private Button _toggleButton;
        private TextMeshProUGUI _toggleLabel;
        private TextMeshProUGUI _statusLabel;
        private TextMeshProUGUI _piecesLabel;
        private TextMeshProUGUI _deleteLabel;
        private TMP_FontAsset _font;
        private Material _fontMaterial;
        private float _fontSize = 14f;

        private void Awake()
        {
            _pieces = Config.Bind("Split", "Pieces", 3, "Number of longitudinal pieces to create.");
            _deleteOriginal = Config.Bind("Split", "DeleteOriginal", false, "Deletes the selected source part after creating the split pieces.");
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded. The Splitter button is injected beside Edit Fuselage Shape.");
        }

        private void Update()
        {
            if (_injected && (_toggleButton == null || _splitterSection == null))
            {
                ResetInjectedUi();
            }
            else if (_injected && _toggleLabel != null && _toggleLabel.text != "Fuselage Splitter")
            {
                // Some native widget components refresh their cloned label. Keep our replacement stable.
                _toggleLabel.text = "Fuselage Splitter";
            }

            if (!_injected)
            {
                _injectTimer += Time.unscaledDeltaTime;
                if (_injectTimer >= 0.5f)
                {
                    _injectTimer = 0f;
                    TryInjectUi();
                }
            }

        }

        private void TryInjectUi()
        {
            GameObject flyout = FindFuselagePartPropertiesFlyout();
            if (flyout == null)
            {
                _injectAttempts++;
                if (_injectAttempts >= 8 && !_injectDiagnosticsLogged)
                {
                    _injectDiagnosticsLogged = true;
                    LogFlyoutCandidates();
                }
                return;
            }

            ScrollRect scroll = FindInChildren<ScrollRect>(flyout.transform);
            Button template = FindButtonTemplate(flyout);
            if (template == null)
            {
                Logger.LogWarning("Found flyout-fuselage-shape, but no labeled native button was available to clone.");
                return;
            }

            CaptureNativeTextStyle(flyout, template);
            DestroyExistingInjectedUi(flyout);

            GameObject toggleObject = CloneFuselageShapeButton(template);
            _toggleButton = toggleObject.GetComponent<Button>();
            if (_toggleButton == null)
            {
                Destroy(toggleObject);
                return;
            }

            _toggleButton.onClick.RemoveAllListeners();
            _toggleButton.onClick.AddListener(ToggleInjectedPanel);
            toggleObject.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

            bool dockedOverlay = scroll == null || scroll.content == null;
            // The splitter button behaves as an accordion header. Its section belongs directly
            // beneath it in the same native layout, not at the bottom of the overall scroll view.
            Transform panelParent = toggleObject.transform.parent;
            dockedOverlay = false;
            BuildInjectedPanel(panelParent, template, dockedOverlay);
            _splitterSection.transform.SetSiblingIndex(toggleObject.transform.GetSiblingIndex() + 1);
            _panelVisible = false;
            _splitterSection.SetActive(false);
            _injected = true;
            _injectAttempts = 0;
            _injectDiagnosticsLogged = false;
            Logger.LogInfo("Injected Fuselage Splitter beside Edit Fuselage Shape (" +
                           (dockedOverlay ? "bottom dock" : "scroll content") + ").");
        }

        private void BuildInjectedPanel(Transform parent, Button template, bool dockedOverlay)
        {
            _splitterSection = new GameObject("FuselageSplitterSection");
            _splitterSection.transform.SetParent(parent, false);
            RectTransform rect = _splitterSection.AddComponent<RectTransform>();
            if (dockedOverlay)
            {
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 10f);
                rect.sizeDelta = new Vector2(-16f, 272f);
            }
            else
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = Vector2.zero;
            }

            VerticalLayoutGroup layout = _splitterSection.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            if (!dockedOverlay)
            {
                _splitterSection.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            _splitterSection.transform.SetAsLastSibling();

            TextMeshProUGUI heading = MakeText("FUSELAGE SPLITTER", _splitterSection.transform, _fontSize * 0.8f);
            heading.alignment = TextAlignmentOptions.Center;

            _statusLabel = MakeText(_status, _splitterSection.transform, _fontSize * 0.72f);
            _statusLabel.alignment = TextAlignmentOptions.Center;
            _statusLabel.color = new Color(0.7f, 0.84f, 0.95f, 1f);
            LayoutElement statusLayout = _statusLabel.gameObject.AddComponent<LayoutElement>();
            statusLayout.preferredHeight = 36f;
            statusLayout.flexibleWidth = 1f;

            GameObject piecesRow = MakeRow("PiecesRow", _splitterSection.transform, 32f);
            TextMeshProUGUI piecesCaption = MakeText("Pieces", piecesRow.transform, _fontSize * 0.8f);
            piecesCaption.alignment = TextAlignmentOptions.Left;
            piecesCaption.gameObject.AddComponent<LayoutElement>().preferredWidth = 88f;

            GameObject decrease = CloneNativeButton(template, "PiecesDecrease", "◄", piecesRow.transform);
            SetButtonLayout(decrease, 34f, false);
            decrease.GetComponent<Button>().onClick.RemoveAllListeners();
            decrease.GetComponent<Button>().onClick.AddListener(() => ChangePieceCount(-1));

            _piecesLabel = MakeText(FormatPieceCount(), piecesRow.transform, _fontSize * 0.85f);
            _piecesLabel.alignment = TextAlignmentOptions.Center;
            _piecesLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            GameObject increase = CloneNativeButton(template, "PiecesIncrease", "►", piecesRow.transform);
            SetButtonLayout(increase, 34f, false);
            increase.GetComponent<Button>().onClick.RemoveAllListeners();
            increase.GetComponent<Button>().onClick.AddListener(() => ChangePieceCount(1));

            GameObject deleteToggle = CloneNativeButton(template, "DeleteOriginalButton", FormatDeleteOriginal(), _splitterSection.transform);
            SetButtonLayout(deleteToggle, 180f, true);
            _deleteLabel = deleteToggle.GetComponentInChildren<TextMeshProUGUI>(true);
            deleteToggle.GetComponent<Button>().onClick.RemoveAllListeners();
            deleteToggle.GetComponent<Button>().onClick.AddListener(ToggleDeleteOriginal);

            GameObject actions = MakeRow("SplitterActions", _splitterSection.transform, 36f);
            GameObject splitButton = CloneNativeButton(template, "SplitSelectedButton", "Split Selected", actions.transform);
            SetButtonLayout(splitButton, 150f, true);
            splitButton.GetComponent<Button>().onClick.RemoveAllListeners();
            splitButton.GetComponent<Button>().onClick.AddListener(TrySplitSelected);

            GameObject dumpButton = CloneNativeButton(template, "DumpSelectedXmlButton", "Dump XML", actions.transform);
            SetButtonLayout(dumpButton, 95f, true);
            dumpButton.GetComponent<Button>().onClick.RemoveAllListeners();
            dumpButton.GetComponent<Button>().onClick.AddListener(DumpSelectedXml);

            TextMeshProUGUI hint = MakeText("Supports modern and legacy fuselage blocks. Cone style is not supported yet.", _splitterSection.transform, _fontSize * 0.66f);
            hint.alignment = TextAlignmentOptions.Center;
            hint.color = new Color(0.63f, 0.68f, 0.76f, 1f);
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
        }

        private void ToggleInjectedPanel()
        {
            _panelVisible = !_panelVisible;
            if (_splitterSection != null)
            {
                _splitterSection.SetActive(_panelVisible);
            }
        }

        private void ChangePieceCount(int delta)
        {
            _pieces.Value = Mathf.Clamp(_pieces.Value + delta, 2, 32);
            Config.Save();
            if (_piecesLabel != null)
            {
                _piecesLabel.text = FormatPieceCount();
            }
        }

        private void ToggleDeleteOriginal()
        {
            _deleteOriginal.Value = !_deleteOriginal.Value;
            Config.Save();
            if (_deleteLabel != null)
            {
                _deleteLabel.text = FormatDeleteOriginal();
            }
        }

        private string FormatPieceCount()
        {
            return Mathf.Clamp(_pieces.Value, 2, 32).ToString(CultureInfo.InvariantCulture);
        }

        private string FormatDeleteOriginal()
        {
            return "Delete original: " + (_deleteOriginal.Value ? "ON" : "OFF");
        }

        private GameObject CloneNativeButton(Button source, string name, string label, Transform parent)
        {
            GameObject clone = Instantiate(source.gameObject, parent);
            clone.name = name;
            clone.SetActive(true);

            Button button = clone.GetComponent<Button>();
            if (button != null)
            {
                button.interactable = true;
            }

            CanvasGroup group = clone.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }

            TextMeshProUGUI text = clone.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null)
            {
                text.text = label;
                text.enabled = true;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.enableAutoSizing = true;
                text.fontSizeMin = 7f;
                text.fontSizeMax = Mathf.Max(10f, text.fontSize);
                if (_font != null)
                {
                    text.font = _font;
                    text.fontSharedMaterial = _fontMaterial;
                }
                Color color = text.color;
                color.a = 1f;
                text.color = color;
            }
            return clone;
        }

        private GameObject CloneFuselageShapeButton(Button source)
        {
            GameObject clone = Instantiate(source.gameObject, source.transform.parent);
            clone.name = "FuselageSplitterButton";
            clone.SetActive(true);

            Button button = clone.GetComponent<Button>();
            if (button != null)
            {
                button.interactable = true;
            }

            CanvasGroup group = clone.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }

            TextMeshProUGUI[] sourceLabels = source.GetComponentsInChildren<TextMeshProUGUI>(true);
            TextMeshProUGUI[] cloneLabels = clone.GetComponentsInChildren<TextMeshProUGUI>(true);
            int labelIndex = -1;
            for (int i = 0; i < sourceLabels.Length; i++)
            {
                if (sourceLabels[i] != null &&
                    string.Equals(sourceLabels[i].text.Trim(), "Edit Fuselage Shape", StringComparison.OrdinalIgnoreCase))
                {
                    labelIndex = i;
                    break;
                }
            }
            if (labelIndex >= 0 && labelIndex < cloneLabels.Length)
            {
                _toggleLabel = cloneLabels[labelIndex];
            }
            else if (cloneLabels.Length > 0)
            {
                _toggleLabel = cloneLabels[0];
            }
            if (_toggleLabel != null)
            {
                // Change only the string. Font, material, size, alignment and margins remain native.
                _toggleLabel.text = "Fuselage Splitter";
                _toggleLabel.enabled = true;
            }

            RectTransform sourceRect = source.GetComponent<RectTransform>();
            RectTransform cloneRect = clone.GetComponent<RectTransform>();
            float nativeHeight = sourceRect != null ? sourceRect.rect.height : 0f;
            if (nativeHeight < 36f)
            {
                nativeHeight = 50f;
            }
            if (sourceRect != null && cloneRect != null)
            {
                cloneRect.anchorMin = sourceRect.anchorMin;
                cloneRect.anchorMax = sourceRect.anchorMax;
                cloneRect.pivot = sourceRect.pivot;
                cloneRect.sizeDelta = new Vector2(sourceRect.sizeDelta.x, nativeHeight);
            }

            LayoutElement sourceLayout = source.GetComponent<LayoutElement>();
            LayoutElement cloneLayout = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
            cloneLayout.ignoreLayout = false;
            cloneLayout.minWidth = sourceLayout != null ? sourceLayout.minWidth : 0f;
            cloneLayout.preferredWidth = sourceLayout != null ? sourceLayout.preferredWidth : -1f;
            cloneLayout.flexibleWidth = sourceLayout != null ? sourceLayout.flexibleWidth : 1f;
            cloneLayout.minHeight = nativeHeight;
            cloneLayout.preferredHeight = nativeHeight;
            cloneLayout.flexibleHeight = 0f;
            return clone;
        }

        private TextMeshProUGUI MakeText(string value, Transform parent, float size)
        {
            GameObject textObject = new GameObject("SplitterText");
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = Color.white;
            text.raycastTarget = false;
            if (_font != null)
            {
                text.font = _font;
                text.fontSharedMaterial = _fontMaterial;
            }
            return text;
        }

        private static GameObject MakeRow(string name, Transform parent, float height)
        {
            GameObject row = new GameObject(name);
            row.transform.SetParent(parent, false);
            row.AddComponent<RectTransform>();
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.spacing = 6f;
            LayoutElement element = row.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.flexibleWidth = 1f;
            return row;
        }

        private static void SetButtonLayout(GameObject button, float preferredWidth, bool flexible)
        {
            LayoutElement element = button.GetComponent<LayoutElement>() ?? button.AddComponent<LayoutElement>();
            element.preferredWidth = preferredWidth;
            element.minWidth = Mathf.Min(56f, preferredWidth);
            element.preferredHeight = 30f;
            element.flexibleWidth = flexible ? 1f : 0f;
        }

        private void CaptureNativeTextStyle(GameObject flyout, Button template)
        {
            TextMeshProUGUI[] labels = flyout.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] == null || labels[i].font == null)
                {
                    continue;
                }
                _font = labels[i].font;
                _fontMaterial = labels[i].fontSharedMaterial;
                _fontSize = labels[i].fontSize > 0f ? labels[i].fontSize : 14f;
                return;
            }

            TextMeshProUGUI fallback = template.GetComponentInChildren<TextMeshProUGUI>(true);
            if (fallback != null)
            {
                _font = fallback.font;
                _fontMaterial = fallback.fontSharedMaterial;
                _fontSize = fallback.fontSize;
            }
        }

        private static Button FindButtonTemplate(GameObject flyout)
        {
            Button[] buttons = flyout.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                TextMeshProUGUI label = buttons[i].GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null && string.Equals(label.text.Trim(), "Edit Fuselage Shape", StringComparison.OrdinalIgnoreCase))
                {
                    return buttons[i];
                }
            }
            return null;
        }

        private static T FindInChildren<T>(Transform root) where T : Component
        {
            Queue<Transform> pending = new Queue<Transform>();
            pending.Enqueue(root);
            while (pending.Count > 0)
            {
                Transform current = pending.Dequeue();
                T component = current.GetComponent<T>();
                if (component != null)
                {
                    return component;
                }
                for (int i = 0; i < current.childCount; i++)
                {
                    pending.Enqueue(current.GetChild(i));
                }
            }
            return null;
        }

        private static GameObject FindFuselagePartPropertiesFlyout()
        {
            const string assetName = "flyout-part-properties";
            GameObject direct = GameObject.Find(assetName);
            if (direct != null)
            {
                return direct;
            }

            GameObject[] all = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name.IndexOf(assetName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return all[i];
                }
            }

            TextMeshProUGUI[] labels = UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None);
            for (int i = 0; i < labels.Length; i++)
            {
                TextMeshProUGUI label = labels[i];
                if (label == null || string.IsNullOrWhiteSpace(label.text) ||
                    !string.Equals(label.text.Trim(), "Edit Fuselage Shape", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Transform current = label.transform;
                GameObject bestPanel = null;
                for (int depth = 0; current != null && depth < 10; depth++, current = current.parent)
                {
                    if (current.name.IndexOf("flyout", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return current.gameObject;
                    }

                    RectTransform panelRect = current as RectTransform;
                    if (panelRect != null && panelRect.rect.width >= 250f &&
                        current.GetComponentsInChildren<Button>(true).Length >= 2)
                    {
                        bestPanel = current.gameObject;
                    }
                    if (current.GetComponent<Canvas>() != null)
                    {
                        break;
                    }
                }
                if (bestPanel != null)
                {
                    return bestPanel;
                }
            }
            return null;
        }

        private void LogFlyoutCandidates()
        {
            try
            {
                List<string> names = new List<string>();
                GameObject[] all = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null)
                    {
                        continue;
                    }
                    string name = all[i].name ?? "";
                    if (name.IndexOf("flyout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("fuselage", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        names.Add(name);
                    }
                }
                Logger.LogWarning("Could not locate Part Properties with an Edit Fuselage Shape button. Active candidates: " +
                                  string.Join(", ", names.Distinct().Take(40).ToArray()));
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Flyout diagnostics failed: " + ex.Message);
            }
        }

        private static void DestroyExistingInjectedUi(GameObject flyout)
        {
            Transform[] transforms = flyout.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null &&
                    (transforms[i].name == "FuselageSplitterButton" || transforms[i].name == "FuselageSplitterSection"))
                {
                    UnityEngine.Object.Destroy(transforms[i].gameObject);
                }
            }
        }

        private void ResetInjectedUi()
        {
            _injected = false;
            _splitterSection = null;
            _toggleButton = null;
            _toggleLabel = null;
            _statusLabel = null;
            _piecesLabel = null;
            _deleteLabel = null;
            _panelVisible = false;
        }

        private void TrySplitSelected()
        {
            try
            {
                object designer = GetDesignerInstance();
                object selectedPart = GetMemberAny(designer, "SelectedPart", "SelectedPartScript");
                object aircraft = GetMemberAny(designer, "Aircraft");
                object sourcePartData = GetMemberAny(selectedPart, "Part", "PartData", "Data");
                if (designer == null || selectedPart == null || aircraft == null || sourcePartData == null)
                {
                    SetStatus("no selected designer part");
                    return;
                }

                XElement sourceXml = InvokeMember(sourcePartData, "GenerateXml") as XElement;
                if (sourceXml == null)
                {
                    SetStatus("selected part did not generate XML");
                    return;
                }

                int count = Mathf.Clamp(_pieces.Value, 2, 32);
                if (!TryBuildSplitPartXml(sourceXml, sourcePartData, aircraft, count, out List<XElement> splitXml, out string error))
                {
                    SetStatus(error);
                    return;
                }

                object aircraftData = GetMemberAny(aircraft, "Aircraft");
                XElement craftXml = InvokeMember(aircraftData, "GenerateXml", false, false) as XElement;
                if (craftXml == null)
                {
                    SetStatus("could not generate craft XML");
                    return;
                }

                int sourceId = Convert.ToInt32(sourceXml.Attribute("id")?.Value, CultureInfo.InvariantCulture);
                XElement liveSource = FindPartElementById(craftXml, sourceId);
                XElement parts = craftXml.Element("Assembly")?.Element("Parts");
                if (liveSource == null || parts == null)
                {
                    SetStatus("could not find selected part in craft XML");
                    return;
                }

                XElement insertAfter = liveSource;
                if (_deleteOriginal.Value)
                {
                    RemapConnectionsFromSource(craftXml, sourceId, sourceXml, splitXml);
                    RemapPartReferencesFromSource(craftXml, sourceId, splitXml);
                    liveSource.ReplaceWith(splitXml);
                }
                else
                {
                    foreach (XElement part in splitXml)
                    {
                        insertAfter.AddAfterSelf(part);
                        insertAfter = part;
                    }
                }
                AddSplitChainConnections(craftXml, splitXml);
                UpdatePartCount(craftXml, _deleteOriginal.Value ? count - 1 : count);

                CreateUndoStep(designer);
                InvokeMember(designer, "LoadXml", craftXml, false);
                object reloadedAircraft = GetMemberAny(designer, "Aircraft");
                object firstPart = InvokeMember(reloadedAircraft, "GetPartById", Convert.ToInt32(splitXml[0].Attribute("id")?.Value, CultureInfo.InvariantCulture), true);
                object firstPartScript = GetMemberAny(firstPart, "PartScript");
                if (firstPartScript != null)
                {
                    InvokeMember(designer, "SelectPart", firstPartScript);
                }

                string suffix = _deleteOriginal.Value ? " and replaced original" : " as copy";
                SetStatus("created " + count + " split pieces" + suffix);
                InvokeMember(designer, "ShowMessage", "Fuselage split into " + count + " pieces", 1.6f);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Fuselage split failed: " + ex);
                SetStatus("split failed: " + ex.Message);
            }
        }

        private static XElement FindPartElementById(XElement craftXml, int partId)
        {
            return craftXml
                .Element("Assembly")
                ?.Element("Parts")
                ?.Elements("Part")
                .FirstOrDefault(part => string.Equals(
                    (string)part.Attribute("id"),
                    partId.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal));
        }

        private static void AddSplitChainConnections(XElement craftXml, List<XElement> splitXml)
        {
            XElement assembly = craftXml.Element("Assembly");
            if (assembly == null || splitXml.Count < 2)
            {
                return;
            }

            XElement connections = assembly.Element("Connections");
            if (connections == null)
            {
                connections = new XElement("Connections");
                XElement parts = assembly.Element("Parts");
                if (parts != null)
                {
                    parts.AddAfterSelf(connections);
                }
                else
                {
                    assembly.Add(connections);
                }
            }

            for (int i = 0; i < splitXml.Count - 1; i++)
            {
                connections.Add(new XElement(
                    "Connection",
                    new XAttribute("partA", splitXml[i].Attribute("id")?.Value ?? ""),
                    new XAttribute("partB", splitXml[i + 1].Attribute("id")?.Value ?? ""),
                    new XAttribute("attachPointsA", "1"),
                    new XAttribute("attachPointsB", "0")));
            }
        }

        private static void RemapConnectionsFromSource(XElement craftXml, int sourceId, XElement sourceXml, List<XElement> splitXml)
        {
            XElement connections = craftXml.Element("Assembly")?.Element("Connections");
            if (connections == null)
            {
                return;
            }

            string source = sourceId.ToString(CultureInfo.InvariantCulture);
            foreach (XElement connection in connections.Elements("Connection"))
            {
                if (string.Equals((string)connection.Attribute("partA"), source, StringComparison.Ordinal))
                {
                    connection.SetAttributeValue("partA", ChooseReplacementPieceId(
                        (string)connection.Attribute("attachPointsA"),
                        connection,
                        true,
                        craftXml,
                        sourceXml,
                        splitXml));
                }
                if (string.Equals((string)connection.Attribute("partB"), source, StringComparison.Ordinal))
                {
                    connection.SetAttributeValue("partB", ChooseReplacementPieceId(
                        (string)connection.Attribute("attachPointsB"),
                        connection,
                        false,
                        craftXml,
                        sourceXml,
                        splitXml));
                }
            }
        }

        // When the source part is deleted, other parts (notably TextureDecal/TextDecal parts via
        // PartTargeting.State customPartIds) may still reference its id. A dangling id makes
        // DecalData.OnAssemblyLoaded null-deref and the whole craft fails to load. Rewrite every
        // such reference to the full set of split-piece ids so the projection still resolves.
        private static void RemapPartReferencesFromSource(XElement craftXml, int sourceId, List<XElement> splitXml)
        {
            string source = sourceId.ToString(CultureInfo.InvariantCulture);
            string[] replacements = splitXml
                .Select(piece => piece.Attribute("id")?.Value)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToArray();
            if (replacements.Length == 0)
            {
                return;
            }

            foreach (XAttribute attr in craftXml.Descendants().Attributes("customPartIds").ToList())
            {
                string[] tokens = (attr.Value ?? "")
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => t.Length > 0)
                    .ToArray();
                if (!tokens.Any(t => string.Equals(t, source, StringComparison.Ordinal)))
                {
                    continue;
                }

                List<string> rebuilt = new List<string>();
                foreach (string token in tokens)
                {
                    if (string.Equals(token, source, StringComparison.Ordinal))
                    {
                        foreach (string rep in replacements)
                        {
                            if (!rebuilt.Contains(rep))
                            {
                                rebuilt.Add(rep);
                            }
                        }
                    }
                    else if (!rebuilt.Contains(token))
                    {
                        rebuilt.Add(token);
                    }
                }

                attr.Value = string.Join(",", rebuilt.ToArray());
            }
        }

        private static string ChooseReplacementPieceId(
            string attachPoints,
            XElement connection,
            bool sourceIsPartA,
            XElement craftXml,
            XElement sourceXml,
            List<XElement> splitXml)
        {
            string[] points = (attachPoints ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .ToArray();
            XElement firstPiece = splitXml[0];
            XElement lastPiece = splitXml[splitXml.Count - 1];
            bool hasFront = points.Any(p => p == "0");
            bool hasBack = points.Any(p => p == "1");
            XElement chosen;
            if (hasFront && !hasBack)
            {
                chosen = firstPiece;
            }
            else if (hasBack && !hasFront)
            {
                chosen = lastPiece;
            }
            else
            {
                chosen = ChooseNearestPieceByOtherPartPosition(connection, sourceIsPartA, craftXml, sourceXml, splitXml);
            }
            return chosen.Attribute("id")?.Value ?? "";
        }

        private static XElement ChooseNearestPieceByOtherPartPosition(
            XElement connection,
            bool sourceIsPartA,
            XElement craftXml,
            XElement sourceXml,
            List<XElement> splitXml)
        {
            string otherId = (string)connection.Attribute(sourceIsPartA ? "partB" : "partA");
            if (!int.TryParse(otherId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedOtherId))
            {
                return splitXml[0];
            }

            XElement otherPart = FindPartElementById(craftXml, parsedOtherId);
            if (otherPart == null)
            {
                return splitXml[0];
            }

            XElement sourceState = sourceXml.Element("JFuselage.State") ?? sourceXml.Element("Fuselage.State");
            if (sourceState == null)
            {
                return splitXml[0];
            }

            Vector3 sourceCenter = ReadVector3Attribute(sourceXml, "position", Vector3.zero);
            Vector3 otherCenter = ReadVector3Attribute(otherPart, "position", sourceCenter);
            Vector3 offset = ReadVector3Attribute(sourceState, "offset", Vector3.forward);
            float lengthSquared = offset.sqrMagnitude;
            if (lengthSquared < 0.000001f)
            {
                return splitXml[0];
            }

            Vector3 localDelta = Quaternion.Inverse(Quaternion.Euler(ReadVector3Attribute(sourceXml, "rotation", Vector3.zero))) * (otherCenter - sourceCenter);
            float t = Mathf.Clamp01(Vector3.Dot(localDelta + offset * 0.5f, offset) / lengthSquared);
            int index = Mathf.Clamp(Mathf.FloorToInt(t * splitXml.Count), 0, splitXml.Count - 1);
            return splitXml[index];
        }

        private static void UpdatePartCount(XElement craftXml, int delta)
        {
            XElement specs = craftXml.Element("Specifications");
            XAttribute attr = specs?.Attribute("PartCount");
            if (attr == null)
            {
                return;
            }

            if (int.TryParse(attr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                attr.Value = Math.Max(0, count + delta).ToString(CultureInfo.InvariantCulture);
            }
        }

        private bool TryBuildSplitPartXml(
            XElement sourceXml,
            object sourcePartData,
            object aircraft,
            int count,
            out List<XElement> splitXml,
            out string error)
        {
            splitXml = new List<XElement>();
            error = "";

            XElement modernState = sourceXml.Element("JFuselage.State");
            XElement legacyState = sourceXml.Element("Fuselage.State");
            if (modernState == null && legacyState == null)
            {
                error = "selected part is not a supported fuselage";
                return false;
            }

            string style = modernState?.Attribute("style")?.Value ?? "";
            if (string.Equals(style, "Cone", StringComparison.OrdinalIgnoreCase))
            {
                error = "cone fuselage splitting is not supported yet";
                return false;
            }

            Vector3 position = ReadVector3Attribute(sourceXml, "position", Vector3.zero);
            Vector3 rotation = ReadVector3Attribute(sourceXml, "rotation", Vector3.zero);
            XElement state = modernState ?? legacyState;
            Vector3 offset = ReadVector3Attribute(state, "offset", Vector3.forward);
            if (offset.magnitude < 0.001f)
            {
                error = "fuselage offset is too short to split";
                return false;
            }

            int nextId = GetNextPartId(aircraft);
            Quaternion orientation = Quaternion.Euler(rotation);
            Vector3 segmentOffset = offset / count;
            string originalName = Convert.ToString(sourceXml.Attribute("name")?.Value ?? GetMemberAny(sourcePartData, "Name") ?? "Fuselage", CultureInfo.InvariantCulture);

            for (int i = 0; i < count; i++)
            {
                float t0 = i / (float)count;
                float t1 = (i + 1) / (float)count;
                XElement piece = new XElement(sourceXml);
                piece.SetAttributeValue("id", (nextId + i).ToString(CultureInfo.InvariantCulture));
                Vector3 centerShift = offset * (((t0 + t1) * 0.5f) - 0.5f);
                piece.SetAttributeValue("position", FormatVector(position + orientation * centerShift));
                piece.SetAttributeValue("rotation", FormatVector(rotation));
                piece.SetAttributeValue("name", originalName + " split " + (i + 1) + "/" + count);
                piece.SetAttributeValue("symmetryId", null);
                piece.SetAttributeValue("symmetryDisabled", "true");
                piece.SetAttributeValue("drag", null);
                piece.SetAttributeValue("dragArea", null);

                if (modernState != null)
                {
                    ApplyModernSplit(piece.Element("JFuselage.State"), segmentOffset, t0, t1, i == 0, i == count - 1, count);
                }
                else
                {
                    ApplyLegacySplit(piece.Element("Fuselage.State"), segmentOffset, t0, t1, i == 0, i == count - 1, count);
                }

                splitXml.Add(piece);
            }

            return true;
        }

        private static void ApplyModernSplit(
            XElement state,
            Vector3 segmentOffset,
            float t0,
            float t1,
            bool isFirst,
            bool isLast,
            int count)
        {
            XElement sectionA = state.Element("SectionA");
            XElement sectionB = state.Element("SectionB");
            state.SetAttributeValue("offset", FormatVector(segmentOffset));
            state.SetAttributeValue("mass", null);
            DivideAttribute(state, "deadMassKg", count);

            if (sectionA != null && sectionB != null)
            {
                sectionA.ReplaceWith(InterpolateElement(sectionA, sectionB, "SectionA", t0, isFirst, false));
                sectionB.ReplaceWith(InterpolateElement(sectionA, sectionB, "SectionB", t1, false, isLast));
            }
        }

        private static void ApplyLegacySplit(
            XElement state,
            Vector3 segmentOffset,
            float t0,
            float t1,
            bool isFirst,
            bool isLast,
            int count)
        {
            state.SetAttributeValue("offset", FormatVector(segmentOffset));
            state.SetAttributeValue("mass", null);
            DivideAttribute(state, "deadWeight", count);

            SetInterpolatedAttribute(state, "frontScale", "frontScale", "rearScale", t0);
            SetInterpolatedAttribute(state, "rearScale", "frontScale", "rearScale", t1);
            SetInterpolatedAttribute(state, "fillFront", "fillFront", "fillBack", t0);
            SetInterpolatedAttribute(state, "fillBack", "fillFront", "fillBack", t1);

            if (!isFirst && state.Attribute("smoothFront") != null)
            {
                state.SetAttributeValue("smoothFront", "False");
            }
            if (!isLast && state.Attribute("smoothBack") != null)
            {
                state.SetAttributeValue("smoothBack", "False");
            }

            string cornerTypes = (string)state.Attribute("cornerTypes");
            if (TryParseIntList(cornerTypes, out int[] corners) && corners.Length == 8)
            {
                int[] front = corners.Take(4).ToArray();
                int[] rear = corners.Skip(4).Take(4).ToArray();
                int[] split = PickInts(front, rear, t0).Concat(PickInts(front, rear, t1)).ToArray();
                state.SetAttributeValue("cornerTypes", string.Join(",", split.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray()));
            }
        }

        private static XElement InterpolateElement(XElement a, XElement b, string name, float t, bool preserveStartSmoothing, bool preserveEndSmoothing)
        {
            XElement output = new XElement(name);
            HashSet<string> names = new HashSet<string>(a.Attributes().Select(x => x.Name.LocalName));
            foreach (XAttribute attr in b.Attributes())
            {
                names.Add(attr.Name.LocalName);
            }

            foreach (string attrName in names)
            {
                XAttribute aAttr = a.Attribute(attrName);
                XAttribute bAttr = b.Attribute(attrName);
                if (string.Equals(attrName, "smoothing", StringComparison.OrdinalIgnoreCase))
                {
                    if (preserveStartSmoothing && aAttr != null)
                    {
                        output.SetAttributeValue(attrName, aAttr.Value);
                    }
                    else if (preserveEndSmoothing && bAttr != null)
                    {
                        output.SetAttributeValue(attrName, bAttr.Value);
                    }
                    else if (aAttr != null || bAttr != null)
                    {
                        output.SetAttributeValue(attrName, "False");
                    }
                    continue;
                }

                if (aAttr != null && bAttr != null &&
                    TryParseFloatList(aAttr.Value, out float[] av) &&
                    TryParseFloatList(bAttr.Value, out float[] bv) &&
                    av.Length == bv.Length)
                {
                    output.SetAttributeValue(attrName, FormatFloatList(Lerp(av, bv, t)));
                }
                else if (aAttr != null && bAttr != null && aAttr.Value == bAttr.Value)
                {
                    output.SetAttributeValue(attrName, aAttr.Value);
                }
                else
                {
                    XAttribute chosen = t < 0.5f ? aAttr : bAttr;
                    if (chosen != null)
                    {
                        output.SetAttributeValue(attrName, chosen.Value);
                    }
                }
            }

            return output;
        }

        private void ConnectCreatedPieces(List<object> partData)
        {
            if (partData.Count < 2)
            {
                return;
            }

            Type connectionType = FindType("Assets.Scripts.Craft.Parts.PartConnection");
            if (connectionType == null)
            {
                return;
            }

            for (int i = 0; i < partData.Count - 1; i++)
            {
                object a = partData[i];
                object b = partData[i + 1];
                object connection = Activator.CreateInstance(connectionType, a, b);
                object apA = InvokeMember(a, "GetAttachPoint", 1);
                object apB = InvokeMember(b, "GetAttachPoint", 0);
                if (connection == null || apA == null || apB == null)
                {
                    continue;
                }

                InvokeMember(connection, "AddAttachPointA", apA);
                InvokeMember(connection, "AddAttachPointB", apB);
                AddToList(GetMemberAny(a, "PartConnections"), connection);
                AddToList(GetMemberAny(b, "PartConnections"), connection);
                AddToList(GetMemberAny(apA, "PartConnections"), connection);
                AddToList(GetMemberAny(apB, "PartConnections"), connection);
            }
        }

        private void DumpSelectedXml()
        {
            try
            {
                object designer = GetDesignerInstance();
                object selectedPart = GetMemberAny(designer, "SelectedPart", "SelectedPartScript");
                object sourcePartData = GetMemberAny(selectedPart, "Part", "PartData", "Data");
                XElement sourceXml = InvokeMember(sourcePartData, "GenerateXml") as XElement;
                if (sourceXml == null)
                {
                    SetStatus("selected part did not generate XML");
                    return;
                }

                string outDir = Path.Combine(Paths.ConfigPath, "sp_fuselage_splitter_codex");
                Directory.CreateDirectory(outDir);
                string partId = Convert.ToString(sourceXml.Attribute("id")?.Value ?? "unknown", CultureInfo.InvariantCulture);
                string path = Path.Combine(outDir, "selected_part_" + SanitizeFileName(partId) + ".xml");
                File.WriteAllText(path, sourceXml.ToString(SaveOptions.DisableFormatting), Encoding.UTF8);
                SetStatus("wrote " + path);
                Logger.LogInfo("Wrote selected part XML to " + path);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Dump selected XML failed: " + ex);
                SetStatus("dump failed: " + ex.Message);
            }
        }

        private object CreatePartData(XElement partElement, object aircraft)
        {
            Type partDataType = FindType("Assets.Scripts.Craft.Parts.PartData");
            Type loadContextType = FindType("Assets.Scripts.Craft.CraftLoadContext");
            if (partDataType == null || loadContextType == null)
            {
                return null;
            }

            int xmlVersion = ReadAircraftXmlVersion(aircraft);
            object designerLoadContext = GetMemberAny(loadContextType, "Designer") ?? GetMemberAny(loadContextType, "Default");
            return Activator.CreateInstance(partDataType, partElement, xmlVersion, designerLoadContext);
        }

        private object CreatePartCreationInfo()
        {
            Type creationInfoType = FindType("Assets.Scripts.Craft.Parts.PartData+PartCreationInfo");
            if (creationInfoType == null)
            {
                return null;
            }

            object info = Activator.CreateInstance(creationInfoType);
            SetMember(info, "CreateChildren", true);
            SetMember(info, "CreateHingeJoints", true);
            SetMember(info, "CreateRigidBody", false);
            SetMember(info, "EnableWingScript", true);
            SetMember(info, "IsNonFlyableAircraft", false);
            SetMember(info, "IsRigidBodyKinematic", true);
            SetMember(info, "RemoteAircraft", false);
            return info;
        }

        private int ReadAircraftXmlVersion(object aircraft)
        {
            object aircraftData = GetMemberAny(aircraft, "Aircraft");
            object version = GetMemberAny(aircraftData, "XmlVersion");
            try
            {
                return Convert.ToInt32(version, CultureInfo.InvariantCulture);
            }
            catch
            {
                Type aircraftDataType = FindType("Assets.Scripts.Craft.AircraftData");
                object current = GetMemberAny(aircraftDataType, "CurrentXmlVersion");
                try
                {
                    return Convert.ToInt32(current, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return 23;
                }
            }
        }

        private int GetNextPartId(object aircraft)
        {
            int max = 0;
            IEnumerable parts = GetMemberAny(aircraft, "Parts") as IEnumerable;
            if (parts != null)
            {
                foreach (object part in parts)
                {
                    object id = GetMemberAny(part, "Id");
                    try
                    {
                        max = Mathf.Max(max, Convert.ToInt32(id, CultureInfo.InvariantCulture));
                    }
                    catch
                    {
                    }
                }
            }
            return max + 1;
        }

        private void CreateUndoStep(object designer)
        {
            try
            {
                InvokeMember(designer, "CreateUndoStep", "Split fuselage", "codex.sp2.fuselagesplitter.split");
            }
            catch
            {
            }
        }

        private void SetStatus(string message)
        {
            _status = message ?? "";
            if (_statusLabel != null)
            {
                _statusLabel.text = _status;
            }
            Logger.LogInfo(_status);
        }

        private static void SetInterpolatedAttribute(XElement element, string targetName, string startName, string endName, float t)
        {
            XAttribute start = element.Attribute(startName);
            XAttribute end = element.Attribute(endName);
            if (start == null || end == null)
            {
                return;
            }

            if (TryParseFloatList(start.Value, out float[] av) &&
                TryParseFloatList(end.Value, out float[] bv) &&
                av.Length == bv.Length)
            {
                element.SetAttributeValue(targetName, FormatFloatList(Lerp(av, bv, t)));
            }
        }

        private static void DivideAttribute(XElement element, string name, int divisor)
        {
            XAttribute attr = element.Attribute(name);
            if (attr == null)
            {
                return;
            }

            if (float.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                element.SetAttributeValue(name, CleanFloat(value / Mathf.Max(1, divisor)));
            }
        }

        private static Vector3 ReadVector3Attribute(XElement element, string name, Vector3 fallback)
        {
            XAttribute attr = element.Attribute(name);
            if (attr == null || !TryParseFloatList(attr.Value, out float[] values) || values.Length < 3)
            {
                return fallback;
            }

            return new Vector3(values[0], values[1], values[2]);
        }

        private static bool TryParseFloatList(string value, out float[] values)
        {
            values = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string cleaned = value.Trim().Trim('(', ')');
            string[] parts = cleaned.Split(',');
            float[] parsed = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]))
                {
                    return false;
                }
            }

            values = parsed;
            return true;
        }

        private static bool TryParseIntList(string value, out int[] values)
        {
            values = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] parts = value.Split(',');
            int[] parsed = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed[i]))
                {
                    return false;
                }
            }

            values = parsed;
            return true;
        }

        private static float[] Lerp(float[] a, float[] b, float t)
        {
            float[] output = new float[a.Length];
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = Mathf.Lerp(a[i], b[i], t);
            }
            return output;
        }

        private static int[] PickInts(int[] a, int[] b, float t)
        {
            return (t < 0.5f ? a : b).ToArray();
        }

        private static string FormatVector(Vector3 value)
        {
            return CleanFloat(value.x) + "," + CleanFloat(value.y) + "," + CleanFloat(value.z);
        }

        private static string FormatFloatList(float[] values)
        {
            return string.Join(",", values.Select(CleanFloat).ToArray());
        }

        private static string CleanFloat(float value)
        {
            if (Mathf.Abs(value) < 0.0000005f)
            {
                value = 0f;
            }
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static string SanitizeFileName(string value)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in value ?? "")
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            return sb.Length == 0 ? "unknown" : sb.ToString();
        }

        private static object GetDesignerInstance()
        {
            Type type = FindType(DesignerTypeName);
            return GetMemberAny(type, "Instance");
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }

        private static object GetMemberAny(object target, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                object value = GetMember(target, names[i]);
                if (value != null)
                {
                    return value;
                }
            }
            return null;
        }

        private static object GetMember(object target, string name)
        {
            if (target == null)
            {
                return null;
            }

            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                 (instance == null ? BindingFlags.Static : BindingFlags.Instance);

            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(instance, null);
                }
                catch
                {
                    return null;
                }
            }

            FieldInfo field = type.GetField(name, flags);
            if (field != null)
            {
                try
                {
                    return field.GetValue(instance);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static void SetMember(object target, string name, object value)
        {
            if (target == null)
            {
                return;
            }

            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                 (instance == null ? BindingFlags.Static : BindingFlags.Instance);

            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0 && property.CanWrite)
            {
                property.SetValue(instance, value, null);
                return;
            }

            FieldInfo field = type.GetField(name, flags);
            if (field != null)
            {
                field.SetValue(instance, value);
            }
        }

        private static object InvokeMember(object target, string name, params object[] args)
        {
            if (target == null)
            {
                return null;
            }

            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                 (instance == null ? BindingFlags.Static : BindingFlags.Instance);
            MethodInfo[] methods = type.GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != args.Length)
                {
                    continue;
                }

                try
                {
                    return method.Invoke(instance, args);
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }
            }

            return null;
        }

        private static void AddToList(object listObject, object value)
        {
            if (listObject == null || value == null)
            {
                return;
            }

            MethodInfo add = listObject.GetType().GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
            if (add != null)
            {
                add.Invoke(listObject, new[] { value });
            }
        }

    }
}
