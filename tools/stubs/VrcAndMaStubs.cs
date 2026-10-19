// VRChat SDK と Modular Avatar のうち、かいへんナビが使う部分だけのスタブ（コンパイル確認用）。
#pragma warning disable 0649
using System.Collections.Generic;
using UnityEngine;

namespace VRC.SDK3.Avatars.ScriptableObjects
{
    public class VRCExpressionParameters : ScriptableObject
    {
        public const int MAX_PARAMETER_COST = 256;
        public int CalcTotalCost() { return 0; }
        public enum ValueType { Int, Float, Bool }
        public class Parameter
        {
            public string name;
            public ValueType valueType;
            public float defaultValue;
        }
        public Parameter[] parameters;
    }

    public class VRCExpressionsMenu : ScriptableObject
    {
        public class Control
        {
            public enum ControlType { Button = 101, Toggle = 102, SubMenu = 103, TwoAxisPuppet = 201, FourAxisPuppet = 202, RadialPuppet = 203 }
            public enum Style { Style1, Style2, Style3, Style4 }

            public class Parameter
            {
                public string name;
            }

            public struct Label
            {
                public string name;
                public Texture2D icon;
            }

            public string name;
            public Texture2D icon;
            public ControlType type;
            public Parameter parameter;
            public float value;
            public Style style;
            public VRCExpressionsMenu subMenu;
            public Parameter[] subParameters;
            public Label[] labels;
        }

        public List<Control> controls = new List<Control>();
    }
}

namespace VRC.SDK3.Avatars.Components
{
    public class VRCAvatarDescriptor : MonoBehaviour
    {
        public Vector3 ViewPosition;
        public SkinnedMeshRenderer VisemeSkinnedMesh;
        public VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters expressionParameters;
        public VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu expressionsMenu;
        public enum AnimLayerType { Base, Deprecated0, Additive, Gesture, Action, FX, Sitting, TPose, IKPose }
    }
}

namespace nadena.dev.modular_avatar.core
{
    public enum SubmenuSource { MenuAsset, Children }

    public class AvatarTagComponent : MonoBehaviour { }

    public class AvatarObjectReference
    {
        public string referencePath;
        public void Set(GameObject target) { }
        public GameObject Get(Component container) { return null; }
    }

    public class ModularAvatarMenuInstaller : AvatarTagComponent { }
    public class ModularAvatarMenuGroup : AvatarTagComponent { public GameObject targetObject; }

    public class ModularAvatarMergeArmature : AvatarTagComponent { }

    public enum MergeAnimatorPathMode { Relative, Absolute }
    public enum MergeAnimatorMode { Append, Replace }

    public class ModularAvatarMergeAnimator : AvatarTagComponent
    {
        public RuntimeAnimatorController animator;
        public VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType layerType;
        public bool deleteAttachedAnimator;
        public MergeAnimatorPathMode pathMode;
        public bool matchAvatarWriteDefaults;
        public int layerPriority;
        public MergeAnimatorMode mergeAnimatorMode;
    }

    public enum ShapeChangeType { Delete, Set }

    public struct ChangedShape
    {
        public AvatarObjectReference Object;
        public string ShapeName;
        public ShapeChangeType ChangeType;
        public float Value;
    }

    public class ModularAvatarShapeChanger : AvatarTagComponent
    {
        public List<ChangedShape> Shapes { get; set; }
    }

    public class ModularAvatarScaleAdjuster : AvatarTagComponent
    {
        public Vector3 Scale { get; set; }
    }

    public class ModularAvatarMenuItem : AvatarTagComponent
    {
        public VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control Control;
        public SubmenuSource MenuSource;
        public string label = "";
        public bool isSynced = true;
        public bool isSaved = true;
        public bool isDefault;
    }

    public struct ToggledObject
    {
        public AvatarObjectReference Object;
        public bool Active;
    }

    public class ModularAvatarObjectToggle : AvatarTagComponent
    {
        public List<ToggledObject> Objects { get; set; }
    }
}

namespace VRC.Core
{
    public class PipelineManager : UnityEngine.MonoBehaviour
    {
        public string blueprintId;
    }
}
