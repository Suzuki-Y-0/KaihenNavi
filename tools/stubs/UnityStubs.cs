// Unity なしでコンパイル確認をするための最小限のスタブ。
// かいへんナビが使う API の「形」だけを真似ている。配布物には含めない。
#pragma warning disable 0067, 0414, 0649, 1591
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }

    public struct Rect { public float width; public float height; }

    public class Object
    {
        public string name;
        public HideFlags hideFlags;
        public int GetInstanceID() { return 0; }
        public static T Instantiate<T>(T original) where T : Object { return original; }
        public static implicit operator bool(Object o) { return !ReferenceEquals(o, null); }
    }

    [Flags] public enum HideFlags { None = 0, HideInHierarchy = 1, DontSave = 52, HideAndDontSave = 61 }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform;
        public T GetComponent<T>() { return default(T); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) { return new T[0]; }
    }

    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { }

    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public Transform transform;
        public bool activeSelf;
        public bool activeInHierarchy;
        public void SetActive(bool value) { }
        public T GetComponent<T>() { return default(T); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) { return new T[0]; }
    }

    public class Transform : Component, System.Collections.IEnumerable
    {
        public Transform parent;
        public int childCount;
        public Vector3 position, localPosition, localScale, up, right, forward;
        public Quaternion rotation, localRotation;
        public Transform GetChild(int index) { return null; }
        public Transform Find(string n) { return null; }
        public bool IsChildOf(Transform p) { return false; }
        public void SetParent(Transform p, bool worldPositionStays) { }
        public Vector3 TransformDirection(Vector3 v) { return v; }
        public System.Collections.IEnumerator GetEnumerator() { yield break; }
    }

    public class Renderer : Component { public Material[] sharedMaterials; }
    public class MeshRenderer : Renderer { }
    public class SkinnedMeshRenderer : Renderer
    {
        public Mesh sharedMesh;
        public Transform rootBone;
        public Transform[] bones;
        public float GetBlendShapeWeight(int i) { return 0; }
        public void SetBlendShapeWeight(int i, float v) { }
    }

    public class Mesh : Object { public int blendShapeCount; }
    public class Texture : Object { public TextureWrapMode wrapMode; }
    public enum TextureFormat { RGBA32 }
    public enum TextureWrapMode { Repeat, Clamp }
    public class Texture2D : Texture
    {
        public Texture2D(int w, int h, TextureFormat f, bool mip) { }
        public void SetPixel(int x, int y, Color c) { }
        public void Apply() { }
    }

    public class Material : Object
    {
        public Material(Material m) { }
        public Texture mainTexture;
        public Color color;
        public bool HasProperty(string n) { return false; }
        public Vector4 GetVector(string n) { return default(Vector4); }
        public void SetVector(string n, Vector4 v) { }
        public Color GetColor(string n) { return default(Color); }
        public void SetColor(string n, Color c) { }
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject { return null; }
    }

    public class TextAsset : Object { public string text; }

    public class Animator : Behaviour
    {
        public bool isHuman;
        public Transform GetBoneTransform(HumanBodyBones b) { return null; }
    }

    public enum HumanBodyBones { Hips = 0, Head = 10, LastBone = 55 }

    public struct Vector2 { public Vector2(float x, float y) { } }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3 normalized { get { return this; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a) { return a; }
        public static Vector3 operator *(Vector3 a, float d) { return a; }
        public static bool operator ==(Vector3 a, Vector3 b) { return true; }
        public static bool operator !=(Vector3 a, Vector3 b) { return false; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Quaternion
    {
        public static Quaternion AngleAxis(float a, Vector3 axis) { return default(Quaternion); }
        public static Quaternion operator *(Quaternion a, Quaternion b) { return a; }
        public static bool operator ==(Quaternion a, Quaternion b) { return true; }
        public static bool operator !=(Quaternion a, Quaternion b) { return false; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public static Color gray, white;
        public static void RGBToHSV(Color c, out float h, out float s, out float v) { h = s = v = 0; }
        public static Color HSVToRGB(float h, float s, float v) { return default(Color); }
        public static Color HSVToRGB(float h, float s, float v, bool hdr) { return default(Color); }
    }

    public static class JsonUtility
    {
        public static string ToJson(object o) { return ""; }
        public static T FromJson<T>(string json) { return default(T); }
    }

    public static class ColorUtility { public static bool TryParseHtmlString(string s, out Color c) { c = default(Color); return true; } }

    public static class Mathf
    {
        public static float Max(float a, float b) { return a; }
        public static int Max(int a, int b) { return a; }
        public static int Min(int a, int b) { return a; }
        public static float Clamp(float v, float a, float b) { return v; }
        public static float Clamp01(float v) { return v; }
        public static float Abs(float v) { return v; }
        public static float Repeat(float t, float l) { return t; }
        public static bool Approximately(float a, float b) { return true; }
    }

    public static class Debug
    {
        public static void Log(object m) { }
        public static void LogWarning(object m) { }
        public static void LogError(object m) { }
        public static void LogException(Exception e) { }
    }

    public enum SystemLanguage { Chinese, English, Japanese, Korean, ChineseSimplified, ChineseTraditional, Unknown }

    public static class Application
    {
        public static SystemLanguage systemLanguage;
        public static void OpenURL(string url) { }
    }

    public class GUIContent
    {
        public GUIContent(string text) { }
    }

    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public class AddComponentMenu : Attribute { public AddComponentMenu(string s) { } }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public bool isLoaded;
        public UnityEngine.GameObject[] GetRootGameObjects() { return null; }
    }

    public static class SceneManager
    {
        public static int sceneCount;
        public static Scene GetSceneAt(int i) { return default(Scene); }
    }
}

namespace UnityEngine.UIElements
{
    public struct StyleBackground
    {
        public StyleBackground(UnityEngine.Texture2D t) { }
    }

    public struct StyleColor
    {
        public static implicit operator StyleColor(UnityEngine.Color c) { return default(StyleColor); }
    }

    public struct StyleLength
    {
        public static implicit operator StyleLength(float f) { return default(StyleLength); }
    }

    public enum DisplayStyle { Flex, None }

    public struct StyleEnum<T> where T : struct
    {
        public static implicit operator StyleEnum<T>(T v) { return default(StyleEnum<T>); }
    }

    public struct FontDefinition
    {
        public static FontDefinition FromSDFFont(UnityEngine.TextCore.Text.FontAsset f) { return default(FontDefinition); }
    }

    public struct StyleFontDefinition
    {
        public static implicit operator StyleFontDefinition(FontDefinition f) { return default(StyleFontDefinition); }
    }

    public interface IStyle
    {
        StyleFontDefinition unityFontDefinition { get; set; }
        StyleEnum<UnityEngine.FontStyle> unityFontStyleAndWeight { get; set; }
        StyleEnum<DisplayStyle> display { get; set; }
        StyleColor backgroundColor { get; set; }
        StyleBackground backgroundImage { get; set; }
        StyleLength paddingLeft { get; set; }
        StyleLength minHeight { get; set; }
    }

    public class StyleSheet : UnityEngine.ScriptableObject { }

    public class VisualElementStyleSheetSet
    {
        public bool Contains(StyleSheet s) { return false; }
        public void Add(StyleSheet s) { }
    }

    public abstract class EventBase { }
    public class MouseDownEvent : EventBase { public int clickCount; }
    public class FocusOutEvent : EventBase { }
    public delegate void EventCallback<T>(T evt);

    public interface IVisualElementScheduledItem
    {
        IVisualElementScheduledItem Every(long intervalMs);
        IVisualElementScheduledItem StartingIn(long delayMs);
        void Pause();
    }

    public interface IVisualElementScheduler
    {
        IVisualElementScheduledItem Execute(Action timerUpdateEvent);
    }

    public interface IResolvedStyle
    {
        UnityEngine.FontStyle unityFontStyleAndWeight { get; }
    }

    public struct UQueryBuilder<T> where T : VisualElement
    {
        public void ForEach(Action<T> funcCall) { }
    }

    public static class UQueryExtensions
    {
        public static UQueryBuilder<T> Query<T>(this VisualElement e) where T : VisualElement { return default(UQueryBuilder<T>); }
    }

    public class VisualElement
    {
        public UnityEngine.Rect layout;
        public IStyle style;
        public IResolvedStyle resolvedStyle;
        public IVisualElementScheduler schedule;
        public bool ClassListContains(string c) { return false; }
        public string tooltip;
        public VisualElementStyleSheetSet styleSheets;
        public void Add(VisualElement child) { }
        public void Clear() { }
        public void AddToClassList(string c) { }
        public void EnableInClassList(string c, bool enable) { }
        public void SetEnabled(bool value) { }
        public void RegisterCallback<T>(EventCallback<T> callback) where T : EventBase { }
    }

    public class ChangeEvent<T> : EventBase
    {
        public T previousValue;
        public T newValue;
    }

    public abstract class BaseField<T> : VisualElement
    {
        public T value;
        public void SetValueWithoutNotify(T v) { }
    }

    public static class INotifyValueChangedExtensions
    {
        public static bool RegisterValueChangedCallback<T>(this BaseField<T> control, EventCallback<ChangeEvent<T>> callback) { return true; }
    }

    public class TextElement : VisualElement { public string text; }
    public class Label : TextElement { public Label() { } public Label(string text) { } }
    public class Button : TextElement { public Button() { } public Button(Action clickEvent) { } }
    public class Toggle : BaseField<bool> { }
    public class TextField : BaseField<string> { }
    public class Slider : BaseField<float>
    {
        public Slider(string label, float start, float end) { }
        public bool showInputField;
    }

    public class DropdownField : BaseField<string>
    {
        public DropdownField(List<string> choices, int defaultIndex) { }
        public DropdownField(string label, List<string> choices, int defaultIndex) { }
    }

    public enum ScrollViewMode { Vertical, Horizontal }
    public class ScrollView : VisualElement { public ScrollView(ScrollViewMode mode) { } public VisualElement contentViewport; }
    public class GeometryChangedEvent : EventBase { }
}

namespace UnityEditor.UIElements
{
    public class PopupField<T> : UnityEngine.UIElements.BaseField<T>
    {
        public PopupField(List<T> choices, int defaultIndex) { }
    }
}

namespace UnityEditor
{
    using UnityEngine;

    public class EditorWindow : ScriptableObject
    {
        public GUIContent titleContent;
        public Vector2 minSize;
        public UnityEngine.UIElements.VisualElement rootVisualElement;
        public static T GetWindow<T>() where T : EditorWindow { return null; }
        public void Show() { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class MenuItem : Attribute { public MenuItem(string path, bool validate, int priority) { } }

    public static class EditorPrefs
    {
        public static string GetString(string k, string d) { return d; }
        public static void SetString(string k, string v) { }
    }

    public static class AssetDatabase
    {
        public static string[] FindAssets(string filter) { return new string[0]; }
        public static string[] FindAssets(string filter, string[] folders) { return new string[0]; }
        public static string GUIDToAssetPath(string guid) { return null; }
        public static T LoadAssetAtPath<T>(string path) where T : Object { return null; }
        public static string GetAssetPath(Object o) { return null; }
        public static bool IsValidFolder(string p) { return true; }
        public static string CreateFolder(string parent, string name) { return null; }
        public static string GenerateUniqueAssetPath(string p) { return p; }
        public static void CreateAsset(Object o, string path) { }
        public static void AddObjectToAsset(Object o, Object asset) { }
        public static bool DeleteAsset(string path) { return true; }
        public static void SaveAssets() { }
        public static void SaveAssetIfDirty(Object o) { }
        public static bool CopyAsset(string a, string b) { return true; }
        public static string RenameAsset(string path, string name) { return ""; }
    }

    public enum InteractionMode { AutomatedAction, UserAction }

    public static class PrefabUtility
    {
        public static bool IsPartOfPrefabInstance(Object o) { return false; }
        public static GameObject GetOutermostPrefabInstanceRoot(Object o) { return null; }
        public static string GetPrefabAssetPathOfNearestInstanceRoot(Object o) { return null; }
        public static GameObject SaveAsPrefabAssetAndConnect(GameObject g, string path, InteractionMode m, out bool ok) { ok = true; return g; }
        public static bool HasPrefabInstanceAnyOverrides(GameObject g, bool includeDefault) { return false; }
        public static void ApplyPrefabInstance(GameObject g, InteractionMode m) { }
        public static T GetCorrespondingObjectFromSource<T>(T o) where T : Object { return null; }
        public static Object InstantiatePrefab(Object o) { return null; }
    }

    public static class AssetPreview
    {
        public static Texture2D GetAssetPreview(Object o) { return null; }
        public static Texture2D GetMiniThumbnail(Object o) { return null; }
    }

    public enum PlayModeStateChange { EnteredEditMode, ExitingEditMode, EnteredPlayMode, ExitingPlayMode }

    public static class Undo
    {
        public static event Action undoRedoPerformed;
        public static void RecordObject(Object o, string name) { }
        public static void IncrementCurrentGroup() { }
        public static int GetCurrentGroup() { return 0; }
        public static void SetCurrentGroupName(string n) { }
        public static void CollapseUndoOperations(int g) { }
        public static void DestroyObjectImmediate(Object o) { }
        public static void RegisterCreatedObjectUndo(Object o, string n) { }
        public static T AddComponent<T>(GameObject go) where T : Component { return null; }
    }

    public static class EditorUtility
    {
        public static void SetDirty(Object o) { }
        public static void CopySerialized(Object src, Object dst) { }
        public static bool DisplayDialog(string t, string m, string ok) { return true; }
        public static bool DisplayDialog(string t, string m, string ok, string cancel) { return true; }
    }

    public static class EditorGUIUtility
    {
        public static bool isProSkin;
        public static void PingObject(Object o) { }
    }

    public static class EditorApplication
    {
        public static double timeSinceStartup;
        public static bool isPlayingOrWillChangePlaymode;
        public static Action hierarchyChanged;
        public static Action<PlayModeStateChange> playModeStateChanged;
        public static CallbackFunction delayCall;
        public delegate void CallbackFunction();
    }

    public static class Selection
    {
        public static GameObject activeGameObject;
        public static Transform activeTransform;
    }

    public class SceneView : EditorWindow
    {
        public static SceneView lastActiveSceneView;
        public bool FrameSelected() { return true; }
    }

    public class GenericMenu
    {
        public delegate void MenuFunction();
        public void AddItem(GUIContent c, bool on, MenuFunction f) { }
        public void AddDisabledItem(GUIContent c) { }
        public void ShowAsContext() { }
    }

    public struct GlobalObjectId
    {
        public static GlobalObjectId GetGlobalObjectIdSlow(Object o) { return default(GlobalObjectId); }
    }
}

namespace UnityEditor.SceneManagement
{
    public static class EditorSceneManager
    {
        public delegate void SceneClosingCallback(UnityEngine.SceneManagement.Scene scene, bool removingScene);
        public static event SceneClosingCallback sceneClosing;
    }
}

namespace UnityEditor.PackageManager
{
    public class PackageInfo
    {
        public string assetPath;
        public static PackageInfo FindForAssembly(System.Reflection.Assembly a) { return null; }
    }
}

namespace UnityEngine.TextCore.Text
{
    public struct FontWeightPair
    {
        public FontAsset regularTypeface;
        public FontAsset italicTypeface;
    }

    public class FontAsset : UnityEngine.ScriptableObject
    {
        public FontWeightPair[] fontWeightTable { get { return null; } }
        public System.Collections.Generic.List<FontAsset> fallbackFontAssetTable { get; set; }
        public static FontAsset CreateFontAsset(string familyName, string styleName, int pointSize) { return null; }
    }
}
