using UnityEngine;

namespace Poker.Characters
{
    /// <summary>
    /// 寫實角色預覽（開發用，可行性驗證）：只有 TELL_REALISTIC_PREVIEW=1 建置時 MainSceneBuilder 才把它放進場景；
    /// 預設不存在，正式版行為不變，模型也不會被打包。
    /// 場景裡有它時，把 Target 那位角色（預設 Rocco）的卡通身體換成 Model、荷官 Dot 換成 DealerModel（有給才換）；字幕、聲音、AI 照舊。
    /// 有 Cast 時改用陣容：4 個對手位（Pip、Rocco、荷官右側老手、Vex 的座位；MainSceneBuilder 依 Opponents 開 5 人局）各放 Lineup 指定的寫實角色
    /// （adb shell setprop debug.tell.lineup "Reginald,Hank,Erik,Jace"，開 App 時讀一次；改了要重開 App）；寫實角色用原座位的台詞與 AI，聲音依角色（SpeechVoices）。
    /// </summary>
    public sealed class RealisticPreview : MonoBehaviour
    {
        public const string EnvVar = "TELL_REALISTIC_PREVIEW";

        public GameObject Model;
        public CharacterKind Target = CharacterKind.Loudmouth;
        /// <summary>荷官 Dot 的寫實模型（沒有就維持卡通）。</summary>
        public GameObject DealerModel;
        /// <summary>可用的寫實角色（GLB 檔名就是名字）。有給就用陣容，不用 Model／Target。</summary>
        public GameObject[] Cast;
        /// <summary>陣容：逗號分隔的名字，依座位 Pip、Rocco、老手、Vex；「-」＝那個座位維持卡通（老手座沒有卡通，見 ResolveLineup）。</summary>
        public string Lineup = DefaultLineup;
        public const string DefaultLineup = "Marcus,Hank,Reginald,Jace";
        public const string LineupProp = "debug.tell.lineup";
        /// <summary>陣容的座位順序（玩家視角由左到右）。</summary>
        public static readonly CharacterKind[] LineupSeats = { CharacterKind.Rookie, CharacterKind.Loudmouth, CharacterKind.Veteran, CharacterKind.Cold };
        /// <summary>沒有卡通角色的座位（2026-11-02 新的老手座）：陣容沒給寫實角色時用預設陣容的那一位，不會變成空座位或借別人的卡通。</summary>
        public static readonly CharacterKind[] SeatsWithoutCartoon = { CharacterKind.Veteran };
        /// <summary>預覽建置的對手（GameConfig.Opponents，依座位順序）：5 人局。正式版沒有寫實模型，維持 3 位卡通對手。</summary>
        public static readonly Poker.AI.Archetype[] Opponents = { Poker.AI.Archetype.Rookie, Poker.AI.Archetype.Loudmouth, Poker.AI.Archetype.Veteran, Poker.AI.Archetype.Cold };
        string[] _lineup;

        /// <summary>
        /// 寫實角色坐這個座位時的設定。真實尺寸的桌子（2026-11-02）座位夠寬，不再把身體往旁邊挪；
        /// 只有玩家座位前面有 0.12 m 的扶手（荷官在缺口裡沒有）：骨盆往後 RailClearance，身體才不會穿過扶手，手照樣放在絨布邊。
        /// </summary>
        public static RealisticSpec SeatSpec(RealisticSpec spec, CharacterKind kind)
        {
            if (kind != CharacterKind.Dealer) spec.HipsBehind += RailClearance;
            return spec;
        }

        /// <summary>扶手從 4 cm 加寬到 TableSeats.RailWidth（0.12 m）多出來的距離：HipsBehind 0.18 m 是在 4 cm 扶手上調的（胸口剛好貼著扶手外緣）。</summary>
        public const float RailClearance = TableSeats.RailWidth - 0.04f;

