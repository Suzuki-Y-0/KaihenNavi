// Unity なしでコンパイル確認をするための最小限のスタブ。
// かいへんナビが使う API の「形」だけを真似ている。配布物には含めない。
#pragma warning disable 0067, 0414, 0649, 1591
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public enum KeyCode { Escape }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
    }

    public static class Resources
    {
        public static Func<Type, Object[]> FindForTests;
        public static T[] FindObjectsOfTypeAll<T>() where T : Object
        { return FindForTests == null ? new T[0] : Array.ConvertAll(FindForTests(typeof(T)), o => (T)o); }
    }

    public class Object
    {
        public string name;
        public HideFlags hideFlags;
        public int GetInstanceID() { return 0; }
        public static Func<Object, Object> InstantiateForTests;
        public static T Instantiate<T>(T original) where T : Object
        { return InstantiateForTests == null ? original : (T)InstantiateForTests(original); }
        public static void DestroyImmediate(Object o) { }
        public static implicit operator bool(Object o) { return !ReferenceEquals(o, null); }
    }

    [Flags] public enum HideFlags { None = 0, HideInHierarchy = 1, DontSave = 52, HideAndDontSave = 61 }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform;
        public T GetComponent<T>() { var all = GetComponents<T>(); return all.Length > 0 ? all[0] : default(T); }
        public T[] GetComponents<T>() { return gameObject == null ? new T[0] : gameObject.GetComponents<T>(); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) { return gameObject == null ? new T[0] : gameObject.GetComponentsInChildren<T>(includeInactive); }
        public T GetComponentInChildren<T>(bool includeInactive) { return default(T); }
    }

    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { }

    public class GameObject : Object
    {
        public GameObject() : this("") { }
        public GameObject(string name)
        {
            this.name = name;
            transform = new Transform { name = name, gameObject = this };
            transform.transform = transform;
        }
        private readonly List<Component> _components = new List<Component>();
        public Transform transform;
        public UnityEngine.SceneManagement.Scene scene;
        public bool activeSelf = true;
        public bool activeInHierarchy;
        public string tag;
        public int layer;
        public void SetActive(bool value) { activeSelf = value; }
        public T AddComponent<T>() where T : Component { return (T)AddComponent(typeof(T)); }
        public Component AddComponent(Type type)
        {
            var component = (Component)Activator.CreateInstance(type);
            component.gameObject = this;
            component.transform = transform;
            component.name = name;
            _components.Add(component);
            return component;
        }
        public T GetComponent<T>() { var all = GetComponents<T>(); return all.Length > 0 ? all[0] : default(T); }
        public Func<Type, object[]> ComponentsForTests;
        public T[] GetComponents<T>()
        {
            if (ComponentsForTests != null) return Array.ConvertAll(ComponentsForTests(typeof(T)), o => (T)o);
            var result = new List<T>();
            if (transform is T) result.Add((T)(object)transform);
            foreach (var component in _components) if (component is T) result.Add((T)(object)component);
            return result.ToArray();
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive)
        {
            var result = new List<T>();
            if (includeInactive || activeSelf) result.AddRange(GetComponents<T>());
            foreach (Transform child in transform)
                if (includeInactive || child.gameObject.activeSelf) result.AddRange(child.gameObject.GetComponentsInChildren<T>(includeInactive));
            return result.ToArray();
        }
        public T GetComponentInChildren<T>(bool includeInactive) { return default(T); }
    }

    public class Transform : Component, System.Collections.IEnumerable
    {
        public Matrix4x4 localToWorldMatrix;
        public Transform parent;
        public Transform root;
        public Vector3 lossyScale;
        public Vector3 localEulerAngles;
        public int childCount;
        public Vector3 position, localPosition, localScale, up, right, forward;
        public Quaternion rotation, localRotation;
        private readonly List<Transform> _children = new List<Transform>();
        public Transform GetChild(int index) { return _children[index]; }
        public Transform Find(string n) { return _children.Find(t => t.name == n); }
        public bool IsChildOf(Transform p) { for (var t = this; t != null; t = t.parent) if (t == p) return true; return false; }
        public void SetParent(Transform p, bool worldPositionStays)
        {
            if (parent != null) { parent._children.Remove(this); parent.childCount = parent._children.Count; }
            parent = p;
            if (p != null) { p._children.Add(this); p.childCount = p._children.Count; }
        }
        public int GetSiblingIndex() { return 0; }
        public void SetSiblingIndex(int i) { }
        public void SetAsLastSibling() { }
        public Vector3 TransformDirection(Vector3 v) { return v; }
        public Vector3 TransformPoint(Vector3 v) { return v; }
        public Vector3 InverseTransformPoint(Vector3 v) { return v; }
        public Vector3 InverseTransformVector(Vector3 v) { return v; }
        public System.Collections.IEnumerator GetEnumerator() { return _children.GetEnumerator(); }
    }

    public class Renderer : Component { public Material[] sharedMaterials; public Bounds bounds; public bool enabled, forceRenderingOff; }
    public class MeshRenderer : Renderer { }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class ParticleSystemRenderer : Renderer { public int meshCount; public int GetMeshes(Mesh[] meshes) { return 0; } }
    public class SkinnedMeshRenderer : Renderer
    {
        public Mesh sharedMesh;
        public Transform rootBone;
        public Transform[] bones;
        public float GetBlendShapeWeight(int i) { return 0; }
        public void SetBlendShapeWeight(int i, float v) { }
        public static Action<SkinnedMeshRenderer, Mesh, bool> BakeForTests;
        public void BakeMesh(Mesh m, bool useScale) { if (BakeForTests != null) BakeForTests(this, m, useScale); }
    }

    public class Mesh : Object
    {
        public Vector2[] uv, uv2;
        public int subMeshCount;
        public readonly Dictionary<int, int[]> submeshes = new Dictionary<int, int[]>();
        public int[] GetTriangles(int submesh) { int[] value; return submeshes.TryGetValue(submesh, out value) ? value : new int[0]; }
        public bool isReadable;
        public int blendShapeCount;
        public Matrix4x4[] bindposes;
        public Vector3[] vertices = new Vector3[0], normals;
        public int[] triangles;
        public int vertexCount { get { return vertices == null ? 0 : vertices.Length; } }
        public UnityEngine.Rendering.IndexFormat indexFormat;
        public void SetTriangles(int[] value, int submesh) { submeshes[submesh] = value; }
        public void RecalculateNormals() { }
        public void RecalculateBounds() { }
        public string GetBlendShapeName(int i) { return ""; }
        public int GetBlendShapeIndex(string name) { return -1; }
    }
    public class Collider : Component { }
    public class MeshCollider : Collider { public Mesh sharedMesh; }
    public struct RaycastHit { public Collider collider; }
    public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }
    public static class Physics
    {
        public static bool queriesHitBackfaces;
        public static void SyncTransforms() { }
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float distance, int layerMask, QueryTriggerInteraction triggers)
        { hit = default(RaycastHit); return false; }
        public static int RaycastNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] hits, float distance, int layerMask, QueryTriggerInteraction triggers) { return 0; }
    }
    public struct Matrix4x4
    {
        public static Matrix4x4 identity;
        public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s) { return default(Matrix4x4); }
        public static Matrix4x4 Scale(Vector3 s) { return default(Matrix4x4); }
        public static Matrix4x4 Ortho(float l, float r, float b, float t, float n, float f) { return default(Matrix4x4); }
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) { return a; }
        public Matrix4x4 inverse { get { return this; } }
        public Matrix4x4 transpose { get { return this; } }
        public Vector3 MultiplyVector(Vector3 v) { return v; }
    }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public struct Hash128 { }
    public class Texture : Object { public TextureWrapMode wrapMode; public int width; public int height; public FilterMode filterMode; public Hash128 imageContentsHash; }
    public enum TextureFormat { RGBA32, RGB24, RGBAFloat }
    public enum TextureWrapMode { Repeat, Clamp }
    public class Texture2D : Texture
    {
        public static Texture2D whiteTexture;
        public Texture2D(int w, int h, TextureFormat f, bool mip) { }
        public Texture2D(int w, int h, TextureFormat f, bool mip, bool linear) { }
        public Color[] GetPixels() { return new Color[0]; }
        public void SetPixels(Color[] p) { }
        public void SetPixel(int x, int y, Color c) { }
        public Color32[] GetPixels32() { return new Color32[0]; }
        public void SetPixels32(Color32[] p) { }
        public void ReadPixels(Rect r, int x, int y) { }
        public void ReadPixels(Rect r, int x, int y, bool recalculateMipMaps) { }
        public byte[] EncodeToPNG() { return new byte[0]; }
        public void Apply() { }
    }

    public static class ImageConversion
    {
        public static bool LoadImage(this Texture2D texture, byte[] data, bool markNonReadable) { return true; }
    }

    public enum RenderTextureFormat { ARGB32, RGFloat, ARGBFloat, Depth }
    public enum RenderTextureReadWrite { Default, Linear, sRGB }
    public class RenderTexture : Texture
    {
        public RenderTexture(int w, int h, int depth, RenderTextureFormat f) { }
        public RenderTexture(int w, int h, int depth, RenderTextureFormat f, RenderTextureReadWrite rw) { }
        public bool Create() { return true; }
        public int antiAliasing;
        public static RenderTexture active;
        public void Release() { }
        public static RenderTexture GetTemporary(int w, int h, int depth, RenderTextureFormat f, RenderTextureReadWrite rw) { return null; }
        public static void ReleaseTemporary(RenderTexture rt) { }
    }

    public enum CameraClearFlags { Skybox, SolidColor }
    public class Camera : Behaviour
    {
        public float fieldOfView, aspect, nearClipPlane, farClipPlane, orthographicSize;
        public bool orthographic;
        public CameraClearFlags clearFlags;
        public Color backgroundColor;
        public RenderTexture targetTexture;
        public void Render() { }
        public Vector3 WorldToViewportPoint(Vector3 p) { return p; }
    }

    public class Shader : Object { public bool isSupported; public static Shader Find(string name) { return null; } }
    public static class Graphics { public static void Blit(Texture source, RenderTexture dest) { } }
    public class RuntimeAnimatorController : Object { }

    public class Material : Object
    {
        public Texture mainTexture;
        public int renderQueue;
        public Material(Shader s) { shader = s; }
        public void SetMatrix(string name, Matrix4x4 value) { }
        public Color color;
        public Shader shader;
#if INNER_COLOR_ASSET_TESTS
        internal Dictionary<string, object> values = new Dictionary<string, object>();
        internal HashSet<string> keywords = new HashSet<string>();
        public Material(Material m)
        {
            if (m == null) return;
            name = m.name; shader = m.shader;
            values = new Dictionary<string, object>(m.values);
            keywords = new HashSet<string>(m.keywords);
        }
        private T Read<T>(string key) { object value; return values.TryGetValue(key, out value) ? (T)value : default(T); }
        public bool HasProperty(string n) { return values.ContainsKey(n); }
        public Vector4 GetVector(string n) { return Read<Vector4>(n); }
        public void SetVector(string n, Vector4 v) { values[n] = v; }
        public Color GetColor(string n) { return Read<Color>(n); }
        public void SetColor(string n, Color c) { values[n] = c; }
        public float GetFloat(string n) { return Read<float>(n); }
        public void SetFloat(string n, float v) { values[n] = v; }
        public Texture GetTexture(string n) { return Read<Texture>(n); }
        public void SetTexture(string n, Texture t) { values[n] = t; }
        public bool IsKeywordEnabled(string keyword) { return keywords.Contains(keyword); }
        public string[] GetTexturePropertyNames() { var names = new List<string>(); foreach (var p in values) if (p.Value is Texture) names.Add(p.Key); return names.ToArray(); }
        public void EnableKeyword(string keyword) { keywords.Add(keyword); }
        public void DisableKeyword(string keyword) { keywords.Remove(keyword); }
        public Vector2 GetTextureScale(string n) { return Read<Vector2>(n + "#scale"); }
        public Vector2 GetTextureOffset(string n) { return Read<Vector2>(n + "#offset"); }
        public void SetTextureScale(string n, Vector2 v) { values[n + "#scale"] = v; }
        public void SetTextureOffset(string n, Vector2 v) { values[n + "#offset"] = v; }
#else
        public Material(Material m) { }
        public bool HasProperty(string n) { return false; }
        public Vector4 GetVector(string n) { return default(Vector4); }
        public void SetVector(string n, Vector4 v) { }
        public Color GetColor(string n) { return default(Color); }
        public void SetColor(string n, Color c) { }
        public float GetFloat(string n) { return 0f; }
        public void SetFloat(string n, float v) { }
        public Texture GetTexture(string n) { return null; }
        public void SetTexture(string n, Texture t) { }
        public bool IsKeywordEnabled(string keyword) { return false; }
        public string[] GetTexturePropertyNames() { return new string[0]; }
        public void EnableKeyword(string keyword) { }
        public void DisableKeyword(string keyword) { }
        public Vector2 GetTextureScale(string n) { return default(Vector2); }
        public Vector2 GetTextureOffset(string n) { return default(Vector2); }
        public void SetTextureScale(string n, Vector2 v) { }
        public void SetTextureOffset(string n, Vector2 v) { }
#endif
    }

    public class ScriptableObject : Object
    {
        public static Func<Type, ScriptableObject> CreateForTests;
        public static T CreateInstance<T>() where T : ScriptableObject
        { return CreateForTests == null ? null : (T)CreateForTests(typeof(T)); }
    }

    public class TextAsset : Object { public string text; }
    public class Font : Object { }

    public class Animator : Behaviour
    {
        public bool isHuman;
        public Func<HumanBodyBones, Transform> BoneForTests;
        public Transform GetBoneTransform(HumanBodyBones b) { return BoneForTests == null ? null : BoneForTests(b); }
    }

    public enum HumanBodyBones
    {
        Hips = 0, LeftUpperLeg = 1, RightUpperLeg = 2, LeftLowerLeg = 3, RightLowerLeg = 4, LeftFoot = 5, RightFoot = 6,
        Spine = 7, Chest = 8, Neck = 9, Head = 10, LeftShoulder = 11, RightShoulder = 12, LeftUpperArm = 13, RightUpperArm = 14,
        LeftLowerArm = 15, RightLowerArm = 16, LeftHand = 17, RightHand = 18, LeftToes = 19, RightToes = 20, LeftEye = 21, RightEye = 22,
        Jaw = 23, LeftThumbProximal = 24, RightLittleDistal = 53, UpperChest = 54, LastBone = 55,
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { this.center = center; max = center + size * 0.5f; }
        public Vector3 center, max;
        public void Encapsulate(Bounds b) { }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator *(Vector2 a, float d) { return a; }
        public static float Distance(Vector2 a, Vector2 b) { return 0f; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float this[int i] { get { return i == 0 ? x : i == 1 ? y : z; } set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; } }
        public float sqrMagnitude { get { return Dot(this, this); } }
        public Vector3 normalized { get { var m = (float)Math.Sqrt(sqrMagnitude); return m > 1e-5f ? this * (1f / m) : zero; } }
        public static Vector3 zero = new Vector3(0, 0, 0), one = new Vector3(1, 1, 1), up = new Vector3(0, 1, 0),
            down = new Vector3(0, -1, 0), forward = new Vector3(0, 0, 1), back = new Vector3(0, 0, -1);
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a) { return a * -1; }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return a + -b; }
        public static Vector3 operator *(Vector3 a, float d) { return new Vector3(a.x * d, a.y * d, a.z * d); }
        public static Vector3 operator *(float d, Vector3 a) { return a * d; }
        public static float Distance(Vector3 a, Vector3 b) { return (float)Math.Sqrt((a - b).sqrMagnitude); }
        public static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static Vector3 Cross(Vector3 a, Vector3 b) { return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        public static implicit operator Vector2(Vector3 v) { return new Vector2(v.x, v.y); }
        public static bool operator ==(Vector3 a, Vector3 b) { return (a - b).sqrMagnitude < 1e-10f; }
        public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
        public override bool Equals(object o) { return o is Vector3 && this == (Vector3)o; }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }
    }
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Vector3 eulerAngles { get { return new Vector3(); } }
        public static Quaternion identity;
        public static Quaternion Inverse(Quaternion q) { return q; }
        public static Quaternion AngleAxis(float a, Vector3 axis) { return default(Quaternion); }
        public static Quaternion LookRotation(Vector3 forward) { return default(Quaternion); }
        public static Quaternion LookRotation(Vector3 forward, Vector3 up) { return default(Quaternion); }
        public static Quaternion operator *(Quaternion a, Quaternion b) { return a; }
        public static Vector3 operator *(Quaternion a, Vector3 v) { return v; }
        public static bool operator ==(Quaternion a, Quaternion b) { return true; }
        public static bool operator !=(Quaternion a, Quaternion b) { return false; }
        public override bool Equals(object o) { return false; }
        public override int GetHashCode() { return 0; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color gray, white, clear;
        public static void RGBToHSV(Color c, out float h, out float s, out float v) { h = s = v = 0; }
        public static Color HSVToRGB(float h, float s, float v) { return default(Color); }
        public static Color HSVToRGB(float h, float s, float v, bool hdr) { return default(Color); }
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public static class JsonUtility
    {
        public static Func<object, string> SerializeForTests;
        public static Func<string, Type, object> DeserializeForTests;
        public static string ToJson(object o) { return SerializeForTests == null ? "" : SerializeForTests(o); }
        public static string ToJson(object o, bool pretty) { return ""; }
        public static T FromJson<T>(string json)
        { return DeserializeForTests == null ? default(T) : (T)DeserializeForTests(json, typeof(T)); }
    }

    public static class ColorUtility { public static bool TryParseHtmlString(string s, out Color c) { c = default(Color); return true; } }

    public static class Mathf
    {
        public static float DeltaAngle(float current, float target) { return target - current; }
        public const float PI = 3.14159265f;
        public const float Deg2Rad = PI / 180f;
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static int Min(int a, int b) { return Math.Min(a, b); }
        public static float Clamp(float v, float a, float b) { return Math.Min(b, Math.Max(a, v)); }
        public static float Clamp01(float v) { return Clamp(v, 0, 1); }
        public static float Abs(float v) { return Math.Abs(v); }
        public static float Tan(float v) { return v; }
        public static float Sin(float v) { return v; }
        public static float Cos(float v) { return v; }
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

    public enum EventType { Repaint, Layout }

    public class GUIContent
    {
        public GUIContent(string text) { }
        public Texture image;
    }

    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public class AddComponentMenu : Attribute { public AddComponentMenu(string s) { } }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public bool isLoaded;
        public bool validForTests;
        public bool IsValid() { return validForTests; }
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
        public T value;
        public static implicit operator StyleEnum<T>(T v) { return new StyleEnum<T> { value = v }; }
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
        float flexGrow { get; set; }
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

    public enum TrickleDown { NoTrickleDown, TrickleDown }
    public abstract class EventBase { public object target; }
    public class MouseDownEvent : EventBase { public int clickCount; }
    public class FocusOutEvent : EventBase { }
    public class PointerCaptureOutEvent : EventBase { }
    public class DragUpdatedEvent : EventBase { }
    public abstract class PointerEventBase : EventBase
    {
        public UnityEngine.Vector3 localPosition;
        public int pointerId;
    }
    public class KeyDownEvent : EventBase { public UnityEngine.KeyCode keyCode; public void StopPropagation() { } }
    public class PointerDownEvent : PointerEventBase { }
    public class PointerMoveEvent : PointerEventBase { }
    public class PointerUpEvent : PointerEventBase { }

    public enum LineCap { Butt, Round, Square }
    public enum LineJoin { Miter, Round, Bevel }

    public class Painter2D
    {
        public float lineWidth;
        public LineCap lineCap;
        public LineJoin lineJoin;
        public UnityEngine.Color strokeColor, fillColor;
        public void BeginPath() { }
        public void MoveTo(UnityEngine.Vector2 p) { }
        public void LineTo(UnityEngine.Vector2 p) { }
        public void BezierCurveTo(UnityEngine.Vector2 c1, UnityEngine.Vector2 c2, UnityEngine.Vector2 end) { }
        public void ClosePath() { }
        public void Stroke() { }
        public void Fill() { }
    }

    public class MeshGenerationContext
    {
        public Painter2D painter2D;
    }
    public class DragLeaveEvent : EventBase { }
    public class DragPerformEvent : EventBase { }
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
        UnityEngine.Color color { get; }
    }

    public struct UQueryBuilder<T> where T : VisualElement
    {
        public void ForEach(Action<T> funcCall) { }
    }

    public static class UQueryExtensions
    {
        public static UQueryBuilder<T> Query<T>(this VisualElement e) where T : VisualElement { return default(UQueryBuilder<T>); }
    }

    public interface IPanel { }

    public class VisualElement
    {
        public static Func<IStyle> StyleForTests;
        public VisualElement() { if (StyleForTests != null) style = StyleForTests(); }
        public UnityEngine.Rect layout;
        public IStyle style;
        public IPanel panel;
        public IResolvedStyle resolvedStyle;
        public IVisualElementScheduler schedule;
        public bool ClassListContains(string c) { return false; }
        public UnityEngine.Rect contentRect;
        public Action<MeshGenerationContext> generateVisualContent;
        public int childCount;
        public bool focusable;
        public void Focus() { }
        public void RemoveFromHierarchy() { }
        public Action RepaintForTests;
        public void MarkDirtyRepaint() { if (RepaintForTests != null) RepaintForTests(); }
        public void CapturePointer(int id) { }
        public void ReleasePointer(int id) { }
        public string tooltip;
        public VisualElementStyleSheetSet styleSheets;
        public void Add(VisualElement child) { }
        public void Clear() { }
        public void AddToClassList(string c) { }
        public void RemoveFromClassList(string c) { }
        public void EnableInClassList(string c, bool enable) { }
        public void SetEnabled(bool value) { }
        public void RegisterCallback<T>(EventCallback<T> callback, TrickleDown trickleDown = TrickleDown.NoTrickleDown) where T : EventBase { }
        public void UnregisterCallback<T>(EventCallback<T> callback) where T : EventBase { }
    }

    public class ChangeEvent<T> : EventBase
    {
        public T previousValue;
        public T newValue;
    }

    public abstract class BaseField<T> : VisualElement
    {
        private T _value;
        public EventCallback<ChangeEvent<T>> ChangedForTests;
        public T value
        {
            get { return _value; }
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;
                var previous = _value;
                _value = value;
                if (ChangedForTests != null) ChangedForTests(new ChangeEvent<T> { previousValue = previous, newValue = value });
            }
        }
        public void SetValueWithoutNotify(T v) { _value = v; }
        public void SendValueForTests(T next)
        {
            var old = _value;
            _value = next;
            if (ChangedForTests != null) ChangedForTests(new ChangeEvent<T> { previousValue = old, newValue = next });
        }
    }

    public static class INotifyValueChangedExtensions
    {
        public static bool RegisterValueChangedCallback<T>(this BaseField<T> control, EventCallback<ChangeEvent<T>> callback) { control.ChangedForTests += callback; return true; }
    }

    public class TextElement : VisualElement { public string text; }
    public class Label : TextElement { public Label() { } public Label(string text) { this.text = text; } }
    public class Button : TextElement { public Action clicked; public Button() { } public Button(Action clickEvent) { clicked = clickEvent; } }
    public class Foldout : BaseField<bool> { public string text; }
    public class Toggle : BaseField<bool> { public Toggle() { } public Toggle(string label) { } }
    public class TextField : BaseField<string> { public string label; }
    public class Slider : BaseField<float>
    {
        public Slider(string label, float start, float end) { this.label = label; _low = start; _high = end; }
        public string label;
        private float _low, _high;
        public float lowValue { get { return _low; } set { _low = value; if (this.value < value) this.value = value; } }
        public float highValue { get { return _high; } set { _high = value; if (this.value > value) this.value = value; } }
        public bool showInputField;
    }

    public class DropdownField : BaseField<string>
    {
        public DropdownField(List<string> choices, int defaultIndex) { }
        public DropdownField(string label, List<string> choices, int defaultIndex) { }
    }

    public enum ScrollViewMode { Vertical, Horizontal }
    public class ScrollView : VisualElement { public ScrollView(ScrollViewMode mode) { } public VisualElement contentViewport; }
    public class GeometryChangedEvent : EventBase { public UnityEngine.Rect newRect; }
}

