// Rebuild and capture the snow plague field without clicking or menus: write one line to
// ArtSource/SnowPlague/sp_cmd.txt -> "build", "shots <outDir>", "build+shots <outDir>", "open", "importpkg <file>".
// Results go to ArtSource/SnowPlague/sp_log.txt. The previously open scene is restored afterwards (except "open").
using System.Collections.Generic;
using System.IO;
using SnowPlague;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class SPRemote
{
    static readonly string Dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "SnowPlague");
    static double next;

    static SPRemote() { EditorApplication.update += Tick; }

    static void Log(string s) { Directory.CreateDirectory(Dir); File.AppendAllText(Path.Combine(Dir, "sp_log.txt"), $"[{System.DateTime.Now:HH:mm:ss}] {s}\n"); }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1.0;
        var cmdPath = Path.Combine(Dir, "sp_cmd.txt");
        if (!File.Exists(cmdPath) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var cmd = File.ReadAllText(cmdPath).Trim();
        File.Delete(cmdPath);
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty && active.path != SPBuilder.ScenePath) { Log("ABORT unsaved changes in " + active.path); return; }
        var prev = active.path;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            var parts = cmd.Split(new[] { ' ' }, 2);
            if (parts[0].Contains("build"))
            {
                // .hlsl edits are not picked up by a script recompile while the editor is in the background
                AssetDatabase.ImportAsset(SPBuilder.Root + "/Shaders", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
                SPBuilder.Build(); Log("BUILD OK");
            }
            if (parts[0].Contains("shots")) Shots(parts.Length > 1 ? parts[1] : Path.Combine(Dir, "shots"));
            if (parts[0] == "importpkg" && parts.Length > 1) ImportPkg(parts[1].Trim().Trim('"'));
            if (parts[0] == "probe") Probe();
            if (parts[0] == "casters") Casters();
            if (parts[0] == "open")
            {
                if (EditorSceneManager.GetActiveScene().path != SPBuilder.ScenePath) EditorSceneManager.OpenScene(SPBuilder.ScenePath, OpenSceneMode.Single);
                prev = SPBuilder.ScenePath;
                Log("OPEN OK");
            }
        }
        catch (System.Exception e) { Log("EXCEPTION " + e); }
        finally
        {
            ShaderUtil.allowAsyncCompilation = true;
            if (!string.IsNullOrEmpty(prev) && prev != SPBuilder.ScenePath && EditorSceneManager.GetActiveScene().path != prev) EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
        }
    }

    // "importpkg <path.unitypackage>": silent import (no dialog); logs IMPORTPKG OK/FAIL and the new top-level Assets folders.
    static HashSet<string> pkgBefore;
    static void ImportPkg(string path)
    {
        if (!File.Exists(path)) { Log("IMPORTPKG FAIL no file " + path); return; }
        pkgBefore = new HashSet<string>(Directory.GetDirectories(Application.dataPath, "*", SearchOption.TopDirectoryOnly));
        AssetDatabase.importPackageCompleted -= PkgDone; AssetDatabase.importPackageFailed -= PkgFail;
        AssetDatabase.importPackageCompleted += PkgDone; AssetDatabase.importPackageFailed += PkgFail;
        Log("IMPORTPKG start " + path);
        AssetDatabase.ImportPackage(path, false);
    }
    static void PkgDone(string name)
    {
        AssetDatabase.importPackageCompleted -= PkgDone; AssetDatabase.importPackageFailed -= PkgFail;
        foreach (var d in Directory.GetDirectories(Application.dataPath, "*", SearchOption.TopDirectoryOnly))
            if (pkgBefore == null || !pkgBefore.Contains(d)) Log("IMPORTPKG new folder Assets/" + Path.GetFileName(d));
        Log("IMPORTPKG OK " + name);
    }
    static void PkgFail(string name, string err)
    {
        AssetDatabase.importPackageCompleted -= PkgDone; AssetDatabase.importPackageFailed -= PkgFail;
        Log($"IMPORTPKG FAIL {name}: {err}");
    }

    // Renderer counts, bounds and shaders per top-level group (why something does not show).
    static void Probe()
    {
        if (EditorSceneManager.GetActiveScene().path != SPBuilder.ScenePath) EditorSceneManager.OpenScene(SPBuilder.ScenePath, OpenSceneMode.Single);
        var root = GameObject.Find("SnowPlague"); if (!root) { Log("PROBE no root"); return; }
        foreach (Transform g in root.transform)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            Log($"PROBE {g.name}: children={g.childCount} renderers={rs.Length}");
            int shown = 0;
            foreach (var r in rs)
            {
                if (shown++ >= 3) break;
                var mats = string.Join(",", System.Array.ConvertAll(r.sharedMaterials, m => m ? $"{m.name}[{m.shader.name} ok={m.shader.isSupported} pass={m.passCount}]" : "null"));
                Log($"   {r.transform.parent.name}/{r.name} enabled={r.enabled} active={r.gameObject.activeInHierarchy} bounds={r.bounds.center} size={r.bounds.size} mats={mats}");
            }
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off && (r.bounds.size.x > 6f || r.bounds.size.z > 6f))
                Log($"   BIG CASTER {r.transform.parent.name}/{r.name} c={r.bounds.center} size={r.bounds.size} mode={r.shadowCastingMode}");
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var c = r.bounds.center;
            if (c.x > -6 && c.x < 14 && c.z < -44 && c.z > -60 && r.bounds.size.y > 1f)
                Log($"   NEAR EDGE VIEW {r.transform.parent.name}/{r.name} c={c} size={r.bounds.size} mats={string.Join(",", System.Array.ConvertAll(r.sharedMaterials, m => m ? m.name + "/" + m.shader.name : "null"))}");
        }
        {
            var gmf = GameObject.Find("SnowPlague/Ground").GetComponent<MeshFilter>().sharedMesh;
            var gv = gmf.vertices; var gc = gmf.colors;
            float ymin = 1e9f, ymax = -1e9f;
            var sb = new System.Text.StringBuilder("GROUND line z=0: ");
            for (int k = 0; k < gv.Length; k++)
            {
                var p = gv[k];
                if (Mathf.Abs(p.x) < SPBuilder.HX && Mathf.Abs(p.z) < SPBuilder.HZ) { ymin = Mathf.Min(ymin, p.y); ymax = Mathf.Max(ymax, p.y); }
                if (Mathf.Abs(p.z) < 0.01f && Mathf.Abs(p.x % 6f) < 0.01f) sb.Append($"x{p.x:0} y{p.y:0.00} c({gc[k].r:0.0},{gc[k].g:0.0},{gc[k].b:0.0},{gc[k].a:0.0}) ");
            }
            Log(sb.ToString()); Log($"GROUND arena y range {ymin:0.00}..{ymax:0.00} verts={gv.Length}");
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) Log("ROOT OBJECT " + go.name);
            foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (EditorUtility.IsPersistent(r)) continue;
                if (r.hideFlags == HideFlags.None && r.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()) continue;
                Log($"HIDDEN RENDERER {r.name} flags={r.hideFlags} scene='{r.gameObject.scene.name}' active={r.gameObject.activeInHierarchy} enabled={r.enabled} bounds={r.bounds.center}/{r.bounds.size} mat={(r.sharedMaterial ? r.sharedMaterial.name : "-")}");
            }
            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                if (!EditorUtility.IsPersistent(mb) && mb && mb.gameObject.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                    Log($"OUTSIDE BEHAVIOUR {mb.GetType().Name} on {mb.name} scene='{mb.gameObject.scene.name}' flags={mb.hideFlags} enabled={mb.enabled}");
        }
        foreach (var s in new[] { "SnowPlague/Vegetation", "SnowPlague/Foreground", "SnowPlague/Prop", "SnowPlague/Ground" })
        {
            var sh = Shader.Find(s);
            Log($"PROBE shader {s}: found={(sh != null)} supported={(sh ? sh.isSupported : false)} errors={(sh ? ShaderUtil.ShaderHasError(sh) : false)}");
            if (sh) foreach (var m in ShaderUtil.GetShaderMessages(sh)) Log($"   {m.severity} {m.message} line {m.line}");
        }
    }

    // perf: shadow casters (triangles) per top-level group and per mesh, lights and their shadows/ranges
    static void Casters()
    {
        if (EditorSceneManager.GetActiveScene().path != SPBuilder.ScenePath) EditorSceneManager.OpenScene(SPBuilder.ScenePath, OpenSceneMode.Single);
        var root = GameObject.Find("SnowPlague"); if (!root) { Log("CASTERS no root"); return; }
        long Tris(Mesh m) { long t = 0; if (m) for (int i = 0; i < m.subMeshCount; i++) t += m.GetIndexCount(i) / 3; return t; }
        var byMesh = new Dictionary<string, (long tris, int n)>();
        long all = 0, allDraw = 0; int rn = 0;
        foreach (Transform g in root.transform)
        {
            long cast = 0, draw = 0; int nc = 0, nr = 0; var matsSet = new HashSet<Material>();
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = r.GetComponent<MeshFilter>(); if (!mf) continue;
                long t = Tris(mf.sharedMesh); nr++; draw += t;
                foreach (var m in r.sharedMaterials) if (m) matsSet.Add(m);
                if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off) continue;
                cast += t; nc++;
                var k = (mf.sharedMesh ? mf.sharedMesh.name : "?") + " / " + g.name;
                byMesh[k] = byMesh.TryGetValue(k, out var o) ? (o.tris + t, o.n + 1) : (t, 1);
            }
            all += cast; allDraw += draw; rn += nr;
            Log($"CASTERS {g.name}: renderers {nr} ({draw / 1000}k tris, {matsSet.Count} mats), casters {nc} ({cast / 1000}k tris)");
        }
        Log($"CASTERS total renderers {rn} ({allDraw / 1000}k tris), caster tris {all / 1000}k");
        var list = new List<KeyValuePair<string, (long tris, int n)>>(byMesh); list.Sort((a, b) => b.Value.tris.CompareTo(a.Value.tris));
        for (int i = 0; i < list.Count && i < 25; i++) Log($"   {list[i].Key}: {list[i].Value.n} x = {list[i].Value.tris / 1000}k");
        foreach (var l in root.GetComponentsInChildren<Light>(true))
            Log($"   LIGHT {l.name} {l.type} range {l.range:0.0} shadows {l.shadows} res {l.shadowResolution} on={l.isActiveAndEnabled} at {l.transform.position}");
        var a = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (a) Log($"   URP shadowDistance {a.shadowDistance} cascades {a.shadowCascadeCount} mainRes {a.mainLightShadowmapResolution} addRes {a.additionalLightsShadowmapResolution} msaa {a.msaaSampleCount} hdr {a.supportsHDR} renderScale {a.renderScale} srpBatcher {a.useSRPBatcher}");
        Log("CASTERS OK");
    }

    // RoomRun-angle shots at 1920x1080: centre, the south edge with a stand-in player behind the FG pines (dither hole),
    // the lantern yard, the corpse spot, and a zoomed-out overview.
    static void Shots(string outDir)
    {
        if (EditorSceneManager.GetActiveScene().path != SPBuilder.ScenePath) EditorSceneManager.OpenScene(SPBuilder.ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(outDir);
        var cam = Camera.main; var field = Object.FindAnyObjectByType<SPField>();
        if (!cam || !field) { Log("SHOTS no camera/field"); return; }
        // perf A/B: an output folder whose name contains "nopack" captures with the old per-chunk instanced draw
        bool pack = !Path.GetFileName(outDir.TrimEnd('/', '\\')).Contains("nopack");
        if (SPScatter.PackVisible != pack)
        {
            SPScatter.PackVisible = pack;
            foreach (var s in Object.FindObjectsByType<SPScatter>(FindObjectsSortMode.None)) s.Push();
        }
        Log($"SHOTS packVisible={pack}");
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Simulate(ps.main.prewarm ? 10f : 3f, true, true);
        // 11-11: other fields' DontSave objects can outlive their scene in a shared editor (e.g. ~50 BA_Ground tiles from
        // the bamboo arena drew over this field). Hide anything that belongs to no scene while capturing; restored below.
        var orphans = new List<Renderer>();
        foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
            if (r && !EditorUtility.IsPersistent(r) && r.enabled && !r.gameObject.scene.IsValid()) { r.enabled = false; orphans.Add(r); }
        if (orphans.Count > 0) Log($"SHOTS hid {orphans.Count} orphan renderers from other fields (e.g. {orphans[0].name})");
        var stand = GameObject.CreatePrimitive(PrimitiveType.Capsule); stand.name = "Player (capture stand-in)";
        Object.DestroyImmediate(stand.GetComponent<Collider>());
        stand.SetActive(false);
        var views = new (string name, Vector3 focus, float zoom, bool player)[]
        {
            ("01_overview", new Vector3(0, 0, 6f), 2.4f, false),
            ("02_lantern_yard", new Vector3(-48f, 0, 34f), 1.3f, false),   // tr30 (lead): reframed to include the west edge with the three hero lanterns (was -40, 28 x1)
            ("03_corpse_cart", new Vector3(-42f, 0, -12f), 1f, false),
            ("04_edge_foreground", new Vector3(6f, 0, -SPBuilder.HZ + 1.5f), 1f, true),
            ("05_cart", new Vector3(44f, 0, 8f), 1f, false),
            ("06_ground_close", new Vector3(-20f, 0, -6f), 0.55f, false),
            ("07_game_centre", new Vector3(0f, 0, 0f), 1.6f, false),
            ("08_north_bridge", new Vector3(-20f, 0, 36f), 1.6f, false),
            ("09_north_gate", new Vector3(40f, 0, 36f), 1.6f, false),
            ("10_west_plateau", new Vector3(-42f, 0, 40f), 1.6f, false),
            ("11_west_bridge", new Vector3(-40f, 0, -19f), 1.6f, false),
            ("12_east_bridge", new Vector3(47f, 0, 23f), 1.6f, false),
        };
        // In Play the RoomRun StageMood volume (dark fantasy grade, "snow" entry) sits on top; captures get the same.
        var moodGo = new GameObject("StageMood (capture)");
        var vol = moodGo.AddComponent<UnityEngine.Rendering.Volume>(); vol.isGlobal = true; vol.priority = 1000;
        var prof = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>(); vol.sharedProfile = prof;
        var ca = prof.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
        // StageMood "snow" (r10): exposure = the stage volume's own + 0.15, saturation -22, contrast 24, light sepia filter
        float baseEv = 0f;
        foreach (var sv in Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))
            if (sv != vol && sv.isGlobal && sv.sharedProfile && sv.sharedProfile.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var sca) && sca.postExposure.overrideState) baseEv = sca.postExposure.value;
        ca.postExposure.Override(baseEv + 1.3f);   // = StageMood "snow" exposure (tr12: 0.15 -> 0.45, tr13: 0.55, look2: 1.0, look4: 1.15, look5: 1.25, look6: 1.3)
        // look1: these were commented out by a stray "//" on the line above, so captures graded at SP_PostFX contrast 22.
        // Play: StageMood "snow" contrast + filter, then LookFX "snow" saturation -30 (priority 1100) on top
        ca.saturation.Override(-30f); ca.contrast.Override(50f); ca.colorFilter.Override(new Color(1.0f, 0.95f, 0.87f));
        // FieldLighting dims the ambient x0.75 in Play; StageMood "snow" greys the moon by 0.15 and the ambient by 0.25
        Color Grey(Color c, float k) { float y = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f; return new Color(Mathf.Lerp(c.r, y, k), Mathf.Lerp(c.g, y, k), Mathf.Lerp(c.b, y, k), c.a); }
        var amb = (RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor);
        const float moodAmb = 0.55f;   // = StageMood "snow" ambient (tr13: deeper shadows)
        RenderSettings.ambientSkyColor = Grey(RenderSettings.ambientSkyColor, 0.25f) * 0.75f * moodAmb; RenderSettings.ambientEquatorColor = Grey(RenderSettings.ambientEquatorColor, 0.25f) * 0.75f * moodAmb; RenderSettings.ambientGroundColor = Grey(RenderSettings.ambientGroundColor, 0.25f) * 0.75f * moodAmb;
        var sunL = RenderSettings.sun; var sunCol = sunL ? sunL.color : Color.white;
        if (sunL) sunL.color = Grey(sunCol, 0.15f);
        var smh = prof.Add<UnityEngine.Rendering.Universal.ShadowsMidtonesHighlights>(true);
        smh.shadows.Override(new Vector4(0.86f, 0.92f, 1.08f, -0.12f)); smh.midtones.Override(new Vector4(0.98f, 0.98f, 1f, -0.04f)); smh.highlights.Override(new Vector4(1.04f, 1f, 0.94f, 0f));
        var vg = prof.Add<UnityEngine.Rendering.Universal.Vignette>(true); vg.intensity.Override(0.42f); vg.smoothness.Override(0.55f); vg.color.Override(new Color(0.01f, 0.012f, 0.018f));
        int w = 1920, h = 1080;
        // hero pine close-ups at the game camera pitch (tr5): one inside-the-field hero (side/accent) and one foreground hero
        var shotList = new List<(string name, Vector3 focus, float zoom, bool player)>(views);
        if (outDir.Contains("r13only")) shotList.Clear();   // r13: only the round-13 views
        shotList.AddRange(SPBuilder.R13Views());
        Transform heroIn = null, heroFg = null, heroPl = null;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (!t.name.StartsWith("SKF_HeroPine") || !t.parent) continue;
            if (t.parent.name == "Foreground") { if (!heroFg || Mathf.Abs(t.position.x) < Mathf.Abs(heroFg.position.x)) heroFg = t; }
            else if (t.parent.name == "WestPlateau") heroPl = t;
            else if (t.parent.name == "VegetationFrame" && Mathf.Abs(t.position.x) < SPBuilder.HX - 1f && Mathf.Abs(t.position.z) < SPBuilder.HZ - 1f) heroIn = t;   // an accent inside the arena
        }
        // tr7: frame by the crown (renderer bounds centre), camera pulled back
        Vector3 Crown(Transform t) { var b = new Bounds(t.position, Vector3.zero); foreach (var r in t.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds); return new Vector3(b.center.x, 0, b.center.z + b.extents.y * 0.35f); }
        if (heroIn) shotList.Add(("13_hero_close", Crown(heroIn), 1.25f, false));
        if (heroPl) shotList.Add(("14_hero_plateau", Crown(heroPl), 1.25f, false));
        else if (heroFg) shotList.Add(("14_hero_foreground", new Vector3(heroFg.position.x, 0, -SPBuilder.HZ + 1.5f), 1f, true));
        var deck = GameObject.Find("SP_MX_Bridge");
        if (deck) shotList.Add(("15_bridge", new Vector3(deck.transform.position.x, 0, deck.transform.position.z), 1.15f, false));
        shotList.Add(("17_layout_top", Vector3.zero, 0f, false));   // straight down over the whole map
        shotList.Add(("18_house", new Vector3(SPBuilder.HX + 6f, 0, 10f), 1.3f, false));
        shotList.Add(("16_west_gate", new Vector3(SPBuilder.LMGatePos.x + 6f, 0, SPBuilder.LMGatePos.y + 2f), 1.35f, false));   // 솟을삼문 + jangseung + pagoda
        {   // tr30: Korean stone lantern close-up (an east-edge one, focus a little inside so the lantern sits mid-frame)
            var kls = GameObject.Find("KoreanLanterns"); Transform kl0 = null;
            if (kls) foreach (Transform t in kls.transform) if (t.position.x > 0f && (!kl0 || Mathf.Abs(t.position.z) < Mathf.Abs(kl0.position.z))) kl0 = t;
            if (kl0) shotList.Add(("19_lantern", new Vector3(kl0.position.x - 1.5f, 0, kl0.position.z + 1.2f), 0.4f, false));
        }
        shotList.Add(("20_hero_corner", new Vector3(SPBuilder.HCView.x, 0, SPBuilder.HCView.z), SPBuilder.HCView.y, false));   // tr36: ss5 layout - bridge + gate top-left, lanterns mid, corpses right
        foreach (var v in shotList)
        {
            if (v.zoom <= 0f) { cam.transform.SetPositionAndRotation(new Vector3(0, 330f, 0), Quaternion.Euler(90f, 0f, 0f)); }
            else SPBuilder.SetGameView(cam, v.focus, v.zoom);
            SPBuilder.SnowfallFollow(cam);   // tr24: snow layers sit on this view line
            stand.SetActive(v.player); stand.transform.position = v.focus + Vector3.up;
            field.playerOverride = v.player ? stand.transform : null;
            field.SetGlobals(cam);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            cam.targetTexture = null; RenderTexture.active = null;
            var file = Path.Combine(outDir, v.name + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            Log("SHOT " + file);
        }
        if (outDir.Contains("diag"))
        {
            void Snap(string n)
            {
                var rt2 = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt2; cam.Render(); cam.Render();
                RenderTexture.active = rt2; var t2 = new Texture2D(w, h, TextureFormat.RGB24, false); t2.ReadPixels(new Rect(0, 0, w, h), 0, 0); t2.Apply();
                cam.targetTexture = null; RenderTexture.active = null; File.WriteAllBytes(Path.Combine(outDir, n + ".png"), t2.EncodeToPNG());
                Object.DestroyImmediate(t2); rt2.Release(); Object.DestroyImmediate(rt2);
            }
            SPBuilder.SetGameView(cam, views[0].focus, views[0].zoom); stand.SetActive(false); field.playerOverride = null; field.SetGlobals(cam);
            var moon = RenderSettings.sun; moon.enabled = false; Snap("diag_moon_off"); moon.enabled = true;
            var gr = GameObject.Find("SnowPlague/Ground").GetComponent<Renderer>(); var gm = gr.sharedMaterial;
            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit")); lit.color = new Color(0.7f, 0.72f, 0.75f);
            gr.sharedMaterial = lit; Snap("diag_ground_urplit"); gr.sharedMaterial = gm; Object.DestroyImmediate(lit);
            Log($"DIAG ground lightmapIndex={gr.lightmapIndex} probeUsage={(gr as MeshRenderer).lightProbeUsage} staticFlags={GameObjectUtility.GetStaticEditorFlags(gr.gameObject)} APV={UnityEngine.Rendering.ProbeReferenceVolume.instance.isInitialized} data={UnityEngine.Rendering.ProbeReferenceVolume.instance.DataHasBeenLoaded()}");
            var rootT = GameObject.Find("SnowPlague").transform;
            var keepOn = new System.Collections.Generic.HashSet<string> { "Ground", "Moonlight", "Main Camera", "PostFX" };
            var groups = new System.Collections.Generic.List<GameObject>();
            foreach (Transform t in rootT) if (!keepOn.Contains(t.name) && t.gameObject.activeSelf) { groups.Add(t.gameObject); t.gameObject.SetActive(false); }
            var fld = field.enabled; field.enabled = false;
            Snap("diag_iso_none");
            foreach (var gobj in groups) { gobj.SetActive(true); Snap("diag_iso_" + gobj.name); gobj.SetActive(false); }
            field.enabled = true; Snap("diag_iso_fieldonly");
            foreach (var gobj in groups) gobj.SetActive(true);
            {
                var rd = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
                var ao = rd ? rd.rendererFeatures.Find(f => f && f.name.Contains("Ambient")) : null;
                if (ao) { bool was = ao.isActive; ao.SetActive(false); Snap("diag_ssao_off"); ao.SetActive(was); Log("DIAG ssao toggled, restored to " + was); }
            }
            var ms = moon.shadows; moon.shadows = LightShadows.None; Snap("diag_moon_noshadow"); moon.shadows = ms;
            Log($"DIAG moon {moon.name} type={moon.type} cookie={(moon.cookie ? moon.cookie.name : "none")} bake={moon.lightmapBakeType} rot={moon.transform.eulerAngles} shadowDist={QualitySettings.shadowDistance}");
            var fx = GameObject.Find("SnowPlague/FogVfx"); fx.SetActive(false); Snap("diag_fogvfx_off"); fx.SetActive(true);
            // the edge view again with the full-screen height fog off
            var fog = field.heightFog; field.heightFog = null;
            SPBuilder.SetGameView(cam, views[3].focus, 1f); stand.SetActive(true); field.playerOverride = stand.transform; field.SetGlobals(cam);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            cam.targetTexture = null; RenderTexture.active = null; File.WriteAllBytes(Path.Combine(outDir, "diag_nofog.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); field.heightFog = fog;
        }
        field.playerOverride = null; Object.DestroyImmediate(stand);
        Object.DestroyImmediate(moodGo); Object.DestroyImmediate(prof);
        (RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor) = amb;
        if (sunL) sunL.color = sunCol;
        foreach (var r in orphans) if (r) r.enabled = true;
        SPBuilder.SetGameView(cam, Vector3.zero, 1f);
        if (!SPScatter.PackVisible) { SPScatter.PackVisible = true; foreach (var s in Object.FindObjectsByType<SPScatter>(FindObjectsSortMode.None)) s.Push(); }
        Log("SHOTS OK");
    }
}
