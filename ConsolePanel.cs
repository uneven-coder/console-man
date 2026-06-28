using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SFS.UI.ModGUI;
using SFS.UI;
using TMPro;
using UITools;
using UnityEngine;
using UnityEngine.UI;
using SFSConsole = ModLoader.IO.Console;

namespace consoleMan
{
    public class ConsolePanel : MonoBehaviour
    {
        public static ConsolePanel instance;

        SFSConsole console;
        Transform rowContainer;
        bool panelBuilt;

        readonly Dictionary<string, TMP_Text> countTMPs = new();

        const int PanelW = 600;
        const int NotiW = 34;
        const int AllBtnW = 52;
        const int BtnW = 60;
        static int RowW => PanelW - PadRight;
        static int NameW => RowW - 4 - NotiW - AllBtnW - 4 * BtnW - 6 * ColGap;
        const int ColGap = 3;
        const int RowH = 34;
        const int TitleH = 30;
        const int HeaderH = 22;
        const int PadTop = 24;
        const int PadRight = 28;

        static readonly Color BtnOnNormal = new Color(0.22f, 0.65f, 0.22f);
        static readonly Color BtnOnHigh = new Color(0.28f, 0.80f, 0.28f);
        static readonly Color BtnOnPress = new Color(0.16f, 0.48f, 0.16f);
        static readonly Color BtnOffNormal = new Color(0.22f, 0.22f, 0.22f);
        static readonly Color BtnOffHigh = new Color(0.30f, 0.30f, 0.30f);
        static readonly Color BtnOffPress = new Color(0.15f, 0.15f, 0.15f);
        static readonly Color BtnOnText = Color.white;
        static readonly Color BtnOffText = new Color(0.50f, 0.50f, 0.50f);

        static readonly string[] PinnedOrder = { "Game", "ModLoader", "Harmony", "Other" };
        static readonly HashSet<string> Pinned = new(PinnedOrder, StringComparer.OrdinalIgnoreCase);

        public static void Build(SFSConsole con)
        {
            var go = new GameObject("ConsolePanelManager");
            go.transform.SetParent(con.transform, false);
            instance = go.AddComponent<ConsolePanel>();
            instance.console = con;
        }

        void OnEnable()
        {
            if (panelBuilt) return;
            panelBuilt = true;
            StartCoroutine(DeferredBuild());
        }

        IEnumerator DeferredBuild()
        {
            yield return null;
            try { InjectIntoHolder(); }
            catch (Exception ex) { Debug.LogError($"[ConsoleMan] {ex}"); }
        }

        void InjectIntoHolder()
        {
            var holderT = console.holder.transform;
            var existingLG = console.holder.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (existingLG != null) existingLG.enabled = false;

            var children = holderT.Cast<Transform>().ToList();

            var leftRT = MakeRect(holderT, "ConsoleContent", 0, 0, PanelW, 0);
            foreach (var child in children) child.SetParent(leftRT, false);

            var rightRT = new GameObject("ConsoleFilterPanel").AddComponent<RectTransform>();
            rightRT.SetParent(holderT, false);
            rightRT.anchorMin = new Vector2(1f, 0f);
            rightRT.anchorMax = Vector2.one;
            rightRT.offsetMin = new Vector2(-PanelW, 0f);
            rightRT.offsetMax = Vector2.zero;

            BuildFilterPanel(rightRT);
        }

        void BuildFilterPanel(RectTransform parent)
        {
            BuildTitleBar(MakeTopStrip(parent, "TitleBar", PadTop, TitleH, PadRight));
            BuildColumnHeaders(MakeTopStrip(parent, "ColumnHeaders", PadTop + TitleH, HeaderH, PadRight));
            BuildScrollArea(MakeRect(parent, "ScrollHost", 0, 0, PadRight, PadTop + TitleH + HeaderH));
            RefreshSourceRows();
        }

        void BuildTitleBar(RectTransform parent)
        {
            var hlg = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 6;
            hlg.padding = new RectOffset(4, 4, 2, 2);
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            Builder.CreateLabel(parent, 160, TitleH - 4, 0, 0, "Console Filters");
        }

        void BuildColumnHeaders(RectTransform parent)
        {
            var hlg = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = ColGap;
            hlg.padding = new RectOffset(2, 2, 0, 0);
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            MakeHeaderCell(parent, NotiW, "#");
            MakeHeaderCell(parent, NameW, "Source");
            MakeHeaderCell(parent, AllBtnW, "ALL");
            MakeHeaderCell(parent, BtnW, "LOG");
            MakeHeaderCell(parent, BtnW, "WRN");
            MakeHeaderCell(parent, BtnW, "ERR");
            MakeHeaderCell(parent, BtnW, "EXC");
        }