namespace UnityEditor.UIElements
{
    public class ColorField : UnityEngine.UIElements.BaseField<UnityEngine.Color>
    {
        public ColorField(string label) { }
        public bool showAlpha, hdr;
    }

    public class ObjectField : UnityEngine.UIElements.BaseField<UnityEngine.Object>
    {
        public ObjectField(string label) { }
        public Type objectType;
        public bool allowSceneObjects;
    }

    public class PopupField<T> : UnityEngine.UIElements.BaseField<T>
    {
        public string label;
        public PopupField(List<T> choices, int defaultIndex) { }
    }
}

namespace UnityEditor
{
    using UnityEngine;

    public class EditorWindow : ScriptableObject
    {
        public GUIContent titleContent;
        public Vector2 minSize, maxSize;
        public UnityEngine.UIElements.VisualElement rootVisualElement;
        public static T GetWindow<T>() where T : EditorWindow { return null; }
        public void Show() { }
        public void ShowUtility() { }
        public void Close() { }
    }

    public class AssetImporter : Object
    {
        public string assetPath;
        public string userData;
        public static Func<string, AssetImporter> LoadForTests;
        public static AssetImporter GetAtPath(string path) { return LoadForTests == null ? null : LoadForTests(path); }
        public void SaveAndReimport() { }
    }

