using Poker.AI;
using System.Collections.Generic;
using UnityEngine;

namespace Poker.Characters
{
    /// <summary>
    /// 把寫實模型（Mixamo 骨架＋ARKit blendshape，例如 Hank）掛在一個 CharacterPerformer 上，取代卡通身體（開發用預覽）。
    /// 卡通角色照常運作（看人、字幕、聲音、AI 都不變），只是 renderer 不畫；每幀把它的臉（FaceState）、前傾與轉頭抄到模型上。
    /// v2（wrap 模型）：臉是另一個 SkinnedMeshRenderer（52 組 ARKit）；眼骨頭照卡通的注視點轉（比頭轉得多、有上限）；
    /// 講話時下巴跟著語音音量（沒有聲音就照字幕節奏）；低頭幅度縮小、主要用眼睛往下看。
    /// 座標：根節點在角色面前的桌緣、桌面高度，+Z 朝牌桌中央（見 CharacterRig）。
    /// </summary>
    public sealed class RealisticBody : MonoBehaviour
    {
        // 坐姿參數（根節點本地座標，公尺／度）；身高、頭頂高度、前傾、眼睛上限依角色（RealisticSpec）
        // 手腕放在桌緣、手指沿著桌緣朝身體中間（不伸上桌：對面的牌、籌碼都在桌緣前方 2 cm 以外）；右手；左手 x 取負
        /// <summary>真人的頭（含頭髮）從玩家看過去的半徑（公尺）。</summary>
        public const float RealHeadRadius = 0.12f;
        static readonly Vector3 HandTarget = new Vector3(0.20f, 0.04f, 0.01f), HandPoint = new Vector3(-1f, -0.06f, 0.12f);
        static readonly Vector3 ElbowPole = new Vector3(0.3f, -1f, -0.4f);       // 手肘往下、往後、稍微往外彎
        /// <summary>低頭只取卡通的這麼多（看桌面主要用眼睛，不像在打瞌睡）。</summary>
        public const float HeadDownScale = 0.35f;
        /// <summary>轉頭：目標水平角的這個比例、最多幾度（見 HeadYawToward）。</summary>
        public const float HeadShare = 0.4f, HeadYawMax = 35f;
        /// <summary>胸口（Spine1）最多轉幾度：只有頭＋眼睛搆不到（旁邊的人）才轉（見 ChestYawToward）。</summary>
        public const float ChestYawMax = 15f;
        const float EyeRate = 30f;

        public Transform Model { get; private set; }
        /// <summary>身體（帽子、衣服；量身高用）。</summary>
        public SkinnedMeshRenderer Skin { get; private set; }
        /// <summary>臉（有 ARKit blendshape 的那個）。</summary>
        public SkinnedMeshRenderer FaceSkin { get; private set; }
        /// <summary>眼睛在頭裡目前的角度（度）：x 往右、y 往下（兩眼平均）。</summary>
        public Vector2 EyeAngles { get; private set; }
        /// <summary>胸口（Spine1）目前往右轉了幾度（平滑後；看旁邊的人才會轉）。</summary>
        public float ChestYaw => _chestYaw;
        /// <summary>開發用：固定表情／眼神（見 HankPin）。</summary>
        public HankPin Pin
        {
            get => _pin;
            set { _pin = value; _pinLook = null; }
        }
        HankPin _pin;
        CharacterPerformer _pinLook;   // 固定看左右邊時找到的角色（設定時找一次，不每幀掃場景）
        float _headYaw, _chestYaw, _headUp, _headDown;
        float _faceRestDown;   // 坐好、頭不轉時，臉的正前方往下幾度（上身前傾）
        /// <summary>縮放後的站姿身高（公尺）。</summary>
        public float StandingHeight { get; private set; }
        /// <summary>模型原始高度（模型單位，縮放前）。</summary>
        public float RawHeight { get; private set; }
        /// <summary>坐下後帽頂在桌面（根節點）上方幾公尺。</summary>
        public float HeadTopAboveRoot => _topLocal.y;