        /// <summary>解析陣容：回傳 4 個座位的名字（null ＝維持卡通）；空字串／"0"／null 回 null（＝用預設）。名字不分大小寫，只認 cast 裡有的。</summary>
        public static string[] ParseLineup(string value, System.Collections.Generic.IEnumerable<string> cast)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() == "0") return null;
            var known = new System.Collections.Generic.List<string>(cast);
            var parts = value.Split(',');
            var seats = new string[LineupSeats.Length];
            for (int i = 0; i < seats.Length && i < parts.Length; i++)
            {
                var t = parts[i].Trim();
                seats[i] = known.Find(k => string.Equals(k, t, System.StringComparison.OrdinalIgnoreCase));
            }
            return seats;
        }

        /// <summary>實際要放的陣容：ParseLineup（空＝預設陣容），沒有卡通的座位沒給寫實角色時補上預設陣容的那一位。</summary>
        public static string[] ResolveLineup(string value, System.Collections.Generic.IEnumerable<string> cast)
        {
            var known = new System.Collections.Generic.List<string>(cast);
            var def = ParseLineup(DefaultLineup, known) ?? new string[LineupSeats.Length];
            var seats = ParseLineup(value, known) ?? def;
            for (int i = 0; i < seats.Length; i++)
                if (seats[i] == null && System.Array.IndexOf(SeatsWithoutCartoon, LineupSeats[i]) >= 0) seats[i] = def[i];
            return seats;
        }

        /// <summary>
        /// 開發用（實機驗證聲音與字幕泡泡，頭盔沒人戴、沒有手時 AI 不會講話）：adb shell setprop debug.tell.say "rookie+loudmouth:3"
        /// ＝那幾個座位同時講自己台詞本裡的第 3 句（有預錄）；改值才會再講一次。座位：rookie、loudmouth、veteran、cold、dealer。
        /// </summary>
        public const string SayProp = "debug.tell.say";
        string _say = "";

        public static CharacterKind[] ParseSay(string value)
        {
            var list = new System.Collections.Generic.List<CharacterKind>();
            if (string.IsNullOrWhiteSpace(value)) return list.ToArray();
            foreach (var part in value.Split(':')[0].Split('+'))
                if (!int.TryParse(part.Trim(), out _) && System.Enum.TryParse<CharacterKind>(part.Trim(), true, out var k)) list.Add(k);
            return list.ToArray();
        }

        static int SayNumber(string value) => int.TryParse(value.Contains(":") ? value.Substring(value.IndexOf(':') + 1) : "0", out var n) ? n : 0;

        static Poker.AI.Line SayEntry(CharacterKind kind, int n)
        {
            string prefix = kind == CharacterKind.Dealer ? "dot." : kind == CharacterKind.Rookie ? "pip." : kind == CharacterKind.Loudmouth ? "rocco."
                : kind == CharacterKind.Veteran ? "silas." : "vex.";
            var lines = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(Poker.AI.LineBook.All, l => l.Id.StartsWith(prefix)));
            return lines[((n % lines.Count) + lines.Count) % lines.Count];
        }

        public static string SayLine(CharacterKind kind, int n) => SayEntry(kind, n).Text;
        public static string SayLineId(CharacterKind kind, int n) => SayEntry(kind, n).Id;

        void CheckSay()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_sysProps == null) _sysProps = new AndroidJavaClass("android.os.SystemProperties");
            var v = _sysProps.CallStatic<string>("get", SayProp, "");
            if (v == _say) return;
            _say = v;
            int n = SayNumber(v);
            foreach (var kind in ParseSay(v))
                foreach (var p in FindObjectsByType<CharacterPerformer>(FindObjectsSortMode.None))
                    if (p.Kind == kind && p.Speech != null)
                    {
                        p.Speech.Say(SayLine(kind, n), Poker.AI.SpeechPriority.Reply, SayLineId(kind, n));
                        Debug.Log($"[RealisticPreview] say {kind}: {SayLineId(kind, n)}");
                    }
