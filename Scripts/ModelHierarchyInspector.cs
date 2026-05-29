using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace VehicleMeasurement
{
    /// <summary>
    /// Model Hierarchy Inspector (Optimized)
    /// - Builds a lightweight data tree once.
    /// - Renders only currently-visible rows (lazy) using pooling.
    /// - Expand/collapse rebuilds only visible list; doesn't create UI for entire model.
    /// - Optional async build to avoid frame hitches on huge hierarchies.
    /// </summary>
    public class ModelHierarchyInspector : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("The panel that contains the hierarchy")]
        public GameObject inspectorPanel;

        [Tooltip("Container where hierarchy items are spawned (Content of ScrollRect)")]
        public Transform hierarchyContainer;

        [Tooltip("Prefab for each hierarchy row (should have Toggle and TMP_Text)")]
        public GameObject hierarchyItemPrefab;

        [Tooltip("Button to show/hide the inspector")]
        public Button toggleInspectorButton;

        [Tooltip("Close button")]
        public Button hideButton;
        public Button showButton;

        [Header("Settings")]
        [Tooltip("Indent amount per hierarchy level (pixels)")]
        public float indentPerLevel = 20f;

        [Tooltip("Show mesh renderer info (evaluated once per node during build)")]
        public bool showMeshInfo = true;

        [Tooltip("Color for active items")]
        public Color activeColor = Color.white;

        [Tooltip("Color for inactive items")]
        public Color inactiveColor = Color.gray;

        [Header("Performance")]
        [Tooltip("Use pooling for rows (recommended)")]
        public bool usePooling = true;

        [Tooltip("Build hierarchy asynchronously over multiple frames (recommended for huge models)")]
        public bool buildAsync = true;

        [Tooltip("How many nodes to process per frame when building async")]
        public int nodesPerFrame = 800;

        [Tooltip("If true, toggling will only enable/disable Renderers (does not SetActive on GameObjects). Much cheaper on complex prefabs with scripts.")]
        public bool rendererOnlyVisibility = false;

        [Tooltip("Log actions (Debug.Log is expensive in Editor)")]
        public bool verboseLogging = false;

        // Current model being inspected
        private GameObject _currentModel;

        // Data tree root
        private Node _root;

        // Flattened visible nodes (based on expanded states)
        private readonly List<Node> _visibleNodes = new List<Node>(2048);

        // Node lookup (for future extensions)
        private readonly Dictionary<Transform, Node> _nodeLookup = new Dictionary<Transform, Node>(2048);

        // Row pool and row caches
        private readonly List<GameObject> _rowPool = new List<GameObject>(256);
        private readonly List<RowRefs> _rowRefsPool = new List<RowRefs>(256);

        // Build coroutine handle
        private Coroutine _buildRoutine;


        [Header("Icons")]
        public Sprite expandedIcon;   // e.g. down arrow
        public Sprite collapsedIcon;  // e.g. right arrow
        public Sprite leafIcon;       // e.g. dot or small circ

        [Header("Row Toggle Button Sprites")]
        public Sprite rowOnSprite;   // shown when node is active
        public Sprite rowOffSprite;  // shown when node is inactive
        // ---------------------------
        // Internal data structures
        // ---------------------------

        public Button showAllButton;

        private class Node
        {
            public Transform transform;
            public Node parent;
            public readonly List<Node> children = new List<Node>(8);
            public int depth;
            public bool expanded = true;

            // Cached info (computed once)
            public bool hasMeshFilter;
            public bool hasRenderer;
        }

        private class RowRefs
        {
            public GameObject rowGO;
            public Button stateButton; // the button you click to enable/disable node
            public Image stateImage;   // the image on that button (where sprite changes)
            public TMP_Text label;
            public Button labelButton;

            // Optional indent helpers (if present in prefab)
            public LayoutElement indentLayoutElement;   // child named "Indent" with LayoutElement (best with HorizontalLayoutGroup)
            public RectTransform indentRect;            // child named "Indent" with RectTransform
            public HorizontalLayoutGroup layoutGroup;   // fallback (less ideal)

            public Node boundNode;

            // One-time listeners hook to avoid per-rebind allocations
            public bool listenersHooked;
            public Image prefixIcon;
            public Button prefixButton;
        }

        // ---------------------------
        // Unity lifecycle
        // ---------------------------

        private void Start()
        {
            if (toggleInspectorButton != null)
                toggleInspectorButton.onClick.AddListener(ToggleInspector);

            if (hideButton != null)
                hideButton.onClick.AddListener(HideInspector);
            if(showButton!=null)
                showButton.onClick.AddListener(ShowInspector);

            if (inspectorPanel != null)
                inspectorPanel.SetActive(false);

            showAllButton.onClick.AddListener(EnableAndDisableAll);
            _showAllButtonImage=showAllButton.transform.GetChild(0).GetComponent<Image>();
            _showAllButtonText = showAllButton.GetComponentInChildren<TMP_Text>();

            
        }

        private void OnDisable()
        {
            // Stop async build if object disabled
            if (_buildRoutine != null)
            {
                StopCoroutine(_buildRoutine);
                _buildRoutine = null;
            }
        }

        // ---------------------------
        // Public API
        // ---------------------------

        /// <summary>
        /// Set the model to inspect
        /// </summary>
        public void InspectModel(GameObject model)
        {
            if (model == null)
            {
                Debug.LogWarning("[ModelInspector] No model to inspect");
                return;
            }

            _currentModel = model;

            if (_buildRoutine != null)
            {
                StopCoroutine(_buildRoutine);
                _buildRoutine = null;
            }

            if (buildAsync)
                _buildRoutine = StartCoroutine(BuildTreeAsync());
            else
                BuildTreeSync();

            ShowInspector();
            CollapseAll();
        }

        /// <summary>
        /// Clears everything including current model reference
        /// </summary>
        public void ClearHierarchy()
        {
            _currentModel = null;
            _root = null;
            _nodeLookup.Clear();
            _visibleNodes.Clear();

            // Keep pool (reusable) but hide active rows
            HideAllRows();
        }

        /// <summary>
        /// Refresh the hierarchy (useful if model changed)
        /// </summary>
        public void Refresh()
        {
            if (_currentModel == null) return;
            InspectModel(_currentModel);
        }

        public void ShowInspector()
        {
            if (inspectorPanel != null)
                inspectorPanel.SetActive(true);
        }

        public void HideInspector()
        {
            if (inspectorPanel != null)
                inspectorPanel.SetActive(false);
        }

        public void ToggleInspector()
        {
            if (inspectorPanel != null)
                inspectorPanel.SetActive(!inspectorPanel.activeSelf);
        }

        /// <summary>
        /// Activate all parts
        /// </summary>

        public void ActivateAll()
        {
            if (_root == null) return;

            Node workingRoot = GetEffectiveRoot(_root);

            // Expand & activate only the effective root subtree
            SetExpandedRecursive(workingRoot, true);
            ApplyVisibilityToSubtree(workingRoot, true);

            RebuildVisibleRows();

            if (verboseLogging)
                Debug.Log("[ModelInspector] Activated all parts (effective root)");
        }

        private Node GetEffectiveRoot(Node start)
        {
            if (start == null) return null;

            Node n = start;

            // Skip wrapper nodes that only contain a single child
            while (n.children != null && n.children.Count == 1)
                n = n.children[0];

            return n;
        }
        private bool _isCollapsed = true;
        public void ExpandAndCollapse()
        {
            _isCollapsed=!_isCollapsed;
            if (_isCollapsed)
            {
                ExpandAll();
            }
            else
            {
                CollapseAll();
            }
        }
        private bool _isEnabled = true;
        [SerializeField] private Sprite _eyeOpen;
        [SerializeField] private Sprite _eyeClose;
        private Image _showAllButtonImage;
        private TMP_Text _showAllButtonText;
        public void EnableAndDisableAll()
        {
            _isEnabled = !_isEnabled;

            Node workingRoot = GetEffectiveRoot(_root);
            if (workingRoot == null) return;

            if (_isEnabled)
            {
                // Only turn everything ON
                ApplyVisibilityToSubtree(workingRoot, true);

                // Update visible icons/colors only (cheap)
                RebindVisibleRowsActiveStateOnly();

                _showAllButtonImage.sprite = _eyeOpen;
                _showAllButtonText.text = "Show all";
            }
            else
            {
                // Only turn everything OFF
                ApplyVisibilityToSubtree(workingRoot, false);

                // Update visible icons/colors only (cheap)
                RebindVisibleRowsActiveStateOnly();

                _showAllButtonImage.sprite = _eyeClose;
                _showAllButtonText.text = "Hide All";
            }
        }
        /// <summary>
        /// Deactivate all parts (except root)
        /// </summary>
        public void DeactivateAll()
        {

            if (_root == null) return;

            Node workingRoot = GetEffectiveRoot(_root);

            ApplyVisibilityToSubtree(workingRoot, false);
            RebuildVisibleRows();

            if (verboseLogging)
                Debug.Log("[ModelInspector] Deactivated effective root subtree");

        }

        /// <summary>
        /// Collapse hierarchy (show only root + level 1)
        /// </summary>
        public void CollapseAll()
        {
            if (_root == null) return;

            // Collapse everything
            SetExpandedRecursive(_root, false);
            // But keep root expanded so level 1 is visible
            _root.expanded = true;

            RebuildVisibleRows();
        }

        /// <summary>
        /// Expand all hierarchy items
        /// </summary>
        public void ExpandAll()
        {
            if (_root == null) return;

            SetExpandedRecursive(_root, true);
            RebuildVisibleRows();
        }

        // ---------------------------
        // Build tree
        // ---------------------------

        private void BuildTreeSync()
        {
            if (_currentModel == null || hierarchyContainer == null || hierarchyItemPrefab == null)
                return;

            _nodeLookup.Clear();

            // Build node tree (iterative DFS)
            _root = new Node
            {
                transform = _currentModel.transform,
                parent = null,
                depth = 0,
                expanded = true
            };
            CacheNodeInfo(_root);
            _nodeLookup[_root.transform] = _root;

            var stack = new Stack<Node>(1024);
            stack.Push(_root);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                var t = node.transform;

                // preserve child order by pushing reversed
                for (int i = t.childCount - 1; i >= 0; i--)
                {
                    var ct = t.GetChild(i);
                    var child = new Node
                    {
                        transform = ct,
                        parent = node,
                        depth = node.depth + 1,
                        expanded = true
                    };
                    CacheNodeInfo(child);

                    node.children.Add(child);
                    _nodeLookup[ct] = child;
                    stack.Push(child);
                }
            }

            RebuildVisibleRows();

            if (verboseLogging)
                Debug.Log($"[ModelInspector] Built data tree with {_nodeLookup.Count} nodes");
        }
        private IEnumerator BuildTreeAsync()
        {
            if (_currentModel == null || hierarchyContainer == null || hierarchyItemPrefab == null)
                yield break;

            _nodeLookup.Clear();

            _root = new Node
            {
                transform = _currentModel.transform,
                parent = null,
                depth = 0,
                expanded = true
            };
            CacheNodeInfo(_root);
            _nodeLookup[_root.transform] = _root;

            var stack = new Stack<Node>(1024);
            stack.Push(_root);

            int processed = 0;

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                var t = node.transform;

                for (int i = t.childCount - 1; i >= 0; i--)
                {
                    var ct = t.GetChild(i);
                    var child = new Node
                    {
                        transform = ct,
                        parent = node,
                        depth = node.depth + 1,
                        expanded = true
                    };
                    CacheNodeInfo(child);

                    node.children.Add(child);
                    _nodeLookup[ct] = child;
                    stack.Push(child);

                    processed++;
                    if (processed >= nodesPerFrame)
                    {
                        processed = 0;
                        yield return null; // spread work across frames
                    }
                }
            }

            RebuildVisibleRows();

            if (verboseLogging)
                Debug.Log($"[ModelInspector] Built data tree async with {_nodeLookup.Count} nodes");

            _buildRoutine = null;
        }

        private void CacheNodeInfo(Node node)
        {
            if (!showMeshInfo || node == null || node.transform == null) return;

            // TryGetComponent avoids allocations.
            node.hasMeshFilter = node.transform.TryGetComponent<MeshFilter>(out _);

            // "Renderer" covers MeshRenderer + SkinnedMeshRenderer etc.
            node.hasRenderer = node.transform.TryGetComponent<Renderer>(out _);
        }

        private void RebuildVisibleRows()
        {
            if (_root == null || hierarchyContainer == null || hierarchyItemPrefab == null)
            {
                HideAllRows();
                return;
            }

            BuildVisibleList();
            EnsureRowPoolSize(_visibleNodes.Count);

            // Bind rows
            for (int i = 0; i < _visibleNodes.Count; i++)
            {
                var node = _visibleNodes[i];
                var refs = _rowRefsPool[i];

                if (!refs.rowGO.activeSelf)
                    refs.rowGO.SetActive(true);

                BindRow(refs, node);
            }

            // Hide extra pooled rows
            for (int i = _visibleNodes.Count; i < _rowRefsPool.Count; i++)
            {
                if (_rowRefsPool[i].rowGO.activeSelf)
                    _rowRefsPool[i].rowGO.SetActive(false);
            }

            if (verboseLogging)
                Debug.Log($"[ModelInspector] Visible rows: {_visibleNodes.Count} (pool size {_rowRefsPool.Count})");
        }

        private void BuildVisibleList()
        {
            _visibleNodes.Clear();

            // Pre-order DFS; only traverse children of expanded nodes
            var stack = new Stack<Node>(1024);
            stack.Push(_root);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                _visibleNodes.Add(node);

                if (!node.expanded) continue;

                // push reversed to keep order
                for (int i = node.children.Count - 1; i >= 0; i--)
                    stack.Push(node.children[i]);
            }
        }

        private void EnsureRowPoolSize(int required)
        {
            if (!usePooling)
            {
                // If you truly want no pooling (not recommended),
                // we destroy and recreate rows to match visible count.
                // Pooling is significantly faster and less GC.
                ClearAllRowsHard();
            }

            while (_rowRefsPool.Count < required)
            {
                var go = Instantiate(hierarchyItemPrefab, hierarchyContainer);
                var refs = CreateRowRefs(go);

                _rowPool.Add(go);
                _rowRefsPool.Add(refs);
            }
        }

        private RowRefs CreateRowRefs(GameObject rowGO)
        {
            var refs = new RowRefs();
            refs.rowGO = rowGO;

            // Find the state button (name it e.g. "StateButton" in prefab)
            Button stateBtn = null;
            var Actbuttons = rowGO.GetComponentsInChildren<Button>(true);

            foreach (var b in Actbuttons)
            {
                if (b != null && b.gameObject != null &&
                    b.gameObject.name.IndexOf("State", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    stateBtn = b;
                    break;
                }
            }

            refs.stateButton = stateBtn;
            refs.stateImage = stateBtn != null ? stateBtn.GetComponent<Image>() : null;
            refs.label = rowGO.GetComponentInChildren<TMP_Text>(true);

            Button labelButton = null;
            var buttons = rowGO.GetComponentsInChildren<Button>(true);
            foreach (var b in buttons)
            {
                if (b != null && b.gameObject != null && b.gameObject.name.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    labelButton = b;
                    break;
                }
            }
            refs.labelButton = labelButton;

            // Optional indent support:
            // - Prefer a child named "Indent" with LayoutElement (best)
            // - Or a child named "Indent" with RectTransform
            var indents = rowGO.GetComponentsInChildren<Transform>(true);
            foreach (var t in indents)
            {
                if (t == null || t.gameObject == null) continue;
                if (string.Equals(t.gameObject.name, "Indent", StringComparison.OrdinalIgnoreCase))
                {
                    refs.indentLayoutElement = t.GetComponent<LayoutElement>();
                    refs.indentRect = t as RectTransform;
                    break;
                }
            }


            var images = rowGO.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                if (img != null && img.gameObject.name.IndexOf("PrefixIcon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    refs.prefixIcon = img;
                    break;
                }
            }
            if (refs.prefixIcon != null)
            {
                // Prefix icon can be clickable if it has a Button
                refs.prefixButton = refs.prefixIcon.GetComponent<Button>();
            }

            // Fallback: layout group padding (works but more expensive if you render thousands of rows)
            refs.layoutGroup = rowGO.GetComponent<HorizontalLayoutGroup>();

            if (refs.stateButton == null || refs.label == null)
            {
                Debug.LogError("[ModelInspector] hierarchyItemPrefab must contain a Toggle and a TMP_Text.");
            }

            // Hook listeners ONCE per pooled row (no per-rebind allocations)
            if (!refs.listenersHooked)
            {
                refs.listenersHooked = true;

                if (refs.stateButton != null)
                {
                    refs.stateButton.onClick.AddListener(() =>
                    {
                        if (refs.boundNode == null || refs.boundNode.transform == null) return;

                        // Flip current active state
                        bool isActive = GetNodeActiveState(refs.boundNode);
                        OnToggleNode(refs.boundNode, !isActive);
                    });
                }

                if (refs.prefixButton != null)
                {
                    refs.prefixButton.onClick.AddListener(() =>
                    {
                        if (refs.boundNode == null) return;
                        ToggleExpandCollapse(refs.boundNode);
                    });
                }

                if (refs.labelButton != null)
                {
                    refs.labelButton.onClick.AddListener(() =>
                    {
                        if (refs.boundNode == null) return;
                        ToggleExpandCollapse(refs.boundNode);
                    });
                }
                else
                {
                    // If no label button, allow clicking the row itself if it has a Button component
                    var rowButton = rowGO.GetComponent<Button>();
                    if (rowButton != null)
                    {
                        rowButton.onClick.AddListener(() =>
                        {
                            if (refs.boundNode == null) return;
                            ToggleExpandCollapse(refs.boundNode);
                        });
                    }
                }
            }

            return refs;
        }

        [Header("Prefix Icon Sizes (Width x Height)")]
        public Vector2 expandedIconSize = new Vector2(8f, 14f);
        public Vector2 collapsedIconSize = new Vector2(14f, 8f);
        public Vector2 leafIconSize = new Vector2(8f, 8f);

        private void BindRow(RowRefs refs, Node node)
        {
            refs.boundNode = node;

            // Decide icon based on children + expanded state
            bool hasChildren = node.children.Count > 0;

            if (refs.prefixIcon != null)
            {
                if (hasChildren)
                {

                    bool isExpanded = node.expanded;

                    refs.prefixIcon.sprite = isExpanded ? expandedIcon : collapsedIcon;

                    // Resize icon
                    var rt = refs.prefixIcon.rectTransform;
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                        isExpanded ? expandedIconSize.x : collapsedIconSize.x);
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                        isExpanded ? expandedIconSize.y : collapsedIconSize.y);

                }
                else
                {

                    refs.prefixIcon.sprite = leafIcon;

                    // Optional leaf size
                    var rt = refs.prefixIcon.rectTransform;
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, leafIconSize.x);
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, leafIconSize.y);

                }
                //refs.prefixIcon.sprite = node.expanded ? expandedIcon : collapsedIcon;

                // else
                // refs.prefixIcon.sprite = leafIcon;

                // Optional: hide icon if sprite missing
                refs.prefixIcon.enabled = (refs.prefixIcon.sprite != null);
            }

            if (refs.label != null)
            {
                string name = node.transform != null ? node.transform.name : "<null>";

                if (showMeshInfo)
                {
                    if (node.hasMeshFilter) name += " [Mesh]";
                    else if (node.hasRenderer) name += " [Renderer]";
                }

                // No more unicode bullets/arrows — label is just name
                refs.label.text = name;
            }

            // Indentation
            // Indentation
            float indent = node.depth * indentPerLevel;

            // Update LayoutElement (good metadata for layout)
            if (refs.indentLayoutElement != null)
            {
                refs.indentLayoutElement.minWidth = indent;
                refs.indentLayoutElement.preferredWidth = indent;
                refs.indentLayoutElement.flexibleWidth = 0;
            }

            // ALSO force the RectTransform width so spacing changes even when
            // HorizontalLayoutGroup is NOT controlling child widths.
            if (refs.indentRect != null)
            {
                refs.indentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, indent);
            }

            // If you use pooling, layout sometimes won't refresh immediately:
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)refs.rowGO.transform);

            // Toggle + label color reflect active state
            bool isActive = GetNodeActiveState(node);

            if (refs.stateImage != null)
                refs.stateImage.sprite = isActive ? rowOnSprite : rowOffSprite;

            if (refs.label != null)
                refs.label.color = isActive ? activeColor : inactiveColor;
        }

        private bool GetNodeActiveState(Node node)
        {
            if (node == null || node.transform == null) return false;

            if (!rendererOnlyVisibility)
                return node.transform.gameObject.activeSelf;

            // Renderer-only mode: consider active if ANY renderer on this GameObject is enabled.
            if (node.transform.TryGetComponent<Renderer>(out var r))
                return r.enabled;

            // No renderer: fall back to GameObject activeSelf
            return node.transform.gameObject.activeSelf;
        }

        private void HideAllRows()
        {
            for (int i = 0; i < _rowRefsPool.Count; i++)
            {
                if (_rowRefsPool[i].rowGO != null && _rowRefsPool[i].rowGO.activeSelf)
                    _rowRefsPool[i].rowGO.SetActive(false);
            }
        }

        private void ClearAllRowsHard()
        {
            for (int i = 0; i < _rowPool.Count; i++)
            {
                if (_rowPool[i] != null)
                    Destroy(_rowPool[i]);
            }
            _rowPool.Clear();
            _rowRefsPool.Clear();
        }   

        // ---------------------------
        // Expand / Collapse
        // ---------------------------

        private void ToggleExpandCollapse(Node node)
        {
            if (node == null) return;
            if (node.children == null || node.children.Count == 0) return; // <-- add this

            node.expanded = !node.expanded;
            RebuildVisibleRows();

            if (verboseLogging && node.transform != null)
                Debug.Log($"[ModelInspector] {(node.expanded ? "Expanded" : "Collapsed")} {node.transform.name}");
        }

        private void SetExpandedRecursive(Node node, bool expanded)
        {
            if (node == null) return;

            var stack = new Stack<Node>(1024);
            stack.Push(node);

            while (stack.Count > 0)
            {
                var n = stack.Pop();
                n.expanded = expanded;
                for (int i = 0; i < n.children.Count; i++)
                    stack.Push(n.children[i]);
            }
        }

        // ---------------------------
        // Activation toggles
        // ---------------------------

        private void OnToggleNode(Node node, bool isOn)
        {
            if (node == null || node.transform == null) return;

            ApplyVisibilityToSubtree(node, isOn);

            // Update currently visible rows only (cheap)
            RebindVisibleRowsActiveStateOnly();

            if (verboseLogging)
                Debug.Log($"[ModelInspector] {node.transform.name} subtree set to {(isOn ? "active" : "inactive")}");
        }

        private void ApplyVisibilityToSubtree(Node root, bool isOn)
        {
            if (root == null || root.transform == null) return;

            // Use data tree to traverse (faster than Transform.GetChild loops repeatedly)
            var stack = new Stack<Node>(1024);
            stack.Push(root);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node.transform == null) continue;

                if (rendererOnlyVisibility)
                {
                    // Disable/enable renderers only (cheaper, avoids OnEnable/OnDisable storm)
                    if (node.transform.TryGetComponent<Renderer>(out var r))
                        r.enabled = isOn;
                }
                else
                {
                    // Match old behavior: SetActive on every object so activeSelf reflects intended state
                    node.transform.gameObject.SetActive(isOn);
                }

                for (int i = 0; i < node.children.Count; i++)
                    stack.Push(node.children[i]);
            }
        }

        private void RebindVisibleRowsActiveStateOnly()
        {
            // Only update toggle state + label color for visible rows
            int count = Mathf.Min(_visibleNodes.Count, _rowRefsPool.Count);
            for (int i = 0; i < count; i++)
            {
                var node = _visibleNodes[i];
                var refs = _rowRefsPool[i];
                if (refs == null || refs.rowGO == null || !refs.rowGO.activeSelf) continue;

                bool isActive = GetNodeActiveState(node);

                if (refs.stateImage != null)
                    refs.stateImage.sprite = isActive ? rowOnSprite : rowOffSprite;

                if (refs.label != null)
                    refs.label.color = isActive ? activeColor : inactiveColor;
            }
        }
    }
}