        void MakeHeaderCell(Transform parent, int width, string text)
        {
            var lbl = Builder.CreateLabel(parent, width, HeaderH - 2, 0, 0, text);
            var tmp = lbl.gameObject.GetComponentInChildren<TMP_Text>(true);
            if (tmp == null) return;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.55f, 0.55f, 0.55f);
            tmp.fontSize = 11;
        }

        void BuildScrollArea(RectTransform parent)
        {
            var vpRT = MakeRect(parent, "Viewport", 0, 0, 0, 0);
            vpRT.gameObject.AddComponent<RectMask2D>();

            var cRT = new GameObject("Content").AddComponent<RectTransform>();
            cRT.SetParent(vpRT, false);
            cRT.anchorMin = new Vector2(0f, 1f);
            cRT.anchorMax = new Vector2(1f, 1f);
            cRT.pivot = new Vector2(0.5f, 1f);
            cRT.sizeDelta = Vector2.zero;

            var cVLG = cRT.gameObject.AddComponent<VerticalLayoutGroup>();
            cVLG.childAlignment = TextAnchor.UpperLeft;
            cVLG.spacing = 2;
            cVLG.childControlWidth = false;
            cVLG.childControlHeight = true;
            cVLG.childForceExpandWidth = false;
            cVLG.childForceExpandHeight = false;
            cRT.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = parent.gameObject.AddComponent<ScrollRect>();
            sr.viewport = vpRT;
            sr.content = cRT;
            sr.vertical = true;
            sr.horizontal = false;
            sr.scrollSensitivity = 30f;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia = true;
            sr.verticalNormalizedPosition = 1f;

            rowContainer = cRT;
        }

        public void RefreshSourceRows()
        {
            if (rowContainer == null) return;
            countTMPs.Clear();
            foreach (Transform child in rowContainer)
                Destroy(child.gameObject);

            foreach (string source in SortedSources())
                AddSourceRow(source);
        }