        public Vector3 HeadTopWorld => transform.TransformPoint(_topLocal);
        /// <summary>頭的中心（約眼睛高度）。</summary>
        public Vector3 HeadCenterWorld => Vector3.Lerp(_head.position, _headTop.position, 0.45f);
        /// <summary>臉朝向（世界）：靜止時等於根節點 forward。</summary>
        public Vector3 HeadForwardWorld => _head.rotation * Quaternion.Inverse(transform.rotation * _restHead) * transform.forward;

        CharacterPerformer _p;
        readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        Transform _head, _headTop, _spine1, _neck, _eyeL, _eyeR;
        Quaternion _eyeRestL, _eyeRestR;   // 眼骨頭在頭裡、瞳孔（+Y）轉到臉正前方時的旋轉
        Vector3 _faceFwdH, _faceUpH, _faceRightH;   // 臉的方向（頭骨頭本地座標；站姿時的正前方，不看眼骨頭怎麼擺）
        Vector2 _eyeAngL, _eyeAngR;
        readonly MouthDriver _mouth = new MouthDriver();
        Utterance _lastUtterance;
        float _peakJaw;
        bool _lastHadEnvelope;
        Transform _armL, _foreL, _handL, _armR, _foreR, _handR, _palmL, _palmR;
        Quaternion _restHead, _restNeck, _restSpine1;
        Quaternion _restArmL, _restForeL, _restArmR, _restForeR;
        Vector3 _topLocal;
        int[] _shapeIndex;
        float[] _shapeFull;   // 每組 blendshape 全開時的影格權重（glTFast 匯入是 1，FBX 常見 100）
        readonly float[] _w = new float[HankFace.Shapes.Length];

        /// <summary>掛上模型（同一個角色只掛一次）。</summary>
        public static RealisticBody Attach(CharacterPerformer p, GameObject model) => Attach(p, model, RealisticSpec.For(p.Kind));

        public static RealisticBody Attach(CharacterPerformer p, GameObject model, RealisticSpec spec)
        {
            if (p.TryGetComponent<RealisticBody>(out var existing)) return existing;
            var body = p.gameObject.AddComponent<RealisticBody>();
            body.Spec = spec;
            body.Build(p, model);
            return body;
        }

        /// <summary>這個角色的設定（身高、坐姿、眼睛上限）。</summary>
        public RealisticSpec Spec { get; private set; }

        public Transform Bone(string name) => _bones.TryGetValue("mixamorig:" + name, out var t) ? t : null;

        /// <summary>blendshape 目前開到幾 %（0–100，已依影格權重正規化）。</summary>
        public float Weight(string shape)
        {
            int i = FaceSkin.sharedMesh.GetBlendShapeIndex(shape);
            return i < 0 ? 0 : FaceSkin.GetBlendShapeWeight(i) / FullWeight(FaceSkin.sharedMesh, i) * 100f;
        }

        static float FullWeight(Mesh m, int i)
        {
            float w = m.GetBlendShapeFrameWeight(i, m.GetBlendShapeFrameCount(i) - 1);
            return w > 1e-4f ? w : 100f;
        }

