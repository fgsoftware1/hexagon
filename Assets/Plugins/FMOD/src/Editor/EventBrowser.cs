﻿using System;
using System.Collections.Generic;
using System.Linq;
using FMOD;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FMODUnity
{
    internal class EventBrowser : EditorWindow, ISerializationCallbackReceiver
    {
        private const float RepaintInterval = 1 / 30.0f;

        [SerializeField] private PreviewArea previewArea = new PreviewArea();

        [SerializeField] private TreeView.State treeViewState;

        private Texture2D borderIcon;
        private GUIStyle borderStyle;

        [NonSerialized] private float[] cachedMetering;

        [NonSerialized] private DateTime LastKnownCacheTime;

        [NonSerialized] private float nextRepaintTime;

        private SerializedProperty outputProperty;

        [NonSerialized] private SearchField searchField;

        [NonSerialized] private TreeView treeView;

        public static bool IsOpen { get; private set; }

        private bool InChooserMode => outputProperty != null;

        private void Update()
        {
            var forceRepaint = false;

            var currentMetering = EditorUtils.GetMetering();
            if (cachedMetering == null || !cachedMetering.SequenceEqual(currentMetering))
            {
                cachedMetering = currentMetering;
                forceRepaint = true;
            }

            if (LastKnownCacheTime != EventManager.CacheTime)
            {
                ReadEventCache();
                forceRepaint = true;
            }

            if (forceRepaint || (previewArea != null && previewArea.forceRepaint &&
                                 nextRepaintTime < Time.realtimeSinceStartup))
            {
                Repaint();
                nextRepaintTime = Time.time + RepaintInterval;
            }
        }

        public void OnEnable()
        {
            if (treeViewState == null) treeViewState = new TreeView.State();

            searchField = new SearchField();
            treeView = new TreeView(treeViewState);

            ReadEventCache();

            searchField.downOrUpArrowKeyPressed += treeView.SetFocus;

#if UNITY_2019_1_OR_NEWER
            SceneView.duringSceneGui += SceneUpdate;
#else
            SceneView.onSceneGUIDelegate += SceneUpdate;
#endif

            EditorApplication.hierarchyWindowItemOnGUI += HierarchyUpdate;

            IsOpen = true;
        }

        public void OnDestroy()
        {
            EditorUtils.PreviewStop();

            IsOpen = false;
        }

        private void OnGUI()
        {
            AffirmResources();

            if (InChooserMode) GUILayout.BeginVertical(borderStyle, GUILayout.ExpandWidth(true));

            treeView.searchString = searchField.OnGUI(treeView.searchString);

            var treeRect = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            treeRect.y += 2;
            treeRect.height -= 2;

            treeView.OnGUI(treeRect);

            if (InChooserMode)
            {
                GUILayout.EndVertical();
                HandleChooserModeEvents();
            }
            else
            {
                previewArea.treeView = treeView;
                previewArea.OnGUI(cachedMetering != null ? cachedMetering : EditorUtils.GetMetering());
            }
        }

        public void OnBeforeSerialize()
        {
            treeViewState = treeView.state;
        }

        public void OnAfterDeserialize()
        {
        }

        [MenuItem("FMOD/Event Browser", priority = 2)]
        public static void ShowWindow()
        {
            var eventBrowser = GetWindow<EventBrowser>("FMOD Events");
            eventBrowser.minSize = new Vector2(380, 600);

            eventBrowser.BeginStandaloneWindow();
            eventBrowser.Show();
        }

        private void ReadEventCache()
        {
            LastKnownCacheTime = EventManager.CacheTime;
            treeView.Reload();
        }

        private void AffirmResources()
        {
            if (borderIcon == null)
            {
                borderIcon = EditorGUIUtility.Load("FMOD/Border.png") as Texture2D;

                borderStyle = new GUIStyle(GUI.skin.box);
                borderStyle.normal.background = borderIcon;
                borderStyle.margin = new RectOffset();
            }
        }

        private void HandleChooserModeEvents()
        {
            if (Event.current.isKey)
            {
                var keyCode = Event.current.keyCode;

                if ((keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter) && treeView.SelectedObject != null)
                {
                    SetOutputProperty(treeView.SelectedObject);
                    Event.current.Use();
                    Close();
                }
                else if (keyCode == KeyCode.Escape)
                {
                    Event.current.Use();
                    Close();
                }
            }
            else if (treeView.DoubleClickedObject != null)
            {
                SetOutputProperty(treeView.DoubleClickedObject);
                Close();
            }
        }

        private void SetOutputProperty(ScriptableObject data)
        {
            if (data is EditorEventRef)
            {
                var path = (data as EditorEventRef).Path;
                outputProperty.stringValue = path;
                EditorUtils.UpdateParamsOnEmitter(outputProperty.serializedObject, path);
            }
            else if (data is EditorBankRef)
            {
                outputProperty.stringValue = (data as EditorBankRef).Name;
            }
            else if (data is EditorParamRef)
            {
                outputProperty.stringValue = (data as EditorParamRef).Name;
            }

            outputProperty.serializedObject.ApplyModifiedProperties();
        }

        public void ChooseEvent(SerializedProperty property)
        {
            BeginInspectorPopup(property, TypeFilter.Event);

            if (!string.IsNullOrEmpty(property.stringValue)) treeView.JumpToEvent(property.stringValue);
        }

        public void ChooseBank(SerializedProperty property)
        {
            BeginInspectorPopup(property, TypeFilter.Bank);

            if (!string.IsNullOrEmpty(property.stringValue)) treeView.JumpToBank(property.stringValue);
        }

        public void ChooseParameter(SerializedProperty property)
        {
            BeginInspectorPopup(property, TypeFilter.Parameter);
        }

        public void FrameEvent(string path)
        {
            treeView.JumpToEvent(path);
        }

        private void BeginInspectorPopup(SerializedProperty property, TypeFilter typeFilter)
        {
            treeView.TypeFilter = typeFilter;
            outputProperty = property;
            searchField.SetFocus();
            treeView.DragEnabled = false;
        }

        private void BeginStandaloneWindow()
        {
            treeView.TypeFilter = TypeFilter.All;
            outputProperty = null;
            searchField.SetFocus();
            treeView.DragEnabled = true;
        }

        private static bool IsDraggable(Object data)
        {
            return data is EditorEventRef || data is EditorBankRef || data is EditorParamRef;
        }

        public static bool IsDroppable(Object[] data)
        {
            return data.Length > 0 && IsDraggable(data[0]);
        }

        // This is an event handler on the hierachy view to handle dragging our objects from the browser
        private void HierarchyUpdate(int instance, Rect rect)
        {
            if (Event.current.type == EventType.DragPerform && rect.Contains(Event.current.mousePosition))
                if (IsDroppable(DragAndDrop.objectReferences))
                {
                    var data = DragAndDrop.objectReferences[0];

                    var target = EditorUtility.InstanceIDToObject(instance) as GameObject;

                    if (data is EditorEventRef)
                    {
                        Undo.SetCurrentGroupName("Add Studio Event Emitter");

                        var emitter = Undo.AddComponent<StudioEventEmitter>(target);
                        emitter.Event = (data as EditorEventRef).Path;
                    }
                    else if (data is EditorBankRef)
                    {
                        Undo.SetCurrentGroupName("Add Studio Bank Loader");

                        var loader = Undo.AddComponent<StudioBankLoader>(target);
                        loader.Banks = new List<string>();
                        loader.Banks.Add((data as EditorBankRef).Name);
                    }
                    else // data is EditorParamRef
                    {
                        Undo.SetCurrentGroupName("Add Studio Global Parameter Trigger");

                        var trigger = Undo.AddComponent<StudioGlobalParameterTrigger>(target);
                        trigger.parameter = (data as EditorParamRef).Name;
                    }

                    Selection.activeObject = target;

                    Event.current.Use();
                }
        }

        // This is an event handler on the scene view to handle dragging our objects from the browser
        // and creating new gameobjects
        private void SceneUpdate(SceneView sceneView)
        {
            if (Event.current.type == EventType.DragPerform && IsDroppable(DragAndDrop.objectReferences))
            {
                var data = DragAndDrop.objectReferences[0];
                GameObject newObject;

                if (data is EditorEventRef)
                {
                    var path = (data as EditorEventRef).Path;

                    var name = path.Substring(path.LastIndexOf("/") + 1);
                    newObject = new GameObject(name + " Emitter");

                    var emitter = newObject.AddComponent<StudioEventEmitter>();
                    emitter.Event = path;

                    Undo.RegisterCreatedObjectUndo(newObject, "Create Studio Event Emitter");
                }
                else if (data is EditorBankRef)
                {
                    newObject = new GameObject("Studio Bank Loader");

                    var loader = newObject.AddComponent<StudioBankLoader>();
                    loader.Banks = new List<string>();
                    loader.Banks.Add((data as EditorBankRef).Name);

                    Undo.RegisterCreatedObjectUndo(newObject, "Create Studio Bank Loader");
                }
                else // data is EditorParamRef
                {
                    var name = (data as EditorParamRef).Name;

                    newObject = new GameObject(name + " Trigger");

                    var trigger = newObject.AddComponent<StudioGlobalParameterTrigger>();
                    trigger.parameter = name;

                    Undo.RegisterCreatedObjectUndo(newObject, "Create Studio Global Parameter Trigger");
                }

                var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
                var hit = HandleUtility.RaySnap(ray);

                if (hit != null)
                    newObject.transform.position = ((RaycastHit) hit).point;
                else
                    newObject.transform.position = ray.origin + ray.direction * 10.0f;

                Selection.activeObject = newObject;
                Event.current.Use();
            }
            else if (Event.current.type == EventType.DragUpdated && IsDroppable(DragAndDrop.objectReferences))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                DragAndDrop.AcceptDrag();
                Event.current.Use();
            }
        }

        private class TreeView : UnityEditor.IMGUI.Controls.TreeView
        {
            private const string EventPrefix = "event:/";
            private const string SnapshotPrefix = "snapshot:/";
            private const string BankPrefix = "bank:/";
            private const string ParameterPrefix = "parameter:/";

            private static readonly Texture2D folderOpenIcon =
                EditorGUIUtility.Load("FMOD/FolderIconOpen.png") as Texture2D;

            private static readonly Texture2D folderClosedIcon =
                EditorGUIUtility.Load("FMOD/FolderIconClosed.png") as Texture2D;

            private static readonly Texture2D eventIcon = EditorGUIUtility.Load("FMOD/EventIcon.png") as Texture2D;

            private static readonly Texture2D
                snapshotIcon = EditorGUIUtility.Load("FMOD/SnapshotIcon.png") as Texture2D;

            private static readonly Texture2D bankIcon = EditorGUIUtility.Load("FMOD/BankIcon.png") as Texture2D;
            private static readonly Texture2D parameterIcon = EditorGUIUtility.Load("FMOD/EventIcon.png") as Texture2D;

            private static readonly NaturalComparer naturalComparer = new NaturalComparer();

            private bool expandNextFolderSet;

            private readonly Dictionary<string, int> itemIDs = new Dictionary<string, int>();
            private string nextFramedItemPath;

            private IList<int> noSearchExpandState;

            private float oldBaseIndent;
            private string[] searchStringSplit;

            public TreeView(State state) : base(state.baseState)
            {
                noSearchExpandState = state.noSearchExpandState;
                SelectedObject = state.selectedObject;
                TypeFilter = state.typeFilter;
                DragEnabled = state.dragEnabled;

                for (var i = 0; i < state.itemPaths.Count; ++i) itemIDs.Add(state.itemPaths[i], state.itemIDs[i]);
            }

            public TypeFilter TypeFilter { get; set; }
            public bool DragEnabled { get; set; }

            public ScriptableObject SelectedObject { get; private set; }
            public ScriptableObject DoubleClickedObject { get; private set; }

            public new State state
            {
                get
                {
                    var result = new State(base.state);

                    if (noSearchExpandState != null) result.noSearchExpandState = new List<int>(noSearchExpandState);

                    result.selectedObject = SelectedObject;

                    foreach (var entry in itemIDs)
                    {
                        result.itemPaths.Add(entry.Key);
                        result.itemIDs.Add(entry.Value);
                    }

                    result.typeFilter = TypeFilter;
                    result.dragEnabled = true;

                    return result;
                }
            }

            public void JumpToEvent(string path)
            {
                JumpToItem(path);
            }

            public void JumpToBank(string name)
            {
                JumpToItem(BankPrefix + name);
            }

            private void JumpToItem(string path)
            {
                nextFramedItemPath = path;
                Reload();

                int itemID;
                if (itemIDs.TryGetValue(path, out itemID))
                    SetSelection(new List<int> {itemID},
                        TreeViewSelectionOptions.RevealAndFrame | TreeViewSelectionOptions.FireSelectionChanged);
                else
                    SetSelection(new List<int>());
            }

            private FolderItem CreateFolderItem(string name, string path, bool hasChildren, bool forceExpanded,
                TreeViewItem parent)
            {
                var item = new FolderItem(AffirmItemID("folder:" + path), 0, name);

                bool expanded;

                if (!hasChildren)
                {
                    expanded = false;
                }
                else if (forceExpanded || expandNextFolderSet
                                       || (nextFramedItemPath != null && nextFramedItemPath.StartsWith(path)))
                {
                    SetExpanded(item.id, true);
                    expanded = true;
                }
                else
                {
                    expanded = IsExpanded(item.id);
                }

                if (expanded)
                {
                    item.icon = folderOpenIcon;
                }
                else
                {
                    item.icon = folderClosedIcon;

                    if (hasChildren) item.children = CreateChildListForCollapsedParent();
                }

                parent.AddChild(item);

                return item;
            }

            protected override TreeViewItem BuildRoot()
            {
                return new TreeViewItem(-1, -1);
            }

            private int AffirmItemID(string path)
            {
                int id;

                if (!itemIDs.TryGetValue(path, out id))
                {
                    id = itemIDs.Count;
                    itemIDs.Add(path, id);
                }

                return id;
            }

            protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
            {
                if (hasSearch) searchStringSplit = searchString.Split(' ');

                if (rootItem.children != null) rootItem.children.Clear();

                if ((TypeFilter & TypeFilter.Event) != 0)
                {
                    CreateSubTree("Events", EventPrefix,
                        EventManager.Events.Where(e => e.Path.StartsWith(EventPrefix)), e => e.Path, eventIcon);

                    CreateSubTree("Snapshots", SnapshotPrefix,
                        EventManager.Events.Where(e => e.Path.StartsWith(SnapshotPrefix)), s => s.Path, snapshotIcon);
                }

                if ((TypeFilter & TypeFilter.Bank) != 0)
                    CreateSubTree("Banks", BankPrefix, EventManager.Banks, b => b.StudioPath, bankIcon);

                if ((TypeFilter & TypeFilter.Parameter) != 0)
                    CreateSubTree("Global Parameters", ParameterPrefix,
                        EventManager.Parameters, p => ParameterPrefix + p.Name, parameterIcon,
                        (path, p) => string.Format("{0}:{1:x}:{2:x}", path, p.ID.data1, p.ID.data2));

                var rows = new List<TreeViewItem>();

                AddChildrenInOrder(rows, rootItem);

                SetupDepthsFromParentsAndChildren(rootItem);

                expandNextFolderSet = false;
                nextFramedItemPath = null;

                return rows;
            }

            private void CreateSubTree<T>(string rootName, string rootPath,
                IEnumerable<T> sourceRecords, Func<T, string> GetPath,
                Texture2D icon, Func<string, T, string> MakeUniquePath = null)
                where T : ScriptableObject
            {
                var records = sourceRecords.Select(r => new {source = r, path = GetPath(r)});

                if (hasSearch)
                    records = records.Where(r =>
                    {
                        foreach (var word in searchStringSplit)
                            if (word.Length > 0 && r.path.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0)
                                return false;
                        return true;
                    });

                records = records.OrderBy(r => r.path, naturalComparer);

                TreeViewItem root =
                    CreateFolderItem(rootName, rootPath, records.Any(), TypeFilter != TypeFilter.All, rootItem);

                var currentFolderItems = new List<TreeViewItem>();

                foreach (var record in records)
                {
                    string leafName;
                    var parent = CreateFolderItems(record.path, currentFolderItems, root, out leafName);

                    if (parent != null)
                    {
                        string uniquePath;

                        if (MakeUniquePath != null)
                            uniquePath = MakeUniquePath(record.path, record.source);
                        else
                            uniquePath = record.path;

                        TreeViewItem leafItem = new LeafItem(AffirmItemID(uniquePath), 0, record.source);
                        leafItem.displayName = leafName;
                        leafItem.icon = icon;

                        parent.AddChild(leafItem);
                    }
                }
            }

            private TreeViewItem CreateFolderItems(string path, List<TreeViewItem> currentFolderItems,
                TreeViewItem root, out string leafName)
            {
                var parent = root;

                var separator = '/';

                // Skip the type prefix at the start of the path
                var elementStart = path.IndexOf(separator) + 1;

                for (var i = 0;; ++i)
                {
                    if (!IsExpanded(parent.id))
                    {
                        leafName = null;
                        return null;
                    }

                    var elementEnd = path.IndexOf(separator, elementStart);

                    if (elementEnd < 0)
                        // No more folders; elementStart points to the event name
                        break;

                    var folderName = path.Substring(elementStart, elementEnd - elementStart);

                    if (i < currentFolderItems.Count && folderName != currentFolderItems[i].displayName)
                        currentFolderItems.RemoveRange(i, currentFolderItems.Count - i);

                    if (i == currentFolderItems.Count)
                    {
                        var folderItem =
                            CreateFolderItem(folderName, path.Substring(0, elementEnd), true, false, parent);

                        currentFolderItems.Add(folderItem);
                    }

                    elementStart = elementEnd + 1;
                    parent = currentFolderItems[i];
                }

                leafName = path.Substring(elementStart);
                return parent;
            }

            private static void AddChildrenInOrder(List<TreeViewItem> list, TreeViewItem item)
            {
                if (item.children != null)
                {
                    foreach (var child in item.children.Where(child => child is FolderItem))
                    {
                        list.Add(child);

                        AddChildrenInOrder(list, child);
                    }

                    foreach (var child in item.children.Where(child => !(child == null || child is FolderItem)))
                        list.Add(child);
                }
            }

            protected override bool CanMultiSelect(TreeViewItem item)
            {
                return false;
            }

            protected override bool CanChangeExpandedState(TreeViewItem item)
            {
                return item.hasChildren;
            }

            protected override bool CanStartDrag(CanStartDragArgs args)
            {
                if (DragEnabled && args.draggedItem is LeafItem)
                    return IsDraggable((args.draggedItem as LeafItem).Data);
                return false;
            }

            protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
            {
                var items = FindRows(args.draggedItemIDs);

                if (items[0] is LeafItem)
                {
                    var item = items[0] as LeafItem;

                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = new Object[] {Instantiate(item.Data)};

                    var title = string.Empty;

                    if (item.Data is EditorEventRef)
                        title = "New FMOD Studio Emitter";
                    else if (item.Data is EditorBankRef)
                        title = "New FMOD Studio Bank Loader";
                    else if (item.Data is EditorParamRef) title = "New FMOD Studio Global Parameter Trigger";

                    DragAndDrop.StartDrag(title);
                }
            }

            protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
            {
                return DragAndDropVisualMode.None;
            }

            protected override void SearchChanged(string newSearch)
            {
                if (!string.IsNullOrEmpty(newSearch.Trim()))
                {
                    expandNextFolderSet = true;

                    if (noSearchExpandState == null)
                    {
                        // A new search is beginning
                        noSearchExpandState = GetExpanded();
                        SetExpanded(new List<int>());
                    }
                }
                else
                {
                    if (noSearchExpandState != null)
                    {
                        // A search is ending
                        SetExpanded(noSearchExpandState);
                        noSearchExpandState = null;
                    }
                }
            }

            protected override void SelectionChanged(IList<int> selectedIDs)
            {
                SelectedObject = null;

                if (selectedIDs.Count > 0)
                {
                    var item = FindItem(selectedIDs[0], rootItem);

                    if (item is LeafItem) SelectedObject = (item as LeafItem).Data;
                }
            }

            protected override void DoubleClickedItem(int id)
            {
                var item = FindItem(id, rootItem);

                if (item is LeafItem) DoubleClickedObject = (item as LeafItem).Data;
            }

            protected override void BeforeRowsGUI()
            {
                oldBaseIndent = baseIndent;
                DoubleClickedObject = null;
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                if (hasSearch)
                    // Hack to undo TreeView flattening the hierarchy when searching
                    baseIndent = oldBaseIndent + args.item.depth * depthIndentWidth;

                base.RowGUI(args);

                var item = args.item;

                if (Event.current.type == EventType.MouseUp && item is FolderItem && item.hasChildren)
                {
                    var rect = args.rowRect;
                    rect.xMin = GetContentIndent(item);

                    if (rect.Contains(Event.current.mousePosition))
                    {
                        SetExpanded(item.id, !IsExpanded(item.id));
                        Event.current.Use();
                    }
                }
            }

            protected override void AfterRowsGUI()
            {
                baseIndent = oldBaseIndent;
            }

            private class LeafItem : TreeViewItem
            {
                public readonly ScriptableObject Data;

                public LeafItem(int id, int depth, ScriptableObject data)
                    : base(id, depth)
                {
                    Data = data;
                }
            }

            private class FolderItem : TreeViewItem
            {
                public FolderItem(int id, int depth, string displayName)
                    : base(id, depth, displayName)
                {
                }
            }

            [Serializable]
            public class State
            {
                public TreeViewState baseState;
                public List<int> noSearchExpandState;
                public ScriptableObject selectedObject;
                public List<string> itemPaths = new List<string>();
                public List<int> itemIDs = new List<int>();
                public TypeFilter typeFilter = TypeFilter.All;
                public bool dragEnabled = true;

                public State() : this(new TreeViewState())
                {
                }

                public State(TreeViewState baseState)
                {
                    this.baseState = baseState;
                }
            }
        }

        [Serializable]
        private class PreviewArea
        {
            [SerializeField] private DetailsView detailsView = new DetailsView();

            [SerializeField] private TransportControls transportControls = new TransportControls();

            [SerializeField] private Event3DPreview event3DPreview = new Event3DPreview();

            [SerializeField] private PreviewMeters meters = new PreviewMeters();

            [SerializeField] private EventParameterControls parameterControls = new EventParameterControls();

            [NonSerialized] private EditorEventRef currentEvent;

            private bool isNarrow;

            private GUIStyle mainStyle;

            [NonSerialized] public TreeView treeView;

            public bool forceRepaint => transportControls.forceRepaint;

            private void SetEvent(EditorEventRef eventRef)
            {
                if (eventRef != currentEvent)
                {
                    currentEvent = eventRef;

                    EditorUtils.PreviewStop();
                    transportControls.Reset();
                    event3DPreview.Reset();
                    parameterControls.Reset();
                }
            }

            private void AffirmResources()
            {
                if (mainStyle == null)
                {
                    mainStyle = new GUIStyle(GUI.skin.box);
                    mainStyle.margin = new RectOffset();
                }
            }

            public void OnGUI(float[] metering)
            {
                AffirmResources();

                var selectedObject = treeView.SelectedObject;

                if (selectedObject is EditorEventRef)
                    SetEvent(selectedObject as EditorEventRef);
                else
                    SetEvent(null);

                if (selectedObject != null)
                {
                    GUILayout.BeginVertical(mainStyle, GUILayout.ExpandWidth(true));

                    if (selectedObject is EditorEventRef)
                    {
                        var eventRef = selectedObject as EditorEventRef;

                        if (eventRef.Path.StartsWith("event:"))
                            DrawEventPreview(eventRef, metering);
                        else if (eventRef.Path.StartsWith("snapshot:")) detailsView.DrawSnapshot(eventRef);
                    }
                    else if (selectedObject is EditorBankRef)
                    {
                        detailsView.DrawBank(selectedObject as EditorBankRef);
                    }
                    else if (selectedObject is EditorParamRef)
                    {
                        detailsView.DrawParameter(selectedObject as EditorParamRef);
                    }

                    GUILayout.EndVertical();

                    if (Event.current.type == EventType.Repaint)
                    {
                        var rect = GUILayoutUtility.GetLastRect();
                        isNarrow = rect.width < 600;
                    }
                }
            }

            private void DrawSeparatorLine()
            {
                GUILayout.Box(GUIContent.none, GUILayout.Height(1), GUILayout.ExpandWidth(true));
            }

            private void DrawEventPreview(EditorEventRef eventRef, float[] metering)
            {
                detailsView.DrawEvent(eventRef, isNarrow);

                DrawSeparatorLine();

                // Playback controls, 3D Preview and meters
                EditorGUILayout.BeginHorizontal(GUILayout.Height(event3DPreview.Height));
                GUILayout.FlexibleSpace();

                EditorGUILayout.BeginVertical();

                if (!isNarrow) GUILayout.FlexibleSpace();

                transportControls.OnGUI(eventRef, parameterControls.ParameterValues);

                if (isNarrow)
                {
                    EditorGUILayout.Separator();
                    meters.OnGUI(true, metering);
                }
                else
                {
                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.EndVertical();

                event3DPreview.OnGUI(eventRef);

                if (!isNarrow) meters.OnGUI(false, metering);

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                DrawSeparatorLine();

                parameterControls.OnGUI(eventRef);
            }
        }

        [Serializable]
        private class DetailsView
        {
            private Texture copyIcon;
            private GUIStyle textFieldNameStyle;

            private void AffirmResources()
            {
                if (copyIcon == null)
                {
                    copyIcon = EditorGUIUtility.Load("FMOD/CopyIcon.png") as Texture;

                    textFieldNameStyle = new GUIStyle(EditorStyles.label);
                    textFieldNameStyle.fontStyle = FontStyle.Bold;
                }
            }

            public void DrawEvent(EditorEventRef selectedEvent, bool isNarrow)
            {
                AffirmResources();

                DrawCopyableTextField("Full Path", selectedEvent.Path);

                DrawTextField("Banks", string.Join(", ", selectedEvent.Banks.Select(x => x.Name).ToArray()));

                EditorGUILayout.BeginHorizontal();
                DrawTextField("Panning", selectedEvent.Is3D ? "3D" : "2D");
                DrawTextField("Oneshot", selectedEvent.IsOneShot.ToString());

                var t = TimeSpan.FromMilliseconds(selectedEvent.Length);
                DrawTextField("Length",
                    selectedEvent.Length > 0
                        ? string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds)
                        : "N/A");

                if (!isNarrow) DrawTextField("Streaming", selectedEvent.IsStream.ToString());
                EditorGUILayout.EndHorizontal();
                if (isNarrow) DrawTextField("Streaming", selectedEvent.IsStream.ToString());
            }

            public void DrawSnapshot(EditorEventRef eventRef)
            {
                AffirmResources();

                DrawCopyableTextField("Full Path", eventRef.Path);
            }

            public void DrawBank(EditorBankRef bank)
            {
                AffirmResources();

                DrawCopyableTextField("Full Path", "bank:/" + bank.Name);

                string[] SizeSuffix = {"B", "KB", "MB", "GB"};

                GUILayout.Label("Platform Bank Sizes", textFieldNameStyle);

                EditorGUI.indentLevel++;

                foreach (var sizeInfo in bank.FileSizes)
                {
                    var order = 0;
                    var size = sizeInfo.Value;

                    while (size >= 1024 && order + 1 < SizeSuffix.Length)
                    {
                        order++;
                        size /= 1024;
                    }

                    EditorGUILayout.LabelField(sizeInfo.Name, string.Format("{0} {1}", size, SizeSuffix[order]));
                }

                EditorGUI.indentLevel--;
            }

            public void DrawParameter(EditorParamRef parameter)
            {
                AffirmResources();

                DrawCopyableTextField("Name", parameter.Name);
                DrawCopyableTextField("ID",
                    string.Format("{{ data1 = 0x{0:x8}, data2 = 0x{1:x8} }}", parameter.ID.data1, parameter.ID.data2));
                DrawTextField("Minimum", parameter.Min.ToString());
                DrawTextField("Maximum", parameter.Max.ToString());
            }

            private void DrawCopyableTextField(string name, string value)
            {
                EditorGUILayout.BeginHorizontal();
                DrawTextField(name, value);
                if (GUILayout.Button(copyIcon, GUILayout.ExpandWidth(false))) EditorGUIUtility.systemCopyBuffer = value;
                EditorGUILayout.EndHorizontal();
            }

            private void DrawTextField(string name, string content)
            {
                EditorGUILayout.BeginHorizontal();

                GUILayout.Label(name, textFieldNameStyle, GUILayout.Width(75));
                GUILayout.Label(content);

                EditorGUILayout.EndHorizontal();
            }
        }

        [Serializable]
        private class TransportControls
        {
            private GUIStyle buttonStyle;
            private Texture openIcon;

            private Texture playOff;
            private Texture playOn;
            private Texture stopOff;
            private Texture stopOn;
            public bool forceRepaint { get; private set; }

            public void Reset()
            {
                forceRepaint = false;
            }

            private void AffirmResources()
            {
                if (playOff == null)
                {
                    playOff = EditorGUIUtility.Load("FMOD/TransportPlayButtonOff.png") as Texture;
                    playOn = EditorGUIUtility.Load("FMOD/TransportPlayButtonOn.png") as Texture;
                    stopOff = EditorGUIUtility.Load("FMOD/TransportStopButtonOff.png") as Texture;
                    stopOn = EditorGUIUtility.Load("FMOD/TransportStopButtonOn.png") as Texture;
                    openIcon = EditorGUIUtility.Load("FMOD/transportOpen.png") as Texture;

                    buttonStyle = new GUIStyle();
                    buttonStyle.padding.left = 4;
                    buttonStyle.padding.top = 10;
                }
            }

            public void OnGUI(EditorEventRef selectedEvent, Dictionary<string, float> parameterValues)
            {
                AffirmResources();

                var previewState = EditorUtils.PreviewState;
                var playing = previewState == PreviewState.Playing;
                var paused = previewState == PreviewState.Paused;
                var stopped = previewState == PreviewState.Stopped;

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button(stopped || paused ? stopOn : stopOff, buttonStyle, GUILayout.ExpandWidth(false)))
                {
                    forceRepaint = false;

                    if (paused) EditorUtils.PreviewStop();
                    if (playing) EditorUtils.PreviewPause();
                }

                if (GUILayout.Button(playing ? playOn : playOff, buttonStyle, GUILayout.ExpandWidth(false)))
                {
                    if (playing || stopped)
                        EditorUtils.PreviewEvent(selectedEvent, parameterValues);
                    else
                        EditorUtils.PreviewPause();

                    forceRepaint = true;
                }

                if (GUILayout.Button(new GUIContent(openIcon, "Show Event in FMOD Studio"), buttonStyle,
                        GUILayout.ExpandWidth(false)))
                {
                    var cmd = string.Format("studio.window.navigateTo(studio.project.lookup(\"{0}\"))",
                        selectedEvent.Guid.ToString("b"));
                    EditorUtils.SendScriptCommand(cmd);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        [Serializable]
        private class Event3DPreview
        {
            private Texture arena;
            private Rect arenaRect;
            private Texture emitter;
            private float eventDistance;
            private float eventOrientation;

            private Vector2 eventPosition;
            private bool isDragging;

            public float Height
            {
                get
                {
                    AffirmResources();
                    return GUI.skin.label.CalcSize(new GUIContent(arena)).y;
                }
            }

            public void Reset()
            {
                eventPosition = new Vector2(0, 0);
                eventDistance = 0;
                eventOrientation = 0;
            }

            private void AffirmResources()
            {
                if (arena == null)
                {
                    arena = EditorGUIUtility.Load("FMOD/preview.png") as Texture;
                    emitter = EditorGUIUtility.Load("FMOD/previewemitter.png") as Texture;
                }
            }

            public void OnGUI(EditorEventRef selectedEvent)
            {
                AffirmResources();

                var originalColour = GUI.color;
                if (!selectedEvent.Is3D) GUI.color = new Color(1.0f, 1.0f, 1.0f, 0.1f);

                GUILayout.Label(arena, GUILayout.ExpandWidth(false));

                if (Event.current.type == EventType.Repaint) arenaRect = GUILayoutUtility.GetLastRect();

                var center = arenaRect.center;
                var rect2 = new Rect(center.x + eventPosition.x - 6, center.y + eventPosition.y - 6, 12, 12);
                GUI.DrawTexture(rect2, emitter);

                GUI.color = originalColour;

                if (selectedEvent.Is3D)
                {
                    var useGUIEvent = false;

                    switch (Event.current.type)
                    {
                        case EventType.MouseDown:
                            if (arenaRect.Contains(Event.current.mousePosition))
                            {
                                isDragging = true;
                                useGUIEvent = true;
                            }

                            break;
                        case EventType.MouseUp:
                            if (isDragging)
                            {
                                isDragging = false;
                                useGUIEvent = true;
                            }

                            break;
                        case EventType.MouseDrag:
                            if (isDragging) useGUIEvent = true;
                            break;
                    }

                    if (useGUIEvent)
                    {
                        var newPosition = Event.current.mousePosition;
                        var delta = newPosition - center;

                        float maximumDistance = (arena.width - emitter.width) / 2;
                        var distance = Math.Min(delta.magnitude, maximumDistance);

                        delta.Normalize();
                        eventPosition = delta * distance;
                        eventDistance = distance / maximumDistance * selectedEvent.MaxDistance;

                        var angle = Mathf.Atan2(delta.y, delta.x);
                        eventOrientation = angle + Mathf.PI * 0.5f;

                        Event.current.Use();
                    }
                }

                EditorUtils.PreviewUpdatePosition(eventDistance, eventOrientation);
            }
        }

        [Serializable]
        private class EventParameterControls
        {
            [NonSerialized] private Dictionary<string, float> parameterValues = new Dictionary<string, float>();

            [NonSerialized] private Vector2 scrollPosition;

            public Dictionary<string, float> ParameterValues => parameterValues;

            public void Reset()
            {
                parameterValues.Clear();
            }

            public void OnGUI(EditorEventRef selectedEvent)
            {
                scrollPosition = GUILayout.BeginScrollView(scrollPosition,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight * 3.5f));

                foreach (var paramRef in selectedEvent.Parameters)
                {
                    if (!parameterValues.ContainsKey(paramRef.Name)) parameterValues[paramRef.Name] = paramRef.Default;

                    parameterValues[paramRef.Name] = EditorGUILayout.Slider(paramRef.Name,
                        parameterValues[paramRef.Name], paramRef.Min, paramRef.Max);

                    EditorUtils.PreviewUpdateParameter(paramRef.ID, parameterValues[paramRef.Name]);
                }

                GUILayout.EndScrollView();
            }
        }

        [Serializable]
        private class PreviewMeters
        {
            private Texture meterOff;
            private Texture meterOn;

            private void AffirmResources()
            {
                if (meterOn == null)
                {
                    meterOn = EditorGUIUtility.Load("FMOD/LevelMeter.png") as Texture;
                    meterOff = EditorGUIUtility.Load("FMOD/LevelMeterOff.png") as Texture;
                }
            }

            public void OnGUI(bool minimized, float[] metering)
            {
                AffirmResources();

                var meterHeight = minimized ? 86 : 128;
                var meterWidth = (int) (128 / (float) meterOff.height * meterOff.width);

                var meterPositions =
                    meterPositionsForSpeakerMode(speakerModeForChannelCount(metering.Length), meterWidth, 2, 6);

                const int MeterCountMaximum = 16;

                var minimumWidth = meterWidth * MeterCountMaximum;

                var fullRect = GUILayoutUtility.GetRect(minimumWidth, meterHeight,
                    GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

                var baseX = fullRect.x + (fullRect.width - meterWidth * metering.Length) / 2;

                for (var i = 0; i < metering.Length; i++)
                {
                    var meterRect = new Rect(baseX + meterPositions[i], fullRect.y, meterWidth, fullRect.height);

                    GUI.DrawTexture(meterRect, meterOff);

                    var db = 20.0f * Mathf.Log10(metering[i] * Mathf.Sqrt(2.0f));
                    db = Mathf.Clamp(db, -80.0f, 10.0f);
                    float visible = 0;
                    int[] segmentPixels = {0, 18, 38, 60, 89, 130, 187, 244, 300};
                    float[] segmentDB = {-80.0f, -60.0f, -50.0f, -40.0f, -30.0f, -20.0f, -10.0f, 0, 10.0f};
                    var segment = 1;
                    while (segmentDB[segment] < db) segment++;
                    visible = segmentPixels[segment - 1] + (db - segmentDB[segment - 1]) /
                        (segmentDB[segment] - segmentDB[segment - 1]) *
                        (segmentPixels[segment] - segmentPixels[segment - 1]);

                    visible *= fullRect.height / meterOff.height;

                    var levelPosRect = new Rect(meterRect.x, fullRect.height - visible + meterRect.y, meterWidth,
                        visible);
                    var levelUVRect = new Rect(0, 0, 1.0f, visible / fullRect.height);
                    GUI.DrawTextureWithTexCoords(levelPosRect, meterOn, levelUVRect);
                }
            }

            private SPEAKERMODE speakerModeForChannelCount(int channelCount)
            {
                switch (channelCount)
                {
                    case 1:
                        return SPEAKERMODE.MONO;
                    case 4:
                        return SPEAKERMODE.QUAD;
                    case 5:
                        return SPEAKERMODE.SURROUND;
                    case 6:
                        return SPEAKERMODE._5POINT1;
                    case 8:
                        return SPEAKERMODE._7POINT1;
                    case 12:
                        return SPEAKERMODE._7POINT1POINT4;
                    default:
                        return SPEAKERMODE.STEREO;
                }
            }

            private List<float> meterPositionsForSpeakerMode(SPEAKERMODE mode, float meterWidth, float groupGap,
                float lfeGap)
            {
                var offsets = new List<float>();

                switch (mode)
                {
                    case SPEAKERMODE.MONO: // M
                        offsets.Add(0);
                        break;

                    case SPEAKERMODE.STEREO: // L R
                        offsets.Add(0);
                        offsets.Add(meterWidth);
                        break;

                    case SPEAKERMODE.QUAD:
                        switch (Settings.Instance.MeterChannelOrdering)
                        {
                            case MeterChannelOrderingType.Standard:
                            case MeterChannelOrderingType.SeparateLFE: // L R | LS RS
                                offsets.Add(0); // L
                                offsets.Add(meterWidth * 1); // R
                                offsets.Add(meterWidth * 2 + groupGap); // LS
                                offsets.Add(meterWidth * 3 + groupGap); // RS
                                break;
                            case MeterChannelOrderingType.Positional: // LS | L R | RS
                                offsets.Add(meterWidth * 1 + groupGap); // L
                                offsets.Add(meterWidth * 2 + groupGap); // R
                                offsets.Add(0); // LS
                                offsets.Add(meterWidth * 3 + groupGap * 2); // RS
                                break;
                        }

                        break;

                    case SPEAKERMODE.SURROUND:
                        switch (Settings.Instance.MeterChannelOrdering)
                        {
                            case MeterChannelOrderingType.Standard:
                            case MeterChannelOrderingType.SeparateLFE: // L R | C | LS RS
                                offsets.Add(0); // L
                                offsets.Add(meterWidth * 1); // R
                                offsets.Add(meterWidth * 2 + groupGap); // C
                                offsets.Add(meterWidth * 3 + groupGap * 2); // LS
                                offsets.Add(meterWidth * 4 + groupGap * 2); // RS
                                break;
                            case MeterChannelOrderingType.Positional: // LS | L C R | RS
                                offsets.Add(meterWidth * 1 + groupGap); // L
                                offsets.Add(meterWidth * 3 + groupGap); // R
                                offsets.Add(meterWidth * 2 + groupGap); // C
                                offsets.Add(0); // LS
                                offsets.Add(meterWidth * 4 + groupGap * 2); // RS
                                break;
                        }

                        break;

                    case SPEAKERMODE._5POINT1:
                        switch (Settings.Instance.MeterChannelOrdering)
                        {
                            case MeterChannelOrderingType.Standard: // L R | C | LFE | LS RS
                                offsets.Add(0); // L
                                offsets.Add(meterWidth * 1); // R
                                offsets.Add(meterWidth * 2 + groupGap); // C
                                offsets.Add(meterWidth * 3 + groupGap * 2); // LFE
                                offsets.Add(meterWidth * 4 + groupGap * 3); // LS
                                offsets.Add(meterWidth * 5 + groupGap * 3); // RS
                                break;
                            case MeterChannelOrderingType.SeparateLFE: // L R | C | LS RS || LFE
                                offsets.Add(0); // L
                                offsets.Add(meterWidth * 1); // R
                                offsets.Add(meterWidth * 2 + groupGap); // C
                                offsets.Add(meterWidth * 5 + groupGap * 2 + lfeGap); // LFE
                                offsets.Add(meterWidth * 3 + groupGap * 2); // LS
                                offsets.Add(meterWidth * 4 + groupGap * 2); // RS
                                break;
                            case MeterChannelOrderingType.Positional: // LS | L C R | RS || LFE
                                offsets.Add(meterWidth * 1 + groupGap); // L
                                offsets.Add(meterWidth * 3 + groupGap); // R
                                offsets.Add(meterWidth * 2 + groupGap); // C
                                offsets.Add(meterWidth * 5 + groupGap * 2 + lfeGap); // LFE
                                offsets.Add(0); // LS
                                offsets.Add(meterWidth * 4 + groupGap * 2); // RS
                                break;
                        }

                        break;

                    case SPEAKERMODE._7POINT1:
                        switch (Settings.Instance.MeterChannelOrdering)
                        {
                            case MeterChannelOrderingType.Standard: // L R | C | LFE | LS RS | LSR RSR
                                offsets.Add(0); // L
                                offsets.Add(meterWidth * 1); // R
                                offsets.Add(meterWidth * 2 + groupGap); // C
                                offsets.Add(meterWidth * 3 + groupGap * 2); // LFE
                                offsets.Add(meterWidth * 4 + groupGap * 3); // LS
                                offsets.Add(meterWidth * 5 + groupGap * 3); // RS
                                offsets.Add(meterWidth * 6 + groupGap * 4); // LSR
                                offsets.Add(meterWidth * 7 + groupGap * 4); // RSR
                                break;
                            case MeterChannelOrderingType.SeparateLFE: // L R | C | LS RS | LSR RSR || LFE
                                offsets.Add(0); // L
                                offsets.Add(meterWidth * 1); // R
                                offsets.Add(meterWidth * 2 + groupGap); // C
                                offsets.Add(meterWidth * 7 + groupGap * 3 + lfeGap); // LFE
                                offsets.Add(meterWidth * 3 + groupGap * 2); // LS
                                offsets.Add(meterWidth * 4 + groupGap * 2); // RS
                                offsets.Add(meterWidth * 5 + groupGap * 3); // LSR
                                offsets.Add(meterWidth * 6 + groupGap * 3); // RSR
                                break;
                            case MeterChannelOrderingType.Positional: // LSR LS | L C R | RS RSR || LFE
                                offsets.Add(meterWidth * 2 + groupGap); // L
                                offsets.Add(meterWidth * 4 + groupGap); // R
                                offsets.Add(meterWidth * 3 + groupGap); // C
                                offsets.Add(meterWidth * 7 + groupGap * 2 + lfeGap); // LFE
                                offsets.Add(meterWidth * 1); // LS
                                offsets.Add(meterWidth * 5 + groupGap * 2); // RS
                                offsets.Add(0); // LSR
                                offsets.Add(meterWidth * 6 + groupGap * 2); // RSR
                                break;
                        }

                        break;

                    case SPEAKERMODE._7POINT1POINT4:
                        switch (Settings.Instance.MeterChannelOrdering)
                        {
                            case MeterChannelOrderingType.Standard: // L R | C | LFE | LS RS | LSR RSR | TFL TFR TBL TBR
                                offsets.Add(0); // L
                                offsets.Add(meterWidth * 1); // R
                                offsets.Add(meterWidth * 2 + groupGap); // C
                                offsets.Add(meterWidth * 3 + groupGap * 2); // LFE
                                offsets.Add(meterWidth * 4 + groupGap * 3); // LS
                                offsets.Add(meterWidth * 5 + groupGap * 3); // RS
                                offsets.Add(meterWidth * 6 + groupGap * 4); // LSR
                                offsets.Add(meterWidth * 7 + groupGap * 4); // RSR
                                offsets.Add(meterWidth * 8 + groupGap * 5); // TFL
                                offsets.Add(meterWidth * 9 + groupGap * 5); // TFR
                                offsets.Add(meterWidth * 10 + groupGap * 5); // TBL
                                offsets.Add(meterWidth * 11 + groupGap * 5); // TBR
                                break;
                            case MeterChannelOrderingType.SeparateLFE
                                : // L R | C | LS RS | LSR RSR | TFL TFR TBL TBR || LFE
                                offsets.Add(0); // L
                                offsets.Add(meterWidth * 1); // R
                                offsets.Add(meterWidth * 2 + groupGap); // C
                                offsets.Add(meterWidth * 11 + groupGap * 4 + lfeGap); // LFE
                                offsets.Add(meterWidth * 3 + groupGap * 2); // LS
                                offsets.Add(meterWidth * 4 + groupGap * 2); // RS
                                offsets.Add(meterWidth * 5 + groupGap * 3); // LSR
                                offsets.Add(meterWidth * 6 + groupGap * 3); // RSR
                                offsets.Add(meterWidth * 7 + groupGap * 4); // TFL
                                offsets.Add(meterWidth * 8 + groupGap * 4); // TFR
                                offsets.Add(meterWidth * 9 + groupGap * 4); // TBL
                                offsets.Add(meterWidth * 10 + groupGap * 4); // TBR
                                break;
                            case MeterChannelOrderingType.Positional
                                : // LSR LS | L C R | RS RSR | TBL TFL TFR TBR || LFE
                                offsets.Add(meterWidth * 2 + groupGap); // L
                                offsets.Add(meterWidth * 4 + groupGap); // R
                                offsets.Add(meterWidth * 3 + groupGap); // C
                                offsets.Add(meterWidth * 11 + groupGap * 3 + lfeGap); // LFE
                                offsets.Add(meterWidth * 1); // LS
                                offsets.Add(meterWidth * 5 + groupGap * 2); // RS
                                offsets.Add(0); // LSR
                                offsets.Add(meterWidth * 6 + groupGap * 2); // RSR
                                offsets.Add(meterWidth * 8 + groupGap * 3); // TFL
                                offsets.Add(meterWidth * 9 + groupGap * 3); // TFR
                                offsets.Add(meterWidth * 7 + groupGap * 3); // TBL
                                offsets.Add(meterWidth * 10 + groupGap * 3); // TBR
                                break;
                        }

                        break;
                }

                return offsets;
            }
        }

        [Flags]
        private enum TypeFilter
        {
            Event = 1,
            Bank = 2,
            Parameter = 4,
            All = Event | Bank | Parameter
        }
    }
}