#endif
        }

        float _next;

        /// <summary>環境變數的值是不是「開」（只有 "1" 算）。</summary>
        public static bool IsOn(string value) => value == "1";

        /// <summary>掛在相機底下、跟著頭走的 renderer（包含沒啟用的）。</summary>
        public static Renderer[] CameraLockedRenderers(Transform camera) => camera != null ? camera.GetComponentsInChildren<Renderer>(true) : new Renderer[0];

        /// <summary>
        /// 開發用（實機查畫面上不明的東西）：adb shell setprop debug.tell.dumpview 1、2、3… 把畫面上所有 renderer／Canvas／合成層寫進 log；
        /// debug.tell.cull 1 ＝相機什麼都不畫，0 ＝恢復。
        /// </summary>
        const string DumpViewProp = "debug.tell.dumpview", CullProp = "debug.tell.cull";
        string _dumpView = "", _cull = "";
        int _cullMask = -1;

        /// <summary>寫出相機看得到的東西（名字、路徑、在畫面上的範圍、材質）。</summary>
        public static void DumpView(Camera cam)
        {
            if (cam == null) { Debug.Log("[ViewDump] no camera"); return; }
            var locked = new System.Collections.Generic.HashSet<Renderer>(CameraLockedRenderers(cam.transform));
            Debug.Log($"[ViewDump] camera {Path(cam.transform)} cullingMask={cam.cullingMask} clear={cam.clearFlags} bg={cam.backgroundColor}");
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff) continue;
                var b = r.bounds;
                var c = b.center; var e = b.extents;
                Vector2 min = Vector2.one * 9, max = Vector2.one * -9;
                bool behind = true;
                for (int i = 0; i < 8; i++)
                {
                    var v = cam.WorldToViewportPoint(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                    if (v.z > 0) behind = false;
                    min = Vector2.Min(min, v); max = Vector2.Max(max, v);
                }
                if (behind || max.x < 0 || max.y < 0 || min.x > 1 || min.y > 1) continue;
                var m = r.sharedMaterial;
                Debug.Log($"[ViewDump] {(locked.Contains(r) ? "HEADLOCK " : "")}{Path(r.transform)} {r.GetType().Name} view=({min.x:F2},{min.y:F2})-({max.x:F2},{max.y:F2}) mat={(m != null ? m.name + "/" + m.shader.name : "null")}");
            }
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) Debug.Log($"[ViewDump] Canvas {Path(c.transform)} mode={c.renderMode} enabled={c.enabled}");
            foreach (var b in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                var n = b.GetType().Name;
                if (n.Contains("Overlay") || n.Contains("Passthrough")) Debug.Log($"[ViewDump] layer {n} on {Path(b.transform)} enabled={b.enabled}");
            }
            Debug.Log("[ViewDump] end");
        }

        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;

        /// <summary>
        /// 牌桌要放在哪裡，相機才會正好在牌桌座標的 eye、看向 lookAt（實機截圖用：頭盔沒人戴、隨便擺在桌上時，
        /// 把整張牌桌連同角色搬到相機前面，畫面就等於玩家座位的視角；世界座標的燈光方向會跟著不同）。
        /// </summary>
        public static Pose TableForView(Pose camera, Vector3 eye, Vector3 lookAt)
        {
            var eyeRot = Quaternion.LookRotation(lookAt - eye, Vector3.up);
            var rot = camera.rotation * Quaternion.Inverse(eyeRot);
            return new Pose(camera.position - rot * eye, rot);
        }

        /// <summary>玩家眼睛與注視點（牌桌座標），同 Editor 截圖工具。</summary>
        public static readonly Vector3 PlayerEye = new Vector3(0, 0.45f, -0.25f), PlayerLook = new Vector3(0, 0.15f, 0.6f);
        /// <summary>改這個 Android 系統屬性（adb shell setprop debug.tell.fitview 1、2、3…）＝重新把牌桌搬到相機前。</summary>
        const string FitViewProp = "debug.tell.fitview";
        string _fitView = "";
        /// <summary>開發用：adb shell setprop debug.tell.hankface "talk,happy,player" 把 Hank 固定在某個表情／眼神（見 HankPin）；0 ＝放開。</summary>
        public const string PinProp = "debug.tell.hankface";
        string _pin = "";

        /// <summary>套用固定表情（同 debug.tell.hankface 的值）到所有寫實角色。</summary>
        public void SetPin(string value)
        {
            _pin = value ?? "";
            var pin = HankPin.Parse(_pin);
            foreach (var b in FindObjectsByType<RealisticBody>(FindObjectsSortMode.None)) b.Pin = pin;
            Debug.Log($"[RealisticPreview] pin \"{_pin}\" active={pin.Active}");
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaClass _sysProps;
        void OnDestroy() => _sysProps?.Dispose();
#endif

        void Update()
        {
            // 角色是 GameController 在執行期建的（重開牌局也可能重建），定期找一次，不改 GameController
            if ((Model == null && (Cast == null || Cast.Length == 0)) || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            CheckFitView();
            CheckPin();
            CheckSay();
            CheckDiag();
            foreach (var p in FindObjectsByType<CharacterPerformer>(FindObjectsSortMode.None))
            {
                var model = p.Kind == CharacterKind.Dealer ? DealerModel : ModelFor(p.Kind);
                if (p.Rig == null || model == null || p.TryGetComponent<RealisticBody>(out _)) continue;
                var spec = SeatSpec(RealisticSpec.ByName(model.name) ?? RealisticSpec.For(p.Kind), p.Kind);
                var body = RealisticBody.Attach(p, model, spec);
                body.Pin = HankPin.Parse(_pin);
                p.Speech?.UseVoice(SpeechVoices.ForCharacter(model.name, p.Kind));   // 台詞依座位，聲音依角色（SpeechVoices）
                Debug.Log($"[RealisticPreview] {p.Spec.DisplayName} -> {model.name} (head top {body.HeadTopAboveRoot:F2} m above table)");
            }
        }

        /// <summary>這個座位要放哪個寫實模型（沒有就 null，維持卡通）。</summary>
        GameObject ModelFor(CharacterKind kind)
        {
            if (Cast == null || Cast.Length == 0) return kind == Target ? Model : null;
            if (_lineup == null)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var c in Cast) if (c != null) names.Add(c.name);
                var v = Lineup;
#if UNITY_ANDROID && !UNITY_EDITOR
                if (_sysProps == null) _sysProps = new AndroidJavaClass("android.os.SystemProperties");
                var prop = _sysProps.CallStatic<string>("get", LineupProp, "");
                if (ParseLineup(prop, names) != null) v = prop;
#endif
                _lineup = ResolveLineup(v, names);
                Debug.Log($"[RealisticPreview] lineup (Pip, Rocco, Silas, Vex seats): {string.Join(", ", System.Array.ConvertAll(_lineup, n => n ?? "cartoon"))}");
            }
            int seat = System.Array.IndexOf(LineupSeats, kind);
            if (seat < 0 || _lineup[seat] == null) return null;
            foreach (var c in Cast) if (c != null && c.name == _lineup[seat]) return c;
            return null;
        }

        void CheckDiag()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_sysProps == null) _sysProps = new AndroidJavaClass("android.os.SystemProperties");
            var dump = _sysProps.CallStatic<string>("get", DumpViewProp, "");
            if (dump != _dumpView) { _dumpView = dump; if (!string.IsNullOrEmpty(dump) && dump != "0") DumpView(Camera.main); }
            var cull = _sysProps.CallStatic<string>("get", CullProp, "");
            if (cull != _cull)
            {
                _cull = cull;
                var cam = Camera.main;
                if (cam != null)
                {
                    if (cull == "1") { _cullMask = cam.cullingMask; cam.cullingMask = 0; }
                    else if (_cullMask != -1 || cam.cullingMask == 0) cam.cullingMask = _cullMask;
                    Debug.Log($"[ViewDump] cull={cull} cullingMask={cam.cullingMask}");
                }
            }
