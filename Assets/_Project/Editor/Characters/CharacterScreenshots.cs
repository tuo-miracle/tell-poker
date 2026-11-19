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
    /// 從玩家座位視角渲染角色截圖（batchmode，不要加 -nographics）。
    /// 用法：Unity -batchmode -projectPath … -executeMethod Poker.Editor.Characters.CharacterScreenshots.CaptureBatch -shotDir &lt;輸出資料夾&gt;
    /// </summary>
    public static class CharacterScreenshots
    {
        const float Dt = 1f / 60f;
        // 玩家眼睛（牌桌座標）：坐在桌緣後約 0.25 m、高於桌面約 0.45 m
        static readonly Vector3 PlayerEye = new Vector3(0, 0.45f, -0.25f);

        static string _dir;
        static Camera _cam;
        static Transform _table;

        public static void CaptureBatch()
        {
            int code = 0;
            try
            {
                _dir = Arg("-shotDir") ?? Path.Combine(Path.GetTempPath(), "characters");
                Directory.CreateDirectory(_dir);
                CharacterMaterialAssets.Ensure();
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var ft = font.material.mainTexture;
                Debug.Log($"[CharacterScreenshots] font atlas {ft?.GetType().Name} {(ft as Texture2D)?.format} {ft?.graphicsFormat}");
                CaptureAll();
                Debug.Log("[CharacterScreenshots] DONE " + _dir);
            }
            catch (Exception e)
            {
                Debug.LogError("[CharacterScreenshots] FAILED " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        [MenuItem("Tools/Poker/Character Screenshots")]
        public static void CaptureFromMenu()
        {
            _dir = Path.Combine(Path.GetTempPath(), "characters");
            Directory.CreateDirectory(_dir);
            CaptureAll();
            EditorUtility.RevealInFinder(_dir);
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static void CaptureAll()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupStage();
            var kinds = (CharacterKind[])Enum.GetValues(typeof(CharacterKind));

            // 1. 四個角色在預設座位（玩家視角）
            {
                var list = new List<CharacterPerformer>();
                foreach (var k in kinds) list.Add(CharacterFactory.CreateAtSeat(k, _table));
                Prepare(list);
                foreach (var p in list) p.Play(new PerformanceCue());
                Simulate(list, 2.5f);
                Aim(PlayerEye, new Vector3(0, 0.15f, 0.6f), 95f);
                Save("01_table_idle");
                Clear(list);
            }

            // 2. 四個角色並排待機
            {
                var list = new List<CharacterPerformer>();
                for (int i = 0; i < kinds.Length; i++)
                    list.Add(CharacterFactory.Create(kinds[i], _table, new Vector3(-0.66f + i * 0.44f, 0, 1.0f), Quaternion.Euler(0, 180, 0)));
                Prepare(list);
                foreach (var p in list) p.Play(new PerformanceCue());
                Simulate(list, 2.5f);
                Aim(PlayerEye, new Vector3(0, 0.3f, 1.0f), 62f);
                Save("02_lineup_idle");
                Clear(list);
            }

            // 3. 每個角色的四種情緒特寫
            var moods = new[]
            {
                (Mood.Nervous, Tell.Sweat, "nervous"),
                (Mood.Confident, Tell.LeanBack, "confident"),
                (Mood.Bluffing, Tell.None, "bluffing"),
                (Mood.Startled, Tell.None, "startled"),
            };
            foreach (var k in kinds)
            {
                var shots = new List<string>();
                foreach (var (mood, tell, name) in moods)
                {
                    var p = Single(k);
                    p.Play(new PerformanceCue());
                    Simulate(p, 1.0f);
                    p.Play(new PerformanceCue { Mood = mood, Tell = tell, Intensity = 0.85f });
                    Simulate(p, mood == Mood.Startled ? 0.22f : 2.2f);
                    CloseUp(p);
                    shots.Add(Save($"10_{k}_{name}"));
                    Clear(p);
                }
                Sheet($"11_{k}_moods_sheet", shots, 2);
            }

            // 4. 被注視 vs 沒被注視（同一條指令）
            {
                var shots = new List<string>();
                foreach (var k in kinds)
                {
                    var cue = new PerformanceCue { Mood = Mood.Nervous, Tell = k == CharacterKind.Cold ? Tell.StareDown : Tell.AvoidEyeContact, Intensity = 0.6f };
                    foreach (var gazed in new[] { false, true })
                    {
                        var p = Single(k);
                        p.Play(cue);
                        p.SetGazedAt(gazed);
                        Simulate(p, 1.8f);
                        CloseUp(p);
                        shots.Add(Save($"20_{k}_gaze_{(gazed ? "on" : "off")}"));
                        Clear(p);
                    }
                }
                Sheet("21_gaze_compare_sheet", shots, 2);
            }

            // 5. 其他情緒與破綻（新手與老手），加上字幕
            {
                var shots = new List<string>();
                foreach (var mood in new[] { Mood.Neutral, Mood.Hesitant, Mood.Annoyed, Mood.Happy, Mood.Dejected })
                {
                    var p = Single(CharacterKind.Rookie);
                    p.Play(new PerformanceCue { Mood = mood, Intensity = 0.8f });
                    Simulate(p, 2f);
                    CloseUp(p);
                    shots.Add(Save($"30_Rookie_{mood}"));
                    Clear(p);
                }
                {
                    var p = Single(CharacterKind.Loudmouth);
                    p.Play(new PerformanceCue { Mood = Mood.Happy, Intensity = 0.8f, LineId = "brag", Subtitle = "Read 'em and weep, kid!", Duration = 4f });
                    Simulate(p, 0.9f);
                    CloseUp(p, wide: true);
                    shots.Add(Save("31_Loudmouth_subtitle"));
                    Clear(p);
                }
                Sheet("32_more_moods_sheet", shots, 3);
            }
            {
                var shots = new List<string>();
                foreach (var tell in new[] { Tell.FidgetChips, Tell.GlanceAtCards, Tell.TapFingers, Tell.Swallow, Tell.LeanIn, Tell.LeanBack })
                {
                    var p = Single(tell == Tell.LeanBack ? CharacterKind.Loudmouth : CharacterKind.Rookie);
                    p.Play(new PerformanceCue { Mood = Mood.Nervous, Tell = tell, Intensity = 0.9f });
                    Simulate(p, tell == Tell.Swallow ? 0.75f : 1.05f);
                    CloseUp(p, wide: true);
                    shots.Add(Save($"40_tell_{tell}"));
                    Clear(p);
                }
                Sheet("41_tells_sheet", shots, 3);
            }
        }

        // ───────────── 場景 ─────────────

        static void SetupStage()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.62f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.48f, 0.47f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.27f, 0.25f);
            RenderSettings.fog = false;

            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.96f, 0.9f);
            key.intensity = 1.15f;
            key.shadows = LightShadows.None;
            key.transform.rotation = Quaternion.Euler(40, 20, 0);
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.75f, 0.82f, 1f);
            fill.intensity = 0.35f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(15, -45, 0);

            _table = new GameObject("TableRoot").transform;
            var felt = GameObject.CreatePrimitive(PrimitiveType.Cube);
            felt.name = "Felt";
            felt.transform.SetParent(_table, false);
            felt.transform.localPosition = new Vector3(0, -0.02f, 0.55f);
            felt.transform.localScale = new Vector3(1.3f, 0.04f, 1.1f);
            felt.GetComponent<Renderer>().sharedMaterial = MaterialCache.Lit(new Color(0.13f, 0.42f, 0.28f), 0.1f);
            var rim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rim.name = "Rim";
            rim.transform.SetParent(_table, false);
            rim.transform.localPosition = new Vector3(0, -0.035f, 0.55f);
            rim.transform.localScale = new Vector3(1.38f, 0.045f, 1.18f);
            rim.GetComponent<Renderer>().sharedMaterial = MaterialCache.Lit(new Color(0.36f, 0.2f, 0.12f), 0.4f);

            _cam = new GameObject("PlayerEye").AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.2f, 0.18f, 0.22f);
            _cam.nearClipPlane = 0.02f;
            _cam.farClipPlane = 20f;
        }

        static CharacterPerformer Single(CharacterKind k)
        {
            // 坐在牌桌對面，面向玩家
            var p = CharacterFactory.Create(k, _table, new Vector3(0, 0, 1.0f), Quaternion.Euler(0, 180, 0));
            Prepare(new List<CharacterPerformer> { p });
            return p;
        }

        static void Prepare(List<CharacterPerformer> list)
        {
            foreach (var p in list)
            {
                p.AutoUpdate = false;
                p.SetViewer(_cam.transform);
            }
            _cam.transform.position = _table.TransformPoint(PlayerEye);
        }

        static void Simulate(CharacterPerformer p, float secs) => Simulate(new List<CharacterPerformer> { p }, secs);

        static void Simulate(List<CharacterPerformer> list, float secs)
        {
            for (float t = 0; t < secs; t += Dt)
                foreach (var p in list) p.Tick(Dt);
            // 不要停在眨眼中間
            for (int i = 0; i < 30 && list.Exists(p => p.IsBlinking); i++)
                foreach (var p in list) p.Tick(Dt);
        }

        static void Aim(Vector3 from, Vector3 at, float fov)
        {
            _cam.transform.position = _table.TransformPoint(from);
            _cam.transform.LookAt(_table.TransformPoint(at));
            _cam.fieldOfView = fov;
        }

        static void CloseUp(CharacterPerformer p, bool wide = false)
        {
            var head = p.Rig.Head.position;
            _cam.transform.position = _table.TransformPoint(PlayerEye);
            _cam.transform.LookAt(head + Vector3.down * (wide ? 0.1f : 0.05f) + Vector3.up * (wide ? 0.06f : 0));
            _cam.fieldOfView = wide ? 40f : 30f;
        }

        static void Clear(CharacterPerformer p) => UnityEngine.Object.DestroyImmediate(p.gameObject);

        static void Clear(List<CharacterPerformer> list)
        {
            foreach (var p in list) Clear(p);
        }

        // ───────────── 輸出 ─────────────

        const int Size = 900;

        static string Save(string name)
        {
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
            Debug.Log("[CharacterScreenshots] " + path);
            return path;
        }

        /// <summary>把多張截圖拼成一張（方便一次比較）。</summary>
        static void Sheet(string name, List<string> files, int cols)
        {
            const int cell = Size / 2;
            int rows = (files.Count + cols - 1) / cols;
            var sheet = new Texture2D(cell * cols, cell * rows, TextureFormat.RGB24, false);
            var bg = new Color32(20, 18, 24, 255);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = bg;
            sheet.SetPixels32(fill);
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

    /// <summary>產生 Resources 底下的基底材質，讓 Quest 建置保留 URP Lit／Unlit（含 alpha clip）。</summary>
    public static class CharacterMaterialAssets
    {
        const string Folder = "Assets/_Project/Scripts/Characters/Resources/" + MaterialCache.ResourceFolder;

        [MenuItem("Tools/Poker/Create Character Base Materials")]
        public static void Ensure()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Scripts/Characters/Resources"))
                AssetDatabase.CreateFolder("Assets/_Project/Scripts/Characters", "Resources");
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Project/Scripts/Characters/Resources", MaterialCache.ResourceFolder);
            foreach (var name in new[] { MaterialCache.LitAsset, MaterialCache.UnlitAsset, MaterialCache.TextAsset })
            {
                var path = $"{Folder}/{name}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) continue;
                AssetDatabase.CreateAsset(MaterialCache.CreateBase(name), path);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
