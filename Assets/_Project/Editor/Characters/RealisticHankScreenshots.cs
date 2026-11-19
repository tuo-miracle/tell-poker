using System;
using System.Collections.Generic;
using System.IO;
using Poker.AI;
using Poker.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Poker.Editor.Characters
{
    /// <summary>
    /// 寫實角色預覽（Hank 坐 Rocco 的座位）的截圖，仿 CharacterScreenshots（batchmode，不要加 -nographics）。
    /// 用法：Unity -batchmode -projectPath … -executeMethod Poker.Editor.Characters.RealisticHankScreenshots.CaptureBatch -shotDir &lt;輸出資料夾&gt;
    /// </summary>
    public static class RealisticHankScreenshots
    {
        const float Dt = 1f / 60f;
        const string HankPath = "Assets/_Project/Art/Characters/Hank/Hank.glb";
        const string DealerPath = "Assets/_Project/Art/Characters/Dealer/Dealer.glb";
        /// <summary>-who dealer：拍寫實荷官（Dot 的座位）；預設 Hank（Rocco 的座位）。</summary>
        static bool IsDealer => Arg("-who") == "dealer";
        /// <summary>-who &lt;名字&gt;：Hank（預設）、dealer、Marcus、Jace、Reginald、Erik；-seat rookie|loudmouth|veteran|cold 選座位（預設 Rocco 的座位）。</summary>
        static string WhoName => IsDealer ? "Dealer" : string.IsNullOrEmpty(Arg("-who")) ? "Hank" : char.ToUpperInvariant(Arg("-who")[0]) + Arg("-who").Substring(1).ToLowerInvariant();
        static string ModelPath => $"Assets/_Project/Art/Characters/{WhoName}/{WhoName}.glb";
        static CharacterKind WhoKind => IsDealer ? CharacterKind.Dealer : Arg("-seat") == "rookie" ? CharacterKind.Rookie : Arg("-seat") == "cold" ? CharacterKind.Cold : Arg("-seat") == "veteran" ? CharacterKind.Veteran : CharacterKind.Loudmouth;
        static (string text, string id) TalkLine => IsDealer ? ("Whenever you're ready.", "dot.yourturn.03") : ("Go ahead, call. I dare you.", "rocco.nervous.02");

        /// <summary>掛寫實模型；-eyeMaxUp &lt;度&gt; 暫時改眼睛往上的上限（量眼皮遮瞳孔用）。</summary>
        static RealisticBody AttachWho(CharacterPerformer p, GameObject model)
        {
            var spec = RealisticSpec.ByName(model.name) ?? RealisticSpec.For(p.Kind);
            if (float.TryParse(Arg("-eyeMaxUp"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var up)) spec.EyeMaxUp = up;
            return RealisticBody.Attach(p, model, spec);
        }
        // 玩家眼睛（牌桌座標），同 CharacterScreenshots
        static readonly Vector3 PlayerEye = new Vector3(0, 0.45f, -0.25f);
        const int Size = 1000;

        static string _dir;
        static Camera _cam;
        static Transform _table;

        public static void CaptureBatch()
        {
            int code = 0;
            try
            {
                _dir = Arg("-shotDir") ?? Path.Combine(Path.GetTempPath(), "realistic-hank");
                Directory.CreateDirectory(_dir);
                CharacterMaterialAssets.Ensure();
                CaptureAll();
                Debug.Log("[RealisticHankScreenshots] DONE " + _dir);
            }
            catch (Exception e)
            {
                Debug.LogError("[RealisticHankScreenshots] FAILED " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        /// <summary>轉頭／眼睛分攤：看玩家、左右鄰座、桌面（玩家視角的全景＋特寫）。-shotTag 加在檔名前（before／after）。</summary>
        public static void CaptureGazeBatch()
        {
            int code = 0;
            try
            {
                _dir = Arg("-shotDir") ?? Path.Combine(Path.GetTempPath(), "realistic-hank");
                string tag = Arg("-shotTag") ?? "gaze";
                Directory.CreateDirectory(_dir);
                CharacterMaterialAssets.Ensure();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (model == null) throw new FileNotFoundException(ModelPath);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SetupStage();
                var list = new List<CharacterPerformer>();
                foreach (CharacterKind k in Enum.GetValues(typeof(CharacterKind))) list.Add(CharacterFactory.CreateAtSeat(k, _table));
                foreach (var p in list) { p.AutoUpdate = false; p.SetViewer(_cam.transform); }
                var rocco = list.Find(p => p.Kind == WhoKind);
                var body = AttachWho(rocco, model);
                foreach (var p in list) p.Play(new PerformanceCue());
                Simulate(list, body, 2f);
                var shots = new List<string>();
                foreach (var look in new[] { "player", "left", "right", "table" })
                {
                    body.Pin = HankPin.Parse("neutral," + look);
                    Simulate(list, body, 2f);
                    var root = rocco.transform;
                    var f = root.InverseTransformDirection(body.HeadForwardWorld);
                    Debug.Log($"[RealisticHankScreenshots] gaze {tag} {look}: head yaw {Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg:F1}°, eyes {body.EyeAngles.ToString("F1")}");
                    Aim(PlayerEye, new Vector3(-0.2f, 0.35f, 0.85f), 62f);
                    shots.Add(Save($"{tag}_gaze_{look}_wide"));
                    CloseUp(body.HeadCenterWorld, 22f);
                    shots.Add(Save($"{tag}_gaze_{look}_close"));
                }
                Sheet($"{tag}_gaze_sheet", shots, 4);

                // 往上看（眼睛上限 +15°）：上眼皮要跟著抬（eyeLookUp），瞳孔不能被蓋住
                body.Pin = default;
                var high = new GameObject("HighViewer").transform;
                var ups = new List<string>();
                foreach (var deg in new[] { -14f, -12f, -10f, -8f, -6f, -4f, -2f, 0f, 8f, 25f })
                {
                    var eye = body.HeadCenterWorld;
                    high.position = eye + rocco.transform.TransformDirection(Quaternion.Euler(-deg, 0, 0) * Vector3.forward) * 1.2f;
                    rocco.SetViewer(high);
                    rocco.SetGazedAt(true);
                    rocco.Play(new PerformanceCue { Mood = Mood.Neutral, Look = LookTarget.Player, Duration = 30f });
                    Simulate(list, body, 2f);
                    Debug.Log($"[RealisticHankScreenshots] look up {deg}°: eyes {body.EyeAngles.ToString("F1")} eyeLookUp={body.Weight("eyeLookUpLeft"):F0} eyeWide={body.Weight("eyeWideLeft"):F0} blink={body.Weight("eyeBlinkLeft"):F0}");
                    CloseUp(body.HeadCenterWorld, 14f);
                    ups.Add(Save($"{tag}_look_up_{deg:F0}"));
                    if (Arg("-eyeZoom") != null)
                    {
                        // 量瞳孔露出多少：從玩家的位置、很窄的視角拍兩眼（檔名含眼睛實際往上幾度）
                        foreach (var e in new[] { "LeftEye", "RightEye" })
                        {
                            _cam.transform.position = _table.TransformPoint(PlayerEye);
                            _cam.transform.LookAt(body.Bone(e).position);
                            _cam.fieldOfView = 2.2f;
                            Save($"{tag}_zoom_{e}_up{-body.EyeAngles.y:F1}");
                        }
                    }
                }
                Sheet($"{tag}_look_up_sheet", ups, 5);
                if (Arg("-eyeZoom") != null)
                {
                    // 同一套，往下看：目標在眼睛下方幾度（看桌面的範圍）；量上眼皮跟不跟得上虹膜
                    foreach (var deg in new[] { 10f, 20f, 30f, 40f, 50f })
                    {
                        var eye = body.HeadCenterWorld;
                        high.position = eye + rocco.transform.TransformDirection(Quaternion.Euler(deg, 0, 0) * Vector3.forward) * 0.8f;
                        rocco.SetViewer(high);
                        rocco.SetGazedAt(true);
                        rocco.Play(new PerformanceCue { Mood = Mood.Neutral, Look = LookTarget.Player, Duration = 30f });
                        Simulate(list, body, 2f);
                        Debug.Log($"[RealisticHankScreenshots] look down {deg}°: eyes {body.EyeAngles.ToString("F1")} eyeLookDown={body.Weight("eyeLookDownLeft"):F0}");
                        foreach (var e in new[] { "LeftEye", "RightEye" })
                        {
                            _cam.transform.position = _table.TransformPoint(PlayerEye);
                            _cam.transform.LookAt(body.Bone(e).position);
                            _cam.fieldOfView = 2.2f;
                            Save($"{tag}_zoomdown_{e}_down{body.EyeAngles.y:F1}");
                        }
                    }
                }
                // 診斷：上限往上看時，eyeLookUp 開多少才看得到瞳孔（直接改權重，不經 HankFace）
                if (Arg("-lidSweep") != null)
                {
                    var sweep = new List<string>();
                    var fs = body.FaceSkin;
                    foreach (var w in new[] { 0f, 40f, 70f, 100f })
                    {
                        foreach (var n in new[] { "eyeLookUpLeft", "eyeLookUpRight" })
                        {
                            int i = fs.sharedMesh.GetBlendShapeIndex(n);
                            fs.SetBlendShapeWeight(i, w * fs.sharedMesh.GetBlendShapeFrameWeight(i, fs.sharedMesh.GetBlendShapeFrameCount(i) - 1) / 100f);
                        }
                        CloseUp(body.HeadCenterWorld, 10f);
                        sweep.Add(Save($"{tag}_lid_sweep_{w:F0}"));
                    }
                    Sheet($"{tag}_lid_sweep_sheet", sweep, 4);
                }
                rocco.SetViewer(_cam.transform);
                Debug.Log("[RealisticHankScreenshots] DONE " + _dir);
            }
            catch (Exception e)
            {
                Debug.LogError("[RealisticHankScreenshots] FAILED " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        /// <summary>診斷：匯入後的臉部網格（位置、法線、切線數）寫成 JSON，和 GLB 原始資料比對。-shotDir 是輸出資料夾。</summary>
        public static void DumpMeshBatch()
        {
            int code = 0;
            try
            {
                _dir = Arg("-shotDir") ?? Path.GetTempPath();
                Directory.CreateDirectory(_dir);
                foreach (var (name, path) in new[] { ("dealer", DealerPath), ("hank", HankPath) })
                {
                    var r = Array.Find(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<SkinnedMeshRenderer>(true), x => x.sharedMesh.blendShapeCount > 0);
                    var m = r.sharedMesh;
                    var sb = new System.Text.StringBuilder("{\"tangents\":" + m.tangents.Length + ",\"readable\":" + (m.isReadable ? "true" : "false") + ",\"materials\":[");
                    sb.Append(string.Join(",", Array.ConvertAll(r.sharedMaterials, x => "\"" + x.name + "\"")));
                    sb.Append("],\"v\":[");
                    var v = m.vertices; var n = m.normals;
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    for (int i = 0; i < v.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(string.Format(inv, "[{0:R},{1:R},{2:R},{3:R},{4:R},{5:R}]", v[i].x, v[i].y, v[i].z, n[i].x, n[i].y, n[i].z));
                    }
                    sb.Append("]}");
                    File.WriteAllText(Path.Combine(_dir, name + "_face_imported.json"), sb.ToString());
                }
            }
            catch (Exception e) { Debug.LogError("[RealisticHankScreenshots] FAILED " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        /// <summary>陣容：-lineup "Marcus,Hank,Reginald,Jace"（Pip、Rocco、老手、Vex 的座位）＋寫實荷官，燈同主場景；玩家視角全景與每位特寫（看玩家、看桌面）。</summary>
        public static void LineupBatch()
        {
            int code = 0;
            try
            {
                _dir = Arg("-shotDir") ?? Path.Combine(Path.GetTempPath(), "realistic-lineup");
                Directory.CreateDirectory(_dir);
                CharacterMaterialAssets.Ensure();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SetupStage();
                foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(l.gameObject);
                var key = new GameObject("Key").AddComponent<Light>();
                Poker.Game.MrLighting.ConfigureKey(key);
                var fill = new GameObject("Fill").AddComponent<Light>();
                Poker.Game.MrLighting.ConfigureFill(fill);
                Poker.Game.MrLighting.ApplyAmbient();
                var list = new List<CharacterPerformer>();
                foreach (CharacterKind k in Enum.GetValues(typeof(CharacterKind))) list.Add(CharacterFactory.CreateAtSeat(k, _table));
                foreach (var p in list) { p.AutoUpdate = false; p.SetViewer(_cam.transform); p.Play(new PerformanceCue()); }
                var cast = new[] { "Hank", "Marcus", "Jace", "Reginald", "Erik" };
                var seats = RealisticPreview.ResolveLineup(Arg("-lineup") ?? RealisticPreview.DefaultLineup, cast);
                var bodies = new List<(string name, RealisticBody body)>();
                for (int i = 0; i < seats.Length; i++)
                {
                    if (seats[i] == null) continue;
                    var p = list.Find(x => x.Kind == RealisticPreview.LineupSeats[i]);
                    var spec = RealisticPreview.SeatSpec(RealisticSpec.ByName(seats[i]).Value, p.Kind);
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    if (float.TryParse(Arg("-headYawMax"), System.Globalization.NumberStyles.Float, inv, out var hy)) spec.HeadYawLimit = hy;
                    if (float.TryParse(Arg("-chestYawMax"), System.Globalization.NumberStyles.Float, inv, out var cy)) spec.ChestYawLimit = cy;
                    bodies.Add((seats[i], RealisticBody.Attach(p, AssetDatabase.LoadAssetAtPath<GameObject>(Arg("-baked") != null ? RealisticBake.PrefabPath(seats[i]) : $"Assets/_Project/Art/Characters/{seats[i]}/{seats[i]}.glb"), spec)));
                }
                bodies.Add(("Dealer", RealisticBody.Attach(list.Find(x => x.Kind == CharacterKind.Dealer), AssetDatabase.LoadAssetAtPath<GameObject>(DealerPath), RealisticSpec.Dealer)));
                string tag = string.Join("-", Array.ConvertAll(seats, n => n ?? "cartoon")) + (Arg("-baked") != null ? "_baked" : "") + (Arg("-headYawMax") != null ? $"_h{Arg("-headYawMax")}c{Arg("-chestYawMax")}" : "");
                void Step(float secs) { for (float t = 0; t < secs; t += Dt) { foreach (var p in list) p.Tick(Dt); foreach (var b in bodies) b.body.Sync(Dt); } }
                var shots = new List<string>();
                foreach (var look in (Arg("-looks") ?? "player,table").Split(','))
                {
                    foreach (var b in bodies) b.body.Pin = HankPin.Parse("neutral," + look);
                    Step(2f);
                    Aim(PlayerEye, new Vector3(0, 0.3f, 0.65f), 80f);
                    Save("warm");
                    shots.Add(Save($"{tag}_{look}_wide"));
                    foreach (var b in bodies)
                    {
                        CloseUp(b.body.HeadCenterWorld, 16f);
                        shots.Add(Save($"{tag}_{look}_{b.name}"));
                    }
                }
                Sheet($"{tag}_sheet", shots, bodies.Count + 1);
                Debug.Log("[RealisticHankScreenshots] DONE " + _dir);
            }
            catch (Exception e) { Debug.LogError("[RealisticHankScreenshots] FAILED " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        /// <summary>診斷：每個寫實角色 GLB 匯入後的貼圖（格式、尺寸、mip、記憶體）、網格切線、臉部材質的法線貼圖。</summary>
        public static void TextureReportBatch()
        {
            int code = 0;
            try
            {
                long total = 0;
                foreach (var n in new[] { "Hank", "Dealer", "Marcus", "Jace", "Reginald", "Erik" })
                {
                    var path = $"Assets/_Project/Art/Characters/{n}/{n}.glb";
                    long sum = 0;
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (o is Texture2D t)
                        {
                            long mem = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                            sum += mem;
                            Debug.Log($"[TexReport] {n} tex {t.name} {t.width}x{t.height} {t.format} mips={t.mipmapCount} mem={mem / 1048576f:F1}MB");
                        }
                        else if (o is Mesh m) Debug.Log($"[TexReport] {n} mesh {m.name} verts={m.vertexCount} tangents={m.tangents.Length}");
                        else if (o is Material mat)
                        {
                            string nm = mat.HasProperty("normalTexture") && mat.GetTexture("normalTexture") != null ? mat.GetTexture("normalTexture").name + " scale " + mat.GetFloat("normalTexture_scale").ToString("F2") : "none";
                            Debug.Log($"[TexReport] {n} material {mat.name} shader={mat.shader.name} normal={nm} keywords={string.Join(",", mat.shaderKeywords)}");
                        }
                    }
                    total += sum;
                    Debug.Log($"[TexReport] {n} texture memory {sum / 1048576f:F1}MB");
                }
                Debug.Log($"[TexReport] total {total / 1048576f:F1}MB (platform {EditorUserBuildSettings.activeBuildTarget})");
            }
            catch (Exception e) { Debug.LogError("[RealisticHankScreenshots] FAILED " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        /// <summary>
        /// 診斷：寫實角色在遊戲裡各種眼神下的實際骨頭姿勢，寫成 JSON 給換臉那邊（Blender）重現。-who 名字、-shotDir 輸出資料夾。
        /// 每根骨頭給：Unity 局部旋轉、換回 glTF 座標的局部旋轉（x,-y,-z,w）、相對 GLB 綁定姿勢的差（glTF 局部座標）。
        /// </summary>
        public static void PoseDumpBatch()
        {
            int code = 0;
            try
            {
                _dir = Arg("-shotDir") ?? Path.GetTempPath();
                Directory.CreateDirectory(_dir);
                CharacterMaterialAssets.Ensure();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SetupStage();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                var rest = new Dictionary<string, Quaternion>();
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) rest[t.name] = t.localRotation;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string Q(Quaternion q) => string.Format(inv, "[{0:F5},{1:F5},{2:F5},{3:F5}]", q.x, q.y, q.z, q.w);
                string E(Vector3 v) => string.Format(inv, "[{0:F2},{1:F2},{2:F2}]", v.x > 180 ? v.x - 360 : v.x, v.y > 180 ? v.y - 360 : v.y, v.z > 180 ? v.z - 360 : v.z);
                Quaternion G(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);
                var bones = new[] { "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head", "LeftEye", "RightEye", "LeftShoulder", "RightShoulder" };
                var sb = new System.Text.StringBuilder();
                sb.Append("{\n \"character\": \"").Append(WhoName).Append("\",\n \"glb_sha1_prefix\": \"see report\",\n");
                sb.Append(" \"notes\": \"Unity glTFast 6.20 import. quat order [x,y,z,w]. unity_local = Transform.localRotation after RealisticBody posing (sitting, spine lean 12 deg on Spine, chest/neck/head look). gltf_local = unity_local converted back to glTF axes (x,-y,-z,w). delta_from_bind_gltf = inverse(bind) * pose in glTF local axes; euler_deg is that delta as Unity-style ZXY euler (degrees, glTF axes). Neck gets half of the head look; Spine1 carries chest yaw; Spine2 is not driven by code. Eye angles are in the face frame: x=right(+)/left(-), y=down(+)/up(-).\",\n \"seats\": [\n");
                bool firstSeat = true;
                foreach (var seat in new[] { CharacterKind.Loudmouth, CharacterKind.Rookie })
                {
                    foreach (var o in UnityEngine.Object.FindObjectsByType<CharacterPerformer>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(o.gameObject);
                    var list = new List<CharacterPerformer>();
                    foreach (CharacterKind k in Enum.GetValues(typeof(CharacterKind))) list.Add(CharacterFactory.CreateAtSeat(k, _table));
                    foreach (var p in list) { p.AutoUpdate = false; p.SetViewer(_cam.transform); p.Play(new PerformanceCue()); }
                    var who = list.Find(p => p.Kind == seat);
                    var spec = RealisticPreview.SeatSpec(RealisticSpec.ByName(WhoName).Value, who.Kind);
                    var body = RealisticBody.Attach(who, model, spec);
                    if (!firstSeat) sb.Append(",\n");
                    firstSeat = false;
                    sb.Append("  {\"seat\": \"").Append(seat == CharacterKind.Loudmouth ? "center (Rocco seat, faces the player)" : "left side (Pip seat)").Append("\", \"looks\": [\n");
                    bool firstLook = true;
                    foreach (var look in new[] { "player", "left", "right", "table" })
                    {
                        body.Pin = HankPin.Parse("neutral," + look);
                        for (float t = 0; t < 2.5f; t += Dt) { foreach (var p in list) p.Tick(Dt); body.Sync(Dt); }
                        var f = who.transform.InverseTransformDirection(body.HeadForwardWorld);
                        if (!firstLook) sb.Append(",\n");
                        firstLook = false;
                        sb.Append(string.Format(inv, "   {{\"look\": \"{0}\", \"face_yaw_deg_vs_body\": {1:F1}, \"face_pitch_down_deg\": {2:F1}, \"chest_yaw_deg\": {3:F1}, \"eye_angles_deg\": [{4:F1},{5:F1}], \"bones\": {{",
                            look, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(f.y, -1, 1)) * Mathf.Rad2Deg, body.ChestYaw, body.EyeAngles.x, body.EyeAngles.y));
                        bool firstBone = true;
                        foreach (var b in bones)
                        {
                            var tr = body.Bone(b);
                            if (tr == null) continue;
                            var lr = tr.localRotation;
                            rest.TryGetValue(tr.name, out var r0);
                            var d = Quaternion.Inverse(G(r0)) * G(lr);
                            if (!firstBone) sb.Append(",");
                            firstBone = false;
                            sb.Append($"\n     \"{tr.name}\": {{\"unity_local\": {Q(lr)}, \"gltf_local\": {Q(G(lr))}, \"gltf_bind\": {Q(G(r0))}, \"delta_from_bind_gltf\": {Q(d)}, \"delta_euler_deg\": {E(d.eulerAngles)}}}");
                        }
                        sb.Append("}}");
                        Debug.Log($"[PoseDump] {WhoName} {seat} {look}: face yaw {Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg:F1}, chest {body.ChestYaw:F1}, eyes {body.EyeAngles.ToString("F1")}");
                    }
                    sb.Append("\n  ]}");
                }
                sb.Append("\n ]\n}\n");
                File.WriteAllText(Path.Combine(_dir, WhoName.ToLowerInvariant() + "_pose.json"), sb.ToString());
                // 眼睛材質（查「眼睛偏淡」）
                foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        var props = new List<string>();
                        for (int i = 0; i < m.shader.GetPropertyCount(); i++)
                        {
                            var n = m.shader.GetPropertyName(i);
                            var ty = m.shader.GetPropertyType(i);
                            if (ty == UnityEngine.Rendering.ShaderPropertyType.Float || ty == UnityEngine.Rendering.ShaderPropertyType.Range) props.Add($"{n}={m.GetFloat(n):F3}");
                            else if (ty == UnityEngine.Rendering.ShaderPropertyType.Color) props.Add($"{n}={m.GetColor(n)}");
                            else if (ty == UnityEngine.Rendering.ShaderPropertyType.Texture && m.GetTexture(n) != null) props.Add($"{n}={m.GetTexture(n).name}");
                        }
                        Debug.Log($"[PoseDump] material {r.name}/{m.name} shader={m.shader.name} {string.Join(" ", props)}");
                    }
                Debug.Log($"[PoseDump] reflectionIntensity(after MrLighting)={RenderSettings.reflectionIntensity} defaultReflectionMode={RenderSettings.defaultReflectionMode} probes={UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Length}");
            }
            catch (Exception e) { Debug.LogError("[RealisticHankScreenshots] FAILED " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        /// <summary>光線方向掃描：同主場景的燈（MrLighting），牌桌轉不同角度＝燈相對於臉轉不同角度（世界座標的燈不跟牌桌轉）。</summary>
        public static void LightSweepBatch()
        {
            int code = 0;
            try
            {
                _dir = Arg("-shotDir") ?? Path.Combine(Path.GetTempPath(), "realistic-hank");
                Directory.CreateDirectory(_dir);
                CharacterMaterialAssets.Ensure();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SetupStage();
                foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(l.gameObject);
                var key = new GameObject("Key").AddComponent<Light>();
                Poker.Game.MrLighting.ConfigureKey(key);
                var fill = new GameObject("Fill").AddComponent<Light>();
                Poker.Game.MrLighting.ConfigureFill(fill);
                Poker.Game.MrLighting.ApplyAmbient();
                var list = new List<CharacterPerformer>();
                foreach (CharacterKind k in Enum.GetValues(typeof(CharacterKind))) list.Add(CharacterFactory.CreateAtSeat(k, _table));
                foreach (var p in list) { p.AutoUpdate = false; p.SetViewer(_cam.transform); p.Play(new PerformanceCue()); }
                var hank = RealisticBody.Attach(list.Find(p => p.Kind == CharacterKind.Loudmouth), AssetDatabase.LoadAssetAtPath<GameObject>(HankPath));
                var dealer = RealisticBody.Attach(list.Find(p => p.Kind == CharacterKind.Dealer), AssetDatabase.LoadAssetAtPath<GameObject>(DealerPath));
                var pin = HankPin.Parse("neutral,player");
                hank.Pin = pin; dealer.Pin = pin;
                void Step(float secs) { for (float t = 0; t < secs; t += Dt) { foreach (var p in list) p.Tick(Dt); hank.Sync(Dt); dealer.Sync(Dt); } }
                Step(1f);
                CloseUp(hank.HeadCenterWorld, 16f);
                Save("warmup");
                var hs = new List<string>();
                var ds = new List<string>();
                foreach (var yaw in new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f })
                {
                    _table.rotation = Quaternion.Euler(0, yaw, 0);
                    Step(0.5f);
                    CloseUp(hank.HeadCenterWorld, 16f);
                    hs.Add(Save($"light_hank_yaw{yaw:F0}"));
                    CloseUp(dealer.HeadCenterWorld, 16f);
                    ds.Add(Save($"light_dealer_yaw{yaw:F0}"));
                }
                Sheet("light_hank_sheet", hs, 4);
                Sheet("light_dealer_sheet", ds, 4);

                // 荷官嘴角到下巴的線：藏身體／藏臉，看線屬於哪一塊（側光 yaw 135）
                var line = new List<string>();
                foreach (var yaw in new[] { 0f, 135f, 270f })
                {
                    _table.rotation = Quaternion.Euler(0, yaw, 0);
                    Step(0.5f);
                    CloseUp(dealer.HeadCenterWorld, 10f);
                    line.Add(Save($"line_{yaw:F0}_normal"));
                    dealer.Skin.forceRenderingOff = true;
                    line.Add(Save($"line_{yaw:F0}_body_hidden"));
                    dealer.Skin.forceRenderingOff = false;
                    dealer.FaceSkin.forceRenderingOff = true;
                    line.Add(Save($"line_{yaw:F0}_face_hidden"));
                    dealer.FaceSkin.forceRenderingOff = false;
                }
                Sheet("line_sheet", line, 3);

                // 燈跟著牌桌後（等於 yaw 0）：看玩家、看鄰座、看桌面時臉上的光
                _table.rotation = Quaternion.identity;
                var yaw0 = new List<string>();
                foreach (var look in new[] { "player", "left", "right", "table" })
                {
                    hank.Pin = dealer.Pin = HankPin.Parse("neutral," + look);
                    Step(2f);
                    CloseUp(hank.HeadCenterWorld, 16f);
                    yaw0.Add(Save($"yaw0_hank_{look}"));
                    CloseUp(dealer.HeadCenterWorld, 16f);
                    yaw0.Add(Save($"yaw0_dealer_{look}"));
                }
                Sheet("yaw0_look_sheet", yaw0, 4);

                // 卡通角色：環境反射開／關的差別（同樣的燈、同樣的角度）
                _table.rotation = Quaternion.identity;
                hank.Pin = dealer.Pin = HankPin.Parse("neutral,player");
                Step(1f);
                foreach (var reflOn in new[] { 1f, 0f })
                {
                    RenderSettings.reflectionIntensity = reflOn;
                    foreach (var kind in new[] { CharacterKind.Rookie, CharacterKind.Cold })
                    {
                        var who = list.Find(p => p.Kind == kind);
                        _cam.transform.position = _table.TransformPoint(PlayerEye);
                        _cam.transform.LookAt(who.Rig.Head.position + Vector3.down * 0.12f);
                        _cam.fieldOfView = 24f;
                        Save($"cartoon_{kind}_refl{reflOn:F0}");
                    }
                }
                RenderSettings.reflectionIntensity = 0f;

                // 嘴巴內部偏藍：一次關一個光源，看藍色從哪來
                _table.rotation = Quaternion.identity;
                var talk = HankPin.Parse("talk,neutral");
                hank.Pin = talk; dealer.Pin = talk;
                Step(0.6f);
                var mouth = new List<string>();
                void MouthShot(string name)
                {
                    Step(0.1f);
                    _cam.transform.position = _table.TransformPoint(PlayerEye);
                    _cam.transform.LookAt(dealer.HeadCenterWorld + Vector3.down * 0.08f);
                    _cam.fieldOfView = 5f;
                    mouth.Add(Save("mouth_" + name));
                }
                MouthShot("all");
                fill.enabled = false; MouthShot("no_fill"); fill.enabled = true;
                key.enabled = false; MouthShot("no_key"); key.enabled = true;
                var amb = RenderSettings.ambientProbe;
                RenderSettings.ambientProbe = new SphericalHarmonicsL2(); RenderSettings.ambientLight = Color.black; MouthShot("no_ambient");
                RenderSettings.ambientProbe = amb; RenderSettings.ambientLight = Poker.Game.MrLighting.Ambient;
                float refl = RenderSettings.reflectionIntensity;
                RenderSettings.reflectionIntensity = 0f; MouthShot("no_reflection"); RenderSettings.reflectionIntensity = refl;
                key.enabled = false; fill.enabled = false; RenderSettings.ambientProbe = new SphericalHarmonicsL2(); RenderSettings.ambientLight = Color.black; MouthShot("only_reflection");
                Sheet("mouth_sheet", mouth, 3);
                var fm = dealer.FaceSkin.sharedMaterials;
                foreach (var r in new[] { hank.FaceSkin, dealer.FaceSkin })
                    foreach (var m in r.sharedMaterials)
                        Debug.Log($"[Diag] face material {m.name} shader={m.shader.name} roughness={(m.HasProperty("roughnessFactor") ? m.GetFloat("roughnessFactor").ToString("F3") : "n/a")} metallic={(m.HasProperty("metallicFactor") ? m.GetFloat("metallicFactor").ToString("F3") : "n/a")}");
                Debug.Log("[RealisticHankScreenshots] DONE " + _dir);
            }
            catch (Exception e) { Debug.LogError("[RealisticHankScreenshots] FAILED " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        /// <summary>診斷：blendshape 有沒有畫出來（正常／藏身體／BakeMesh 換成靜態網格）。</summary>
        public static void DiagBatch()
        {
            int code = 0;
            try
            {
                _dir = Arg("-shotDir") ?? Path.Combine(Path.GetTempPath(), "realistic-hank");
                Directory.CreateDirectory(_dir);
                CharacterMaterialAssets.Ensure();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(HankPath);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SetupStage();
                var rocco = CharacterFactory.CreateAtSeat(CharacterKind.Loudmouth, _table);
                rocco.AutoUpdate = false; rocco.SetViewer(_cam.transform);
                var body = RealisticBody.Attach(rocco, model);
                var list = new List<CharacterPerformer> { rocco };
                body.Pin = HankPin.Parse("blink,talk,neutral");
                Simulate(list, body, 1f);
                var fs = body.FaceSkin;
                Debug.Log($"[Diag] face={fs.name} body={body.Skin.name} blink={body.Weight("eyeBlinkLeft")} raw={fs.GetBlendShapeWeight(fs.sharedMesh.GetBlendShapeIndex("eyeBlinkLeft"))} frameW={fs.sharedMesh.GetBlendShapeFrameWeight(fs.sharedMesh.GetBlendShapeIndex("eyeBlinkLeft"), 0)} frames={fs.sharedMesh.GetBlendShapeFrameCount(fs.sharedMesh.GetBlendShapeIndex("eyeBlinkLeft"))} gpuSkin={SystemInfo.supportsComputeShaders} skinWeights={fs.quality} updateOff={fs.updateWhenOffscreen}");
                CloseUp(body.HeadCenterWorld, 14f);
                Save("diag_a_normal");
                body.Skin.forceRenderingOff = true;
                Save("diag_b_body_hidden");
                body.Skin.forceRenderingOff = false;
                var baked = new Mesh();
                fs.BakeMesh(baked, true);
                var go = new GameObject("baked");
                go.transform.SetPositionAndRotation(fs.transform.position, fs.transform.rotation);
                go.AddComponent<MeshFilter>().sharedMesh = baked;
                go.AddComponent<MeshRenderer>().sharedMaterials = fs.sharedMaterials;
                fs.forceRenderingOff = true;
                Save("diag_c_baked");
            }
            catch (Exception e) { Debug.LogError("[Diag] FAILED " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static void CaptureAll()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new FileNotFoundException(ModelPath);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupStage();

            // 同桌：Pip、Hank（Rocco 的座位）、Vex、荷官 Dot
            var list = new List<CharacterPerformer>();
            foreach (CharacterKind k in Enum.GetValues(typeof(CharacterKind))) list.Add(CharacterFactory.CreateAtSeat(k, _table));
            foreach (var p in list) { p.AutoUpdate = false; p.SetViewer(_cam.transform); }
            var rocco = list.Find(p => p.Kind == WhoKind);
            var body = AttachWho(rocco, model);
            Debug.Log($"[RealisticHankScreenshots] hat top {body.HeadTopAboveRoot:F3} m, standing {body.StandingHeight:F3} m, " +
                      $"hips {rocco.transform.InverseTransformPoint(body.Bone("Hips").position).ToString("F3")}, " +
                      $"shoulderL {rocco.transform.InverseTransformPoint(body.Bone("LeftArm").position).ToString("F3")}, elbowL {rocco.transform.InverseTransformPoint(body.Bone("LeftForeArm").position).ToString("F3")}, " +
                      $"handL {rocco.transform.InverseTransformPoint(body.Bone("LeftHand").position).ToString("F3")}");

            foreach (var p in list) p.Play(new PerformanceCue());
            Simulate(list, body, 2.5f);
            EyeShapeDiag(body);
            Aim(PlayerEye, new Vector3(0, 0.15f, 0.6f), 95f);
            Save("01_table_player_view_neutral");
            Aim(PlayerEye, new Vector3(-0.05f, 0.4f, 0.9f), 62f);
            Save("02_table_player_view_narrow");

            // 講話：真的預錄聲音（rocco.nervous.02），嘴巴跟著音量；拍下巴開最大的那一刻
            rocco.Play(new PerformanceCue { Subtitle = TalkLine.text, LineId = TalkLine.id });
            var u = rocco.Speech.Current;
            Debug.Log($"[RealisticHankScreenshots] talk clip={u?.ClipId} dur={u?.Duration:F2} hasClip={u?.Clip != null}");
            float best = 0, bestT = 0, tt = 0;
            var jaw = new System.Text.StringBuilder();
            while (rocco.Speech.Current == u && u != null && tt < 6f)
            {
                foreach (var p in list) p.Tick(Dt);
                body.Sync(Dt);
                tt += Dt;
                float j = body.Weight("jawOpen");
                if (Mathf.RoundToInt(tt / Dt) % 3 == 0) jaw.Append(j.ToString("F0")).Append(' ');
                if (j > best) { best = j; bestT = tt; }
            }
            Debug.Log($"[RealisticHankScreenshots] talk jawOpen every 50 ms: {jaw}");
            Debug.Log($"[RealisticHankScreenshots] talk peak jawOpen={best:F0} at {bestT:F2}s");
            // 重講一次，停在最大開口
            Simulate(list, body, 0.8f);
            rocco.Play(new PerformanceCue { Subtitle = TalkLine.text, LineId = TalkLine.id });
            for (float t = 0; t < bestT - Dt * 0.5f; t += Dt) { foreach (var p in list) p.Tick(Dt); body.Sync(Dt); }
            Debug.Log($"[RealisticHankScreenshots] talk shot jawOpen={body.Weight("jawOpen"):F0}");
            CloseUp(body.HeadCenterWorld, 22f);
            var talkShot = Save("12_hank_talk_voice_open");
            Aim(PlayerEye, new Vector3(-0.05f, 0.4f, 0.9f), 62f);
            Save("03_table_player_view_talking_subtitle");
            Simulate(list, body, 3f);

            // 固定表情／眼神（同實機的 debug.tell.hankface）
            var pins = new (string pin, string name)[]
            {
                ("neutral", "10_hank_neutral"), ("blink,neutral", "11_hank_blink_closed"), ("talk,neutral", "13_hank_talk_pinned_open"),
                ("neutral,player", "14_hank_look_player"), ("neutral,left", "15_hank_look_left_pip"), ("neutral,right", "16_hank_look_right_vex"),
                ("neutral,table", "17_hank_look_table"),
            };
            var keyShots = new List<string>();
            foreach (var (pin, name) in pins)
            {
                body.Pin = HankPin.Parse(pin);
                Simulate(list, body, 1.2f);
                CloseUp(body.HeadCenterWorld, 22f);
                keyShots.Add(Save(name));
                Debug.Log($"[RealisticHankScreenshots] {name}: eyes={body.EyeAngles.ToString("F1")} " + Weights(body));
            }
            keyShots.Insert(3, talkShot);
            Sheet("18_hank_eyes_mouth_sheet", keyShots, 4);

            // 看左邊的角色：拉遠一點，看得到他在看誰
            body.Pin = HankPin.Parse("neutral,left");
            Simulate(list, body, 1.2f);
            Aim(PlayerEye, new Vector3(-0.25f, 0.35f, 0.8f), 62f);
            Save("19_table_hank_looks_at_pip");

            // 表情特寫（玩家位置、長焦）
            var moods = new[]
            {
                (Mood.Neutral, "neutral"), (Mood.Happy, "happy"), (Mood.Confident, "confident_smirk"), (Mood.Annoyed, "annoyed"),
                (Mood.Nervous, "nervous"), (Mood.Bluffing, "bluffing"), (Mood.Startled, "startled"), (Mood.Dejected, "dejected"), (Mood.Hesitant, "hesitant"),
            };
            var shots = new List<string>();
            foreach (var (mood, name) in moods)
            {
                body.Pin = HankPin.Parse(mood.ToString().ToLowerInvariant() + ",player");
                Simulate(list, body, 1.2f);
                CloseUp(body.HeadCenterWorld, 22f);
                shots.Add(Save("20_hank_" + name));
                Debug.Log($"[RealisticHankScreenshots] {name}: " + Weights(body));
            }
            Sheet("21_hank_expressions_sheet", shots, 3);

            // 大約等於 Quest 3 的解析度（約 25 像素／度：1000 px 對 40°）：1 m 外看不看得出來
            var q3 = new List<string>();
            foreach (var (pin, name) in new[] { ("neutral", "neutral"), ("blink,neutral", "blink"), ("talk,neutral", "talk"), ("happy,player", "happy"),
                         ("annoyed,player", "annoyed"), ("neutral,table", "table") })
            {
                body.Pin = HankPin.Parse(pin);
                Simulate(list, body, 1.2f);
                Aim(PlayerEye, rocco.transform.localPosition + new Vector3(0, 0.55f, 0), 40f);
                q3.Add(Save("30_q3res_" + name));
            }
            Sheet("31_q3res_sheet", q3, 3);

            // 不固定：照遊戲的演出跑（被玩家盯著、自動眨眼）
            body.Pin = default;
            rocco.Play(new PerformanceCue { Mood = Mood.Confident, Intensity = 0.9f, Duration = 30f });
            Simulate(list, body, 2.2f);
            CloseUp(body.HeadCenterWorld, 22f);
            Save("22_hank_performer_confident_unpinned");

            // 側面（看坐姿）
            rocco.Play(new PerformanceCue());
            Simulate(list, body, 2f);
            foreach (var p in list) if (p != rocco) p.gameObject.SetActive(false);
            Aim(new Vector3(-1.7f, 0.25f, 0.75f), rocco.transform.localPosition + new Vector3(0, 0.05f, 0.05f), 55f);
            Save("40_hank_side_posture");
            foreach (var p in list) p.gameObject.SetActive(true);
        }

        /// <summary>眼球是骨頭轉的：確認 eyeLook*／eyeBlink 形狀鍵有沒有動到眼球那個子網格（有的話和骨頭轉會疊兩次）。</summary>
        static void EyeShapeDiag(RealisticBody body)
        {
            var m = body.FaceSkin.sharedMesh;
            var eyeVerts = new HashSet<int>(m.GetTriangles(m.subMeshCount - 1));
            var d = new Vector3[m.vertexCount];
            var sb = new System.Text.StringBuilder("[RealisticHankScreenshots] eyeball submesh max move (model units): ");
            foreach (var n in new[] { "eyeLookOutLeft", "eyeLookInLeft", "eyeLookDownLeft", "eyeLookUpLeft", "eyeBlinkLeft", "eyeWideLeft", "eyeSquintLeft", "jawOpen" })
            {
                int i = m.GetBlendShapeIndex(n);
                m.GetBlendShapeFrameVertices(i, m.GetBlendShapeFrameCount(i) - 1, d, null, null);
                float eye = 0, all = 0;
                for (int v = 0; v < d.Length; v++)
                {
                    float mag = d[v].magnitude;
                    all = Mathf.Max(all, mag);
                    if (eyeVerts.Contains(v)) eye = Mathf.Max(eye, mag);
                }
                sb.Append($"{n} eye={eye:F4} any={all:F4}; ");
            }
            Debug.Log(sb.ToString());
        }

        static string Weights(RealisticBody body)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var n in HankFace.Shapes)
            {
                float w = body.Weight(n);
                if (w > 0.5f) sb.Append(n).Append('=').Append(w.ToString("F0")).Append(' ');
            }
            return sb.ToString();
        }

        static void SetupStage()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.62f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.48f, 0.47f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.27f, 0.25f);
            RenderSettings.fog = false;
            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional; key.color = new Color(1f, 0.96f, 0.9f); key.intensity = 1.15f; key.shadows = LightShadows.None;
            key.transform.rotation = Quaternion.Euler(40, 20, 0);
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.color = new Color(0.75f, 0.82f, 1f); fill.intensity = 0.35f; fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(15, -45, 0);

            _table = new GameObject("TableRoot").transform;
            var felt = GameObject.CreatePrimitive(PrimitiveType.Cube);
            felt.name = "Felt";
            felt.transform.SetParent(_table, false);
            // 真實尺寸的桌子（TableSeats.TableSize 的絨布＋一圈 TableSeats.RailWidth；截圖用方塊近似，遊戲裡是長圓形）
            var size = TableSeats.TableSize;
            felt.transform.localPosition = new Vector3(0, -0.02f, size.y * 0.5f);
            felt.transform.localScale = new Vector3(size.x, 0.04f, size.y);
            felt.GetComponent<Renderer>().sharedMaterial = MaterialCache.Lit(new Color(0.13f, 0.42f, 0.28f), 0.1f);
            var rim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rim.name = "Rim";
            rim.transform.SetParent(_table, false);
            rim.transform.localPosition = new Vector3(0, -0.035f, size.y * 0.5f);
            rim.transform.localScale = new Vector3(size.x + 2f * TableSeats.RailWidth, 0.045f, size.y + 2f * TableSeats.RailWidth);
            rim.GetComponent<Renderer>().sharedMaterial = MaterialCache.Lit(new Color(0.36f, 0.2f, 0.12f), 0.4f);

            _cam = new GameObject("PlayerEye").AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.2f, 0.18f, 0.22f);
            _cam.nearClipPlane = 0.02f;
            _cam.farClipPlane = 20f;
            _cam.transform.position = _table.TransformPoint(PlayerEye);
        }

        static void Simulate(List<CharacterPerformer> list, RealisticBody body, float secs)
        {
            for (float t = 0; t < secs; t += Dt)
            {
                foreach (var p in list) p.Tick(Dt);
                body.Sync(Dt);
            }
            for (int i = 0; i < 30 && list.Exists(p => p.IsBlinking); i++)
            {
                foreach (var p in list) p.Tick(Dt);
                body.Sync(Dt);
            }
        }

        static void Aim(Vector3 from, Vector3 at, float fov)
        {
            _cam.transform.position = _table.TransformPoint(from);
            _cam.transform.LookAt(_table.TransformPoint(at));
            _cam.fieldOfView = fov;
        }

        static void CloseUp(Vector3 head, float fov)
        {
            _cam.transform.position = _table.TransformPoint(PlayerEye);
            _cam.transform.LookAt(head + Vector3.down * 0.03f);
            _cam.fieldOfView = fov;
        }

        static string Save(string name)
        {
            // batchmode 的 Editor 沒有逐幀更新：blendshape 只在第一次畫時上傳，之後的權重不會反映（骨頭會）。關掉再打開強迫重新上傳
            foreach (var r in UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
                if (r.enabled) { r.enabled = false; r.enabled = true; }
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(_cam, req)) RenderPipeline.SubmitRenderRequest(_cam, req);
            else { _cam.targetTexture = rt; _cam.Render(); _cam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var path = Path.Combine(_dir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            Debug.Log("[RealisticHankScreenshots] " + path);
            return path;
        }

        static void Sheet(string name, List<string> files, int cols)
        {
            const int cell = Size / 2;
            int rows = (files.Count + cols - 1) / cols;
            var sheet = new Texture2D(cell * cols, cell * rows, TextureFormat.RGB24, false);
            for (int i = 0; i < files.Count; i++)
            {
                var src = new Texture2D(2, 2);
                src.LoadImage(File.ReadAllBytes(files[i]));
                int cx = i % cols, cy = rows - 1 - i / cols;
                for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                    sheet.SetPixel(cx * cell + x, cy * cell + y, src.GetPixelBilinear((x + 0.5f) / cell, (y + 0.5f) / cell));
                UnityEngine.Object.DestroyImmediate(src);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(_dir, name + ".png"), sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }
    }
}
