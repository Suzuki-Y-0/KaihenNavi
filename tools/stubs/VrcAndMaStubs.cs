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
    }

    public class VRCExpressionsMenu : ScriptableObject
    {
        public class Control
        {
            public enum ControlType { Button = 101, Toggle = 102, SubMenu = 103 }

            public class Parameter
            {
                public string name;
            }

            public string name;
            public ControlType type;
            public Parameter parameter;
            public float value;
        }
    }
}

namespace VRC.SDK3.Avatars.Components
{
    public class VRCAvatarDescriptor : MonoBehaviour
    {
        public Vector3 ViewPosition;
        public SkinnedMeshRenderer VisemeSkinnedMesh;
        public VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters expressionParameters;
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

    public class ModularAvatarMenuItem : AvatarTagComponent
    {
        public VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control Control;
        public SubmenuSource MenuSource;
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