        IEnumerable<string> SortedSources()
        {
            var sources = Config.S.Sources;
            return PinnedOrder.Where(sources.ContainsKey)
                .Concat(sources.Keys
                    .Where(k => !Pinned.Contains(k))
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        }

        void AddSourceRow(string source)
        {
            var row = Builder.CreateBox(rowContainer, RowW, RowH, 0, 0, 0.2f);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = RowH;

            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = ColGap;
            hlg.padding = new RectOffset(2, 2, 1, 1);
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            var noti = Builder.CreateBox(row.gameObject.transform, NotiW, RowH - 4, 0, 0, 1f);

            var img = noti.gameObject.GetComponentsInChildren<Image>(true)
                .FirstOrDefault(i => i.GetComponentInChildren<TMP_Text>(true) == null);
            if (img != null) { img.color = new Color(0.76f, 0.06f, 0.06f, 0.8f); img.material = null; }

            noti.gameObject.AddComponent<LayoutElement>().preferredWidth = NotiW;

            var notiLbl = Builder.CreateLabel(noti.gameObject.transform, NotiW, RowH - 4, 0, 0, "");
            var notiTMP = notiLbl.gameObject.GetComponentInChildren<TMP_Text>(true);
            if (notiTMP != null)
            {
                int c = LogInterceptor.GetLogCounts(source).Total;
                notiTMP.text = c > 0 ? c.ToString() : "";
                notiTMP.alignment = TextAlignmentOptions.Center;
                countTMPs[source] = notiTMP;
            }

            Builder.CreateLabel(row.gameObject.transform, NameW, RowH - 2, 0, 0, source);

            var filter = Config.S.GetOrAdd(source);
            AddAllToggle(row.gameObject.transform, filter);
            AddLevelToggle(row.gameObject.transform, filter, LogType.Log, "LOG");
            AddLevelToggle(row.gameObject.transform, filter, LogType.Warning, "WRN");
            AddLevelToggle(row.gameObject.transform, filter, LogType.Error, "ERR");
            AddLevelToggle(row.gameObject.transform, filter, LogType.Exception, "EXC");
        }

        void AddAllToggle(Transform parent, Config.SourceFilter filter)
        {
            bool AllOn() => filter.Log && filter.Warning && filter.Error && filter.Exception;
            var btn = Builder.CreateButton(parent, AllBtnW, RowH - 2, 0, 0, null, "ALL");
            ToggleButtonFix.Attach(btn.gameObject, AllOn, BtnOnText, BtnOffText,
                BtnOnNormal, BtnOnHigh, BtnOnPress, BtnOffNormal, BtnOffHigh, BtnOffPress);
            AttachClick(btn.gameObject, () =>
            {
                bool v = !AllOn();
                filter.Log = filter.Warning = filter.Error = filter.Exception = v;
                OnFilterChanged();
            });
        }

        void AddLevelToggle(Transform parent, Config.SourceFilter filter, LogType level, string label)
        {
            var btn = Builder.CreateButton(parent, BtnW, RowH - 2, 0, 0, null, label);
            ToggleButtonFix.Attach(btn.gameObject, () => filter.GetLevel(level), BtnOnText, BtnOffText,
                BtnOnNormal, BtnOnHigh, BtnOnPress, BtnOffNormal, BtnOffHigh, BtnOffPress);
            AttachClick(btn.gameObject, () =>
            {
                filter.SetLevel(level, !filter.GetLevel(level));
                OnFilterChanged();
            });
        }

        static void AttachClick(GameObject go, Action action)
        {
            var sfs = go.GetComponent<SFS.UI.Button>() ?? go.GetComponentInChildren<SFS.UI.Button>(true);
            if (sfs != null) { sfs.onClick += action; return; }
            var ui = go.GetComponent<UnityEngine.UI.Button>() ?? go.GetComponentInChildren<UnityEngine.UI.Button>(true);
            ui?.onClick.AddListener(() => action());
        }

        void OnFilterChanged()
        {
            LogInterceptor.RebuildQueue(console);
            Config.Save();
        }

        void Update()
        {
            if (LogInterceptor.pendingQueueUpdate && console?.holder != null && console.holder.activeSelf)
            {
                LogInterceptor.pendingQueueUpdate = false;
                LogInterceptor.updateTextMethod?.Invoke(console, null);
            }

            if (LogInterceptor.pendingSourceRefresh)
            {
                LogInterceptor.pendingSourceRefresh = false;
                RefreshSourceRows();
                LogInterceptor.RebuildQueue(console);
                Config.Save();
            }

            if (LogInterceptor.pendingCountRefresh)
            {
                LogInterceptor.pendingCountRefresh = false;
                foreach (var kvp in countTMPs)
                {
                    int c = LogInterceptor.GetLogCounts(kvp.Key).Total;
                    kvp.Value.text = c > 0 ? c.ToString() : "";
                }
            }
        }

        static RectTransform MakeRect(Transform parent, string name, float left, float bottom, float right, float top)
        {
            var rt = new GameObject(name).AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        static RectTransform MakeTopStrip(RectTransform parent, string name, float fromTop, float height, float rightInset = 0f)
        {
            var rt = new GameObject(name).AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(0f, -(fromTop + height));
            rt.offsetMax = new Vector2(-rightInset, -fromTop);
            return rt;
        }
    }

    class ToggleButtonFix : MonoBehaviour
    {
        UnityEngine.UI.Button uiBtn;
        TMP_Text tmp;
        Func<bool> predicate;
        Color onText, offText;
        Color onNormal, onHigh, onPress;
        Color offNormal, offHigh, offPress;
        bool lastState;

        public static void Attach(GameObject go, Func<bool> predicate,
            Color onText, Color offText,
            Color onNormal, Color onHigh, Color onPress,
            Color offNormal, Color offHigh, Color offPress)
        {
            var f = go.AddComponent<ToggleButtonFix>();
            f.uiBtn = go.GetComponentInChildren<UnityEngine.UI.Button>(true);
            f.tmp = go.GetComponentInChildren<TMP_Text>(true);
            f.predicate = predicate;
            f.onText = onText; f.offText = offText;
            f.onNormal = onNormal; f.onHigh = onHigh; f.onPress = onPress;
            f.offNormal = offNormal; f.offHigh = offHigh; f.offPress = offPress;
            f.lastState = !predicate();
            f.ApplyColors();
        }

        void ApplyColors()
        {
            bool state = predicate();
            lastState = state;
            if (tmp != null) tmp.color = state ? onText : offText;
            if (uiBtn == null) return;
            var cb = uiBtn.colors;
            cb.normalColor = state ? onNormal : offNormal;
            cb.highlightedColor = state ? onHigh : offHigh;
            cb.pressedColor = state ? onPress : offPress;
            cb.selectedColor = cb.normalColor;
            cb.colorMultiplier = 1f;
            uiBtn.colors = cb;
            uiBtn.transition = Selectable.Transition.ColorTint;
        }

        void LateUpdate()
        {
            if (predicate == null) return;
            bool state = predicate();
            if (tmp != null) tmp.color = state ? onText : offText;
            if (state == lastState) return;
            lastState = state;
            if (uiBtn == null) return;
            var cb = uiBtn.colors;
            cb.normalColor = state ? onNormal : offNormal;
            cb.highlightedColor = state ? onHigh : offHigh;
            cb.pressedColor = state ? onPress : offPress;
            cb.selectedColor = cb.normalColor;
            cb.colorMultiplier = 1f;
            uiBtn.colors = cb;
        }
    }
}
