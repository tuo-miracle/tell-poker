using UnityEngine;
using UnityEngine.Rendering;

namespace Poker.Game
{
    /// <summary>
    /// 適合 passthrough 的燈光：暖色主光從玩家身後上方照向對面角色的臉、冷色補光從對面打回來、柔和的平均環境光。
    /// 不投影（URP 陰影本來就關掉）。場景產生器與截圖都用這一份設定；執行期再套一次環境光（ambientProbe）確保一致。
    /// 方向是「相對牌桌」的：燈每幀跟著牌桌轉（2026-10-30：牌桌在房間裡可能朝任何方向，世界座標的燈會變成側光，
    /// 低面數的臉上出現硬邊暗塊）。沒有環境反射：沒有天空盒時的預設反射偏藍，會染在深色表面上（嘴巴內部）。
    /// </summary>
    public sealed class MrLighting : MonoBehaviour
    {
        public static readonly Color Ambient = new Color(0.50f, 0.49f, 0.48f);
        public static readonly Color KeyColor = new Color(1f, 0.95f, 0.88f);
        public static readonly Color FillColor = new Color(0.78f, 0.84f, 1f);
        public const float KeyIntensity = 1.05f, FillIntensity = 0.4f;
        public static readonly Quaternion KeyRotation = Quaternion.Euler(42f, 18f, 0f);
        public static readonly Quaternion FillRotation = Quaternion.Euler(28f, -150f, 0f);

        void Awake() => ApplyAmbient();

        /// <summary>燈跟著哪張牌桌；沒指定就用場景裡的 TableFrame（主場景只有一張）。</summary>
        public Transform Table;
        Light[] _lights;
        Quaternion[] _rest;
        Quaternion _tableRot = Quaternion.identity;

        void LateUpdate()
        {
            if (Table == null)
            {
                var frame = FindAnyObjectByType<Poker.Runtime.TableFrame>();
                if (frame == null) return;
                Table = frame.transform;
            }
            if (_lights == null)
            {
                _lights = OwnedLights();
                _rest = new Quaternion[_lights.Length];
                for (int i = 0; i < _lights.Length; i++) _rest[i] = _lights[i].transform.rotation;
            }
            if (Table.rotation == _tableRot) return;
            _tableRot = Table.rotation;
            for (int i = 0; i < _lights.Length; i++)
                if (_lights[i] != null) _lights[i].transform.rotation = _tableRot * _rest[i];
        }

        /// <summary>
        /// 這組燈：CreateRig 的燈是自己的子物件；主場景主光掛這個元件、補光是另一個物件，所以只有一盞時再加上場景裡不屬於別組的燈。
        /// </summary>
        Light[] OwnedLights()
        {
            var own = new System.Collections.Generic.List<Light>(GetComponentsInChildren<Light>());
            if (own.Count >= 2) return own.ToArray();
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (!own.Contains(l) && l.GetComponentInParent<MrLighting>() == null) own.Add(l);
            return own.ToArray();
        }

        /// <summary>設定環境光（平均色，不需要烘焙）。</summary>
        public static void ApplyAmbient()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(Ambient);
            RenderSettings.ambientProbe = sh;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 0f;
        }

        public static void ConfigureKey(Light l)
        {
            l.type = LightType.Directional;
            l.color = KeyColor;
            l.intensity = KeyIntensity;
            l.shadows = LightShadows.None;
            l.transform.rotation = KeyRotation;
        }

        public static void ConfigureFill(Light l)
        {
            l.type = LightType.Directional;
            l.color = FillColor;
            l.intensity = FillIntensity;
            l.shadows = LightShadows.None;
            l.transform.rotation = FillRotation;
        }

        /// <summary>沒有場景的時候（測試、截圖）建立一組完整燈光。</summary>
        public static GameObject CreateRig()
        {
            var root = new GameObject("MrLighting");
            root.AddComponent<MrLighting>();
            var key = new GameObject("Key Light").AddComponent<Light>();
            key.transform.SetParent(root.transform, false);
            ConfigureKey(key);
            var fill = new GameObject("Fill Light").AddComponent<Light>();
            fill.transform.SetParent(root.transform, false);
            ConfigureFill(fill);
            ApplyAmbient();
            return root;
        }
    }
}