    public enum TextureImporterType { Default }
    public enum TextureImporterCompression { Uncompressed }
    public class TextureImporter : AssetImporter
    {
        public TextureImporterType textureType;
        public bool isReadable, sRGBTexture, mipmapEnabled;
        public TextureWrapMode wrapMode;
        public FilterMode filterMode;
        public int maxTextureSize;
        public TextureImporterCompression textureCompression;
    }

    public enum ModelImporterNormals { Import, Calculate, None }

    public class ModelImporter : AssetImporter
    {
        public bool isReadable;
        public ModelImporterNormals importBlendShapeNormals;
    }

    public class SerializedProperty { public bool boolValue; }

    public class SerializedObject
    {
        public SerializedObject(Object o) { }
        public SerializedProperty FindProperty(string name) { return null; }
        public bool ApplyModifiedPropertiesWithoutUndo() { return true; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class MenuItem : Attribute { public MenuItem(string path, bool validate, int priority) { } }

    public static class EditorPrefs
    {
        public static bool GetBool(string key, bool fallback) { return fallback; }
        public static string GetString(string k, string d) { return d; }
        public static void SetString(string k, string v) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class InitializeOnLoadAttribute : Attribute { }

    public class AssetPostprocessor { }

    public static class SessionState
    {
        public static string GetString(string key, string fallback) { return fallback; }
        public static void SetString(string key, string value) { }
        public static void EraseString(string key) { }
    }

    public static class AssetDatabase
    {
        public static void ImportPackage(string path, bool interactive) { }
        public static void Refresh() { }
        public static Func<Object, string> PathForTests;
        public static Func<string, Object> LoadForTests;
        public static Func<string, Object[]> LoadAllForTests;
        public static Action<Object, string> CreateForTests;
        public static Action<string> ImportForTests, DeleteForTests;
        public static Func<string[]> PathsForTests;
        public static Func<string[], string[]> DependenciesForTests;
        public static Func<string, string> GuidForTests;
        public static Func<string, string> PathFromGuidForTests;
        public static Func<Object, long> LocalIdForTests;
        public static Func<string, bool> FolderForTests;
        public static Action<string, string> MoveForTests;
        public static Action<string> importPackageStarted;
        public static Action<string> importPackageCompleted;
        public static Action<string> importPackageCancelled;
        public static Action<string, string> importPackageFailed;
        public static string MoveAsset(string from, string to) { if (MoveForTests != null) MoveForTests(from, to); return ""; }
        public static bool TryGetGUIDAndLocalFileIdentifier(Object o, out string guid, out long localId)
        { guid = GuidForTests == null ? "" : GuidForTests(GetAssetPath(o));
            localId = LocalIdForTests == null ? 0 : LocalIdForTests(o); return o != null; }
        public static Object[] LoadAllAssetsAtPath(string path) { return LoadAllForTests == null ? new Object[0] : LoadAllForTests(path); }
        public static string[] GetAllAssetPaths() { return PathsForTests == null ? new string[0] : PathsForTests(); }
        public static string[] GetDependencies(string[] paths, bool recursive) { return DependenciesForTests == null ? new string[0] : DependenciesForTests(paths); }
        public static bool Contains(Object o) { return false; }
        public static string[] FindAssets(string filter) { return new string[0]; }
        public static Func<string, string[], string[]> FindForTests;
        public static string[] FindAssets(string filter, string[] folders)
        { if (FindForTests != null) return FindForTests(filter, folders);
            return PathsForTests == null ? new string[0] : Array.ConvertAll(Array.FindAll(PathsForTests(), p => Array.Exists(folders, f => p.StartsWith(f + "/"))), AssetPathToGUID); }
        public static string GUIDToAssetPath(string guid) { return PathFromGuidForTests == null ? null : PathFromGuidForTests(guid); }
        public static T LoadAssetAtPath<T>(string path) where T : Object { return LoadForTests == null ? null : LoadForTests(path) as T; }
        public static string GetAssetPath(Object o) { return PathForTests == null ? null : PathForTests(o); }
        public static bool IsValidFolder(string p) { return FolderForTests == null ? true : FolderForTests(p); }
        public static string CreateFolder(string parent, string name) { return null; }
        public static string GenerateUniqueAssetPath(string p) { return p; }
        public static void CreateAsset(Object o, string path) { if (CreateForTests != null) CreateForTests(o, path); }
        public static void AddObjectToAsset(Object o, Object asset) { }
        public static bool DeleteAsset(string path) { if (DeleteForTests != null) DeleteForTests(path); return true; }
        public static void SaveAssets() { }
        public static void SaveAssetIfDirty(Object o) { }
        public static bool CopyAsset(string a, string b) { return true; }
        public static void ImportAsset(string path) { if (ImportForTests != null) ImportForTests(path); }
        public static void ImportAsset(string path, ImportAssetOptions options) { ImportAsset(path); }
        public static string AssetPathToGUID(string path) { return GuidForTests == null ? "" : GuidForTests(path); }
        public static string RenameAsset(string path, string name) { return ""; }
    }

    public enum InteractionMode { AutomatedAction, UserAction }

    public enum DragAndDropVisualMode { None, Copy, Link, Move, Generic, Rejected }

    public static class DragAndDrop
    {
        public static Object[] objectReferences;
        public static DragAndDropVisualMode visualMode;
        public static void AcceptDrag() { }
    }

    public static class PrefabUtility
    {
        public static void RecordPrefabInstancePropertyModifications(Object o) { }
        public static GameObject RootForTests;
        public static string PathForTests;
        public static bool OverridesForTests;
        public static Action<GameObject, InteractionMode> ApplyForTests;
        public static bool IsPartOfPrefabInstance(Object o) { return RootForTests != null; }
        public static bool IsOutermostPrefabInstanceRoot(GameObject g) { return false; }
        public static GameObject GetOutermostPrefabInstanceRoot(Object o) { return RootForTests; }
        public static string GetPrefabAssetPathOfNearestInstanceRoot(Object o) { return PathForTests; }
        public static GameObject SaveAsPrefabAssetAndConnect(GameObject g, string path, InteractionMode m, out bool ok) { ok = true; return g; }
        public static bool HasPrefabInstanceAnyOverrides(GameObject g, bool includeDefault) { return OverridesForTests; }
        public static void ApplyPrefabInstance(GameObject g, InteractionMode m) { if (ApplyForTests != null) ApplyForTests(g, m); }
        public static T GetCorrespondingObjectFromSource<T>(T o) where T : Object { return null; }
        public static Object InstantiatePrefab(Object o) { return null; }
        public static Object InstantiatePrefab(Object o, Transform parent) { return null; }
        public static void RevertObjectOverride(Object o, InteractionMode m) { }
        public static GameObject LoadPrefabContents(string path) { return null; }
        public static void UnloadPrefabContents(GameObject g) { }
        public static GameObject SaveAsPrefabAsset(GameObject g, string path) { return g; }
        public static T GetCorrespondingObjectFromOriginalSource<T>(T o) where T : Object { return null; }
        public static void ReplacePrefabAssetOfPrefabInstance(GameObject root, GameObject asset, PrefabReplacingSettings s, InteractionMode m) { }
        public static void UnpackPrefabInstance(GameObject root, PrefabUnpackMode mode, InteractionMode m) { }
    }

    public enum PrefabUnpackMode { OutermostRoot, Completely }
    public enum ObjectMatchMode { NoMatchingPerformed, ByName, ByHierarchy }

    public class PrefabReplacingSettings
    {
        public bool logInfo;
        public bool changeRootNameToAssetName;
        public ObjectMatchMode objectMatchMode;
    }

    public static class AssetPreview
    {
        public static Texture2D GetAssetPreview(Object o) { return null; }
        public static Texture2D GetMiniThumbnail(Object o) { return null; }
    }

    public enum PlayModeStateChange { EnteredEditMode, ExitingEditMode, EnteredPlayMode, ExitingPlayMode }

    public class PropertyModification { public Object target; }
    public struct UndoPropertyModification { public PropertyModification currentValue; }

    public static class Undo
    {
        public static int GroupForTests;
        public static string GroupNameForTests;
        public static Action PerformUndoForTests;
        public static void PerformUndo() { if (PerformUndoForTests != null) PerformUndoForTests(); }
        public static string GetCurrentGroupName() { return GroupNameForTests; }
        public static void FlushUndoRecordObjects() { }
        public static void RevertAllDownToGroup(int group) { }
        public static void SetTransformParent(Transform t, Transform parent, string name) { t.SetParent(parent, true); }
        public static event Action undoRedoPerformed;
        public delegate UndoPropertyModification[] PostprocessModifications(UndoPropertyModification[] modifications);
        public static event PostprocessModifications postprocessModifications;
        public static Action<Object> RecordForTests;
        public static void RecordObject(Object o, string name) { if (RecordForTests != null) RecordForTests(o); }
        public static void ReplayForTests() { if (undoRedoPerformed != null) undoRedoPerformed(); }
        public static void RegisterFullObjectHierarchyUndo(Object o, string name) { }
        public static Action<int> CollapseForTests;
        public static Action<Object> DestroyForTests;
        public static void IncrementCurrentGroup() { GroupForTests++; }
        public static int GetCurrentGroup() { return GroupForTests; }
        public static void SetCurrentGroupName(string n) { GroupNameForTests = n; }
        public static void CollapseUndoOperations(int g) { if (CollapseForTests != null) CollapseForTests(g); }
        public static void DestroyObjectImmediate(Object o)
        {
            if (DestroyForTests != null) DestroyForTests(o);
            var go = o as GameObject;
            if (go != null) go.transform.SetParent(null, true);
        }
        public static void RegisterCreatedObjectUndo(Object o, string n) { }
        public static T AddComponent<T>(GameObject go) where T : Component { return go.AddComponent<T>(); }
        public static Component AddComponent(GameObject go, Type type) { return go.AddComponent(type); }
    }

    public static class EditorUtility
    {
        public static string OpenFolderPanel(string title, string folder, string defaultName) { return ""; }
        public static Func<Object, bool> PersistentForTests;
        public static bool IsPersistent(Object o) { return PersistentForTests != null && PersistentForTests(o); }
        public static Action<float> ProgressForTests;
        public static Action ClearProgressForTests;
        public static void DisplayProgressBar(string title, string info, float progress) { if (ProgressForTests != null) ProgressForTests(progress); }
        public static void ClearProgressBar() { if (ClearProgressForTests != null) ClearProgressForTests(); }
        public static void SetDirty(Object o) { }
        public static bool IsDirty(Object target) { return false; }
        public static Action<Object, Object> CopyForTests;
        public static void CopySerialized(Object src, Object dst) { if (CopyForTests != null) CopyForTests(src, dst); }
        public static bool DisplayDialog(string t, string m, string ok) { return true; }
        public static bool DisplayDialog(string t, string m, string ok, string cancel) { return true; }
    }

    public static class EditorGUIUtility
    {
        public static bool isProSkin;
        public static GUIContent IconContent(string name) { return new GUIContent(name); }
        public static void PingObject(Object o) { }
    }

    public static class AssemblyReloadEvents
    {
        public static Action beforeAssemblyReload;
    }

    public enum ImportAssetOptions { Default, ForceUpdate, ForceSynchronousImport }
    public static class EditorApplication
    {
        public static CallbackFunction update;
        public static double timeSinceStartup;
        public static bool isPlayingOrWillChangePlaymode;
        public static Action hierarchyChanged;
        public static bool ExecuteMenuItem(string path) { return false; }
        public static Action<PlayModeStateChange> playModeStateChanged;
        public static CallbackFunction delayCall;
        public delegate void CallbackFunction();
    }

    public static class Selection
    {
        public static Object activeObject;
        public static GameObject activeGameObject;
        public static Transform activeTransform;
    }

    public enum Tool { Move }
    public static class Tools { public static Tool current; }

    public class SceneView : EditorWindow
    {
        public static SceneView lastActiveSceneView;
        public Camera camera;
        public static Action<SceneView> duringSceneGui;
        public static void RepaintAll() { }
        public bool FrameSelected() { return true; }
        public void Frame(Bounds bounds, bool instant = true) { }
        public void LookAt(Vector3 point, Quaternion direction, float size) { }
    }

    public static class Handles
    {
        public static Color color;
        public static void SphereHandleCap(int id, Vector3 position, Quaternion rotation, float size, EventType type) { }
        public static void DrawLine(Vector3 a, Vector3 b) { }
        public static void DrawLine(Vector3 a, Vector3 b, float thickness) { }
        public static void DrawWireDisc(Vector3 center, Vector3 normal, float radius, float thickness) { }
        public static void ConeHandleCap(int id, Vector3 position, Quaternion rotation, float size, EventType type) { }
        public static UnityEngine.Rendering.CompareFunction zTest;
    }

    public static class HandleUtility
    {
        public static float GetHandleSize(Vector3 p) { return 1f; }
    }

    public static class ShaderUtil { public static bool ShaderHasError(Shader s) { return false; } }

    public class GenericMenu
    {
        public delegate void MenuFunction();
        public void AddItem(GUIContent c, bool on, MenuFunction f) { }
        public void AddDisabledItem(GUIContent c) { }
        public void AddSeparator(string path) { }
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
        public string userData;
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
        public static FontAsset CreateFontAsset(UnityEngine.Font font) { return null; }
    }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16, UInt32 }
    public enum CompareFunction { Disabled, Never, Less, Equal, LessEqual, Greater, NotEqual, GreaterEqual, Always }
}