        void Build(CharacterPerformer p, GameObject model)
        {
            _p = p;
            var root = transform;
            Model = new GameObject(model.name + " (realistic preview)").transform;
            Model.SetParent(root, false);
            var inst = Instantiate(model, Model, false);
            inst.name = model.name;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) _bones[t.name] = t;
            _skins = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var r in _skins)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.quality = SkinQuality.Bone4;   // Android 品質等級只給 2 根骨頭：頭盔上領口、眼皮的變形會和 Editor 不同
                r.receiveShadows = false;
                if (FaceSkin == null && r.sharedMesh.blendShapeCount > 0) FaceSkin = r;
            }
            foreach (var r in _skins)
                if (r != FaceSkin && (Skin == null || r.sharedMesh.vertexCount > Skin.sharedMesh.vertexCount)) Skin = r;
            if (FaceSkin == null) FaceSkin = Skin;
            if (Skin == null) Skin = FaceSkin;
            _head = Bone("Head"); _headTop = Bone("HeadTop_End"); _neck = Bone("Neck"); _spine1 = Bone("Spine1");
            _eyeL = Bone("LeftEye"); _eyeR = Bone("RightEye");
            _armL = Bone("LeftArm"); _foreL = Bone("LeftForeArm"); _handL = Bone("LeftHand");
            _armR = Bone("RightArm"); _foreR = Bone("RightForeArm"); _handR = Bone("RightHand");
            _palmL = Bone("LeftHandMiddle1"); _palmR = Bone("RightHandMiddle1");

            // 1. 面向：用兩肩算出模型的正面，轉成根節點的 +Z（不依賴匯入器的座標慣例）
            var right = root.InverseTransformDirection(_armR.position - _armL.position);
            var fwd = Vector3.Cross(right, Vector3.up);
            fwd.y = 0;
            Model.localRotation = Quaternion.Inverse(Quaternion.LookRotation(fwd.normalized, Vector3.up));
            // 臉的方向從頭骨頭來（站姿、還沒坐下前傾時的正前方）：眼骨頭靜止時往哪傾（瞳孔朝下、兩眼內聚）不影響眼睛上限與眼皮
            var headInv = Quaternion.Inverse(_head.rotation);
            _faceFwdH = (headInv * root.forward).normalized;
            _faceUpH = Vector3.ProjectOnPlane(headInv * root.up, _faceFwdH).normalized;
            _faceRightH = Vector3.Cross(_faceUpH, _faceFwdH);

            // 2. 縮放到真人身高（用實際蒙皮後的頂點算，含帽子）
            // BakeMesh 只套 renderer 自己的縮放，所以只在縮放前（全部 1 倍）烘一次；帽頂比 HeadTop_End 高多少也在這時記下
            var b = BakedBounds();
            if (_headTop == null)
            {
                // 沒有頭頂骨頭（荷官）：在頭的正上方、網格最高點放一個
                _headTop = new GameObject("mixamorig:HeadTop_End").transform;
                _headTop.SetParent(_head, true);
                _headTop.position = new Vector3(_head.position.x, b.max.y, _head.position.z);
                _bones[_headTop.name] = _headTop;
            }
            float raw = b.size.y;
            float hat = b.max.y - _headTop.position.y;
            float scale = Spec.StandingHeight / raw;
            Model.localScale = Vector3.one * scale;
            StandingHeight = raw * scale;
            RawHeight = raw;

            // 3. 坐姿：大腿往前、小腿往下、腳掌放平、上身微前傾
            var F = root.forward; var U = root.up; var R = root.right;
            foreach (var side in new[] { -1f, 1f })
            {
                string n = side < 0 ? "Left" : "Right";
                Transform up = Bone(n + "UpLeg"), leg = Bone(n + "Leg"), foot = Bone(n + "Foot"), toe = Bone(n + "ToeBase");
                BoneAim.Aim(up, leg.position, F + R * (0.12f * side) - U * 0.05f);
                BoneAim.Aim(leg, foot.position, -U + F * 0.15f);
                BoneAim.Aim(foot, toe.position, F - U * 0.4f);
            }
            var spine = Bone("Spine");
            spine.rotation = Quaternion.AngleAxis(Spec.SpineLean, R) * spine.rotation;

            // 4. 放到座位：帽頂在桌面上方 HeadTop、骨盆在桌緣後方
            var hips = root.InverseTransformPoint(Bone("Hips").position);
            float topY = root.InverseTransformPoint(_headTop.position).y + hat * scale;
            Model.localPosition += new Vector3(-hips.x, Spec.HeadTop - topY, -Spec.HipsBehind - hips.z);
            _topLocal = new Vector3(0, Spec.HeadTop, root.InverseTransformPoint(_headTop.position).z);

            // 5. 雙手放在桌緣前（兩段式 IK）
            _restArmL = _armL.localRotation; _restForeL = _foreL.localRotation;
            _restArmR = _armR.localRotation; _restForeR = _foreR.localRotation;
            SolveArms();
            _restArmL = _armL.localRotation; _restForeL = _foreL.localRotation;
            _restArmR = _armR.localRotation; _restForeR = _foreR.localRotation;
            _restSpine1 = Quaternion.Inverse(root.rotation) * _spine1.rotation;
            _restNeck = Quaternion.Inverse(root.rotation) * _neck.rotation;
            _restHead = Quaternion.Inverse(root.rotation) * _head.rotation;
            var faceRest = root.InverseTransformDirection(_head.rotation * _faceFwdH);
            _faceRestDown = -Mathf.Atan2(faceRest.y, new Vector2(faceRest.x, faceRest.z).magnitude) * Mathf.Rad2Deg;
            if (_eyeL != null && _eyeR != null)
            {
                var hi = Quaternion.Inverse(_head.rotation);
                _eyeRestL = Quaternion.FromToRotation(hi * _eyeL.up, _faceFwdH) * hi * _eyeL.rotation;
                _eyeRestR = Quaternion.FromToRotation(hi * _eyeR.up, _faceFwdH) * hi * _eyeR.rotation;
            }

            // 6. 卡通身體不畫（字幕泡泡除外）；卡通每幀會重開部分 renderer 的 enabled，所以用 forceRenderingOff
            var bubble = p.Subtitle != null ? p.Subtitle.Transform : null;
            foreach (var r in p.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(Model) || (bubble != null && r.transform.IsChildOf(bubble))) continue;
                r.forceRenderingOff = true;
            }

            // 7. 卡通的頭（玩家注視判定、聲音位置）搬到 Hank 的頭；泡泡移到頭旁邊，不擋臉
            var delta = HeadCenterWorld - p.Rig.Head.position;
            p.Rig.HipsRest += p.Rig.Hips.parent.InverseTransformVector(delta);
            var headLocal = root.InverseTransformPoint(HeadCenterWorld);
            p.BubbleAnchor = new Vector3(headLocal.x - 0.32f, Spec.HeadTop - 0.04f, headLocal.z);
            p.HeadRadius = RealHeadRadius;   // 泡泡避開頭用（卡通頭比較大）

            _shapeIndex = new int[HankFace.Shapes.Length];
            _shapeFull = new float[HankFace.Shapes.Length];
            var fm = FaceSkin.sharedMesh;
            for (int i = 0; i < _shapeIndex.Length; i++)
            {
                _shapeIndex[i] = fm.GetBlendShapeIndex(HankFace.Shapes[i]);
                _shapeFull[i] = _shapeIndex[i] >= 0 ? FullWeight(fm, _shapeIndex[i]) : 100f;
            }
        }

        void SolveArms()
        {
            var root = transform;
            _armL.localRotation = _restArmL; _foreL.localRotation = _restForeL;
            _armR.localRotation = _restArmR; _foreR.localRotation = _restForeR;
            var mirror = new Vector3(-1, 1, 1);
            BoneAim.TwoBone(_armR, _foreR, _handR, root.TransformPoint(HandTarget), root.TransformPoint(HandTarget + ElbowPole));
            BoneAim.TwoBone(_armL, _foreL, _handL, root.TransformPoint(Vector3.Scale(HandTarget, mirror)),
                root.TransformPoint(Vector3.Scale(HandTarget + ElbowPole, mirror)));
            BoneAim.Aim(_handR, _palmR.position, root.TransformDirection(HandPoint));
            BoneAim.Aim(_handL, _palmL.position, root.TransformDirection(Vector3.Scale(HandPoint, mirror)));
        }

        SkinnedMeshRenderer[] _skins;

        Bounds BakedBounds()
        {
            Bounds bb = default;
            bool first = true;
            var m = new Mesh();
            foreach (var r in _skins)
            {
                r.BakeMesh(m, true);
                var st = r.transform;
                foreach (var v in m.vertices)
                {
                    var p = st.position + st.rotation * v;
                    if (first) { bb = new Bounds(p, Vector3.zero); first = false; }
                    else bb.Encapsulate(p);
                }
            }
            Destroy(m);
            return bb;
        }

        int _lastSyncFrame = -1;

        void LateUpdate()
        {
            // 角色可能自己更新（AutoUpdate），也可能由 GameController 在 Update 裡 Tick（實機就是這樣）：這一幀有被 Tick、還沒同步就跟上
            if (_p != null && _p.LastTickFrame == Time.frameCount && _lastSyncFrame != Time.frameCount) Sync(Time.deltaTime);
        }

        /// <summary>同 Sync(dt)，dt 用這一幀的 Time.deltaTime。</summary>
        public void Sync() => Sync(Time.deltaTime);

        /// <summary>把卡通這一幀的臉、前傾與轉頭抄到模型上，再算眼睛與講話的嘴（AutoUpdate 關掉時由外部在 Tick 之後呼叫）。</summary>
        public void Sync(float dt)
        {
            if (_p == null || _p.Rig == null) return;
            _lastSyncFrame = Time.frameCount;
            var face = _p.Face;
            var gaze = _p.GazePoint;
            var headAng = _p.HeadAngles;
            if (Pin.Active)
            {
                if (Pin.Mood.HasValue) face = FaceState.FromMood(Pin.Mood.Value);
                face.Blink = Pin.Blink ? 1f : 0f;   // 固定時不隨機眨眼（截圖才乾淨）
                if (Pin.Look != HankLook.None)
                {
                    gaze = PinTarget(Pin.Look);
                    headAng = HeadAnglesToward(gaze);
                }
            }

            // 上身前傾（卡通的 Hips 旋轉取一半）、轉頭（脖子一半、頭全部）：轉頭與低頭縮小，剩下交給眼睛
            var R = transform.rotation;
            var lean = Quaternion.Slerp(Quaternion.identity, _p.Rig.Hips.localRotation, 0.5f);
            float pitch = headAng.x > 0 ? headAng.x * HeadDownScale : headAng.x;
            // 注視點會瞬間跳（待機瞄別處、固定眼神）：胸口和頭平滑追上（同卡通轉頭速度），眼睛可以跳
            float headMax = Spec.HeadYawLimit > 0 ? Spec.HeadYawLimit : HeadYawMax, chestMax = Spec.ChestYawLimit > 0 ? Spec.ChestYawLimit : ChestYawMax;
            float target = GazeYaw(gaze), chestT = ChestYawToward(target, headMax, chestMax), yawT = HeadYawToward(target - chestT, headMax);
            float k = dt > 0 ? 1f - Mathf.Exp(-7f * _p.Spec.Snappiness * dt) : 1f;
            _chestYaw = Mathf.Lerp(_chestYaw, chestT, k);
            _headYaw = Mathf.Lerp(_headYaw, yawT, k);
            // 眼睛往上只能轉 Spec.EyeMaxUp（再上去瞳孔就躲進上眼皮）：目標更高時頭往上抬補足
            _headUp = Mathf.Lerp(_headUp, HeadUpFor(gaze, pitch), k);
            // 往下同理：有些模型的眼皮跟不上眼球（見 RealisticSpec），眼睛只轉到它的上限，其餘由頭低下去
            _headDown = Mathf.Lerp(_headDown, HeadDownFor(gaze, pitch), k);
            pitch += _headDown - _headUp;
            var chest = lean * Quaternion.Euler(0, _chestYaw, 0);
            var look = Quaternion.Euler(pitch, _headYaw, headAng.z);
            _spine1.rotation = R * chest * _restSpine1;
            _neck.rotation = R * chest * Quaternion.Slerp(Quaternion.identity, look, 0.5f) * _restNeck;
            _head.rotation = R * chest * look * _restHead;

            // 眼睛：各自看向注視點，夾在上限內
            if (_eyeL != null && _eyeR != null)
            {
                _eyeAngL = AimEye(_eyeL, _eyeRestL, gaze, _eyeAngL, dt);
                _eyeAngR = AimEye(_eyeR, _eyeRestR, gaze, _eyeAngR, dt);
                EyeAngles = (_eyeAngL + _eyeAngR) * 0.5f;
                face.EyeYaw = EyeAngles.x;
                face.EyePitch = EyeAngles.y;
            }

            // 講話的嘴
            UpdateMouth(dt, Pin.Active && Pin.Talk);
            face.Jaw = _mouth.Jaw;
            face.Round = _mouth.Round;
            face.Close = _mouth.Close;

            HankFace.Map(face, _w, maxUp: Spec.EyeMaxUp, maxDown: Spec.EyeMaxDown, lookDownGain: Spec.LookDownGain);
            for (int i = 0; i < _w.Length; i++)
                if (_shapeIndex[i] >= 0) FaceSkin.SetBlendShapeWeight(_shapeIndex[i], _w[i] * _shapeFull[i]);

            // 上身動了，手仍放在桌上
            SolveArms();
        }

        /// <summary>頭要額外往上抬幾度，眼睛才不用往上超過 Spec.EyeMaxUp（留 15% 餘裕）；pitch 是頭目前的俯角（+ 往下）。</summary>
        float HeadUpFor(Vector3 gaze, float pitch)
        {
            var d = transform.InverseTransformDirection(gaze - HeadCenterWorld);
            float elevUp = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            float eyeUp = elevUp + _faceRestDown + pitch;
            return Mathf.Clamp(eyeUp - Spec.EyeMaxUp * 0.85f, 0f, 30f);
        }

        /// <summary>頭要額外低幾度，眼睛才不用往下超過 Spec.EyeMaxDown（留 15% 餘裕，最多 Spec.HeadDownMax）；pitch 是頭目前的俯角（+ 往下）。</summary>
        float HeadDownFor(Vector3 gaze, float pitch)
        {
            if (Spec.HeadDownMax <= 0f) return 0f;
            var d = transform.InverseTransformDirection(gaze - HeadCenterWorld);
            float elevDown = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            float eyeDown = elevDown - _faceRestDown - pitch;
            return Mathf.Clamp(eyeDown - Spec.EyeMaxDown * 0.85f, 0f, Spec.HeadDownMax);
        }

        /// <summary>注視點在根節點座標的水平角（從脖子量，+ 往角色的右邊）。</summary>
        float GazeYaw(Vector3 gaze)
        {
            var d = transform.InverseTransformPoint(gaze) - transform.InverseTransformPoint(_neck.position);
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 胸口轉角：頭（上限）＋眼睛（留 15% 餘裕）搆得到就不轉（看玩家、看桌面），搆不到的部分由胸口補，最多 ChestYawMax。
        /// 頭的轉角（HeadYawToward）從胸口算起。
        /// </summary>
        public static float ChestYawToward(float targetYaw) => ChestYawToward(targetYaw, HeadYawMax, ChestYawMax);

        /// <summary>同上，頭和胸口的上限依角色（RealisticSpec.HeadYawLimit／ChestYawLimit）。</summary>
        public static float ChestYawToward(float targetYaw, float headMax, float chestMax)
        {
            float over = Mathf.Abs(targetYaw) - headMax - EyeAim.MaxYaw * 0.85f;
            return Mathf.Sign(targetYaw) * Mathf.Clamp(over, 0f, chestMax);
        }

        /// <summary>
        /// 頭與眼分攤轉角（真人看旁邊的人：眼睛轉多、頭轉少）：頭轉目標角的 HeadShare，不夠眼睛搆到時再多轉一點（眼睛留 15% 餘裕），
        /// 但最多 HeadYawMax，不會背對對面的玩家；剩下交給眼睛（到它的上限為止）。
        /// </summary>
        public static float HeadYawToward(float targetYaw) => HeadYawToward(targetYaw, HeadYawMax);

        public static float HeadYawToward(float targetYaw, float headMax)
        {
            float a = Mathf.Abs(targetYaw);
            return Mathf.Sign(targetYaw) * Mathf.Min(Mathf.Max(a * HeadShare, a - EyeAim.MaxYaw * 0.85f), headMax);
        }

        Vector2 AimEye(Transform eye, Quaternion rest, Vector3 target, Vector2 cur, float dt)
        {
            var hr = _head.rotation;
            var d = target - eye.position;
            var want = EyeAim.Angles(new Vector3(Vector3.Dot(d, hr * _faceRightH), Vector3.Dot(d, hr * _faceUpH), Vector3.Dot(d, hr * _faceFwdH)), Spec.EyeMaxUp, Spec.EyeMaxDown);
            var ang = dt > 0 ? Vector2.Lerp(cur, want, 1f - Mathf.Exp(-EyeRate * dt)) : want;
            eye.rotation = hr * Quaternion.AngleAxis(ang.x, _faceUpH) * Quaternion.AngleAxis(ang.y, _faceRightH) * rest;
            return ang;
        }

        void UpdateMouth(float dt, bool pinnedTalk)
        {
            var u = _p.Speech != null ? _p.Speech.Current : null;
            if (u != _lastUtterance)
            {
                if (_lastUtterance != null)
                    Debug.Log($"[RealisticPreview] mouth {_p.Spec.DisplayName} clip={_lastUtterance.ClipId ?? "none"} " +
                              $"drive={(_lastHadEnvelope ? "voice" : "subtitle")} peakJaw={_peakJaw:F2} text=\"{_lastUtterance.Text}\"");
                _lastUtterance = u;
                _peakJaw = 0;
                _lastHadEnvelope = false;
            }
            if (pinnedTalk) { _mouth.Step(dt, true, 1f, null, 0); return; }
            if (u == null) { _mouth.Step(dt, false, null, null, 0); return; }
            float t = u.Elapsed;
            var env = u.IsStream ? null : ClipEnvelope.For(u.Clip);
            if (env != null)
            {
                _lastHadEnvelope = true;
                _mouth.Step(dt, true, ClipEnvelope.Sample(env, t), null, 0);
            }
            else
            {
                // 沒有可量的聲音：照字幕節奏（串流的長度未知，用每秒 14 個字估）
                float dur = !u.IsStream && u.Duration > 0 ? u.Duration : Mathf.Max(0.8f, (u.Text?.Length ?? 0) / 14f);
                float t01 = u.IsStream ? Mathf.Repeat(t / dur, 1f) : t / dur;
                _mouth.Step(dt, true, null, u.Text, t01);
            }
            _peakJaw = Mathf.Max(_peakJaw, _mouth.Jaw);
        }

        /// <summary>固定眼神的目標：玩家、桌面中央、或玩家視角左右邊的角色的頭。</summary>
        Vector3 PinTarget(HankLook look)
        {
            switch (look)
            {
                case HankLook.Player: return _p.LookPointFor(LookTarget.Player);
                case HankLook.Table: return _p.LookPointFor(LookTarget.Board);
            }
            var table = transform.parent;
            if (_pinLook == null)
            {
                float bestX = 0;
                foreach (var o in FindObjectsByType<CharacterPerformer>(FindObjectsSortMode.None))
                {
                    if (o == _p || o.Rig == null || o.transform.parent != table) continue;
                    float x = table != null ? table.InverseTransformPoint(o.transform.position).x : o.transform.position.x;
                    if (look == HankLook.Left ? x < 0 && (_pinLook == null || x < bestX) : x > 0 && (_pinLook == null || x > bestX)) { _pinLook = o; bestX = x; }
                }
            }
            if (_pinLook != null) return _pinLook.Rig.Head.position;
            var side = look == HankLook.Left ? -1f : 1f;
            return table != null ? table.TransformPoint(new Vector3(side * 0.7f, 0.35f, 0.5f)) : transform.TransformPoint(new Vector3(side * 0.7f, 0.35f, 0.5f));
        }

        /// <summary>和卡通同一套轉頭（跟隨 62%、上限）：固定眼神時自己算。</summary>
        Vector3 HeadAnglesToward(Vector3 target)
        {
            var d = transform.InverseTransformPoint(target) - transform.InverseTransformPoint(_neck.position);
            float yaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg * 0.62f, -55f, 55f);
            float pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg * 0.62f * 0.85f, -25f, 30f);
            return new Vector3(pitch, yaw, 0);
        }

        static void Destroy(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