#endif
        }

        void CheckPin()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_sysProps == null) _sysProps = new AndroidJavaClass("android.os.SystemProperties");
            var v = _sysProps.CallStatic<string>("get", PinProp, "");
            if (v != _pin) SetPin(v);
#endif
        }

        void CheckFitView()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_sysProps == null) _sysProps = new AndroidJavaClass("android.os.SystemProperties");
            var v = _sysProps.CallStatic<string>("get", FitViewProp, "");
            if (string.IsNullOrEmpty(v) || v == "0" || v == _fitView) return;
            var cam = Camera.main;
            CharacterPerformer target = null;
            foreach (var p in FindObjectsByType<CharacterPerformer>(FindObjectsSortMode.None)) if (p.Kind == Target) target = p;
            if (cam == null || target == null || target.transform.parent == null) return;   // 角色還沒建好：下次再試
            _fitView = v;
            var table = target.transform.parent;   // 角色都掛在牌桌（TableFrame）底下
            // debug.tell.fitback <公尺>：相機往玩家座位後面退（截圖看到的範圍比頭盔窄，退後才拍得到側邊座位的全身）
            float back = float.TryParse(_sysProps.CallStatic<string>("get", "debug.tell.fitback", "0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : 0f;
            var pose = TableForView(new Pose(cam.transform.position, cam.transform.rotation), PlayerEye + new Vector3(0, 0, -back), PlayerLook);
            table.SetPositionAndRotation(pose.position, pose.rotation);
            Debug.Log($"[RealisticPreview] fit view {v}: table moved so the camera sits at the player seat");
#endif
        }
    }
}
