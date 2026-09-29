using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class LogoSetup
{
    const string Work = "Temp/LogoRevealWork";
    const string GraphPath = "Assets/Shader/KSW/LogoDissolve.shadergraph";
    const string MaterialPath = "Assets/Materials/KSW/LogoDissolve.mat";
    const string AnimationPath = "Assets/Animations/KSW/TempleEntrance/LogoReveal.anim";
    const string VerifyKey = "LogoSetup.Verify";
    static double nextPoll;
    static double sampleReady;
    static int sampleIndex = -1;
    static readonly double[] Times = { 28.0, 29.0, 29.5, 30.3, 30.3, 30.3 };
    static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly List<string> Messages = new List<string>();

    static LogoSetup() { EditorApplication.update += Update; }

    static void Update()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (SessionState.GetBool(VerifyKey, false))
        {
            if (EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isPlaying) VerifyTick();
        }
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .3;
        string commandPath = Work + "/request.txt";
        if (!File.Exists(commandPath)) return;
        string request = File.ReadAllText(commandPath).Trim();
        if (SessionState.GetString("LogoSetup.LastRequest", "") == request) return;
        SessionState.SetString("LogoSetup.LastRequest", request);
        string action = request.Split(':')[0];
        try
        {
            if (action == "probe") Probe();
            if (action == "inspect") Inspect();
            if (action == "apply") Apply();
            if (action == "verify")
            {
                Messages.Clear(); sampleIndex = -1;
                SessionState.SetBool(VerifyKey, true);
                EditorApplication.isPlaying = true;
                Write("verify-started", "Play mode verification requested.");
            }
            if (action == "finish") Finish();
            if (action == "stop") { SessionState.SetBool(VerifyKey, false); EditorApplication.isPlaying = false; }
        }
        catch (Exception ex) { Write(action + "-error", ex.ToString()); Debug.LogException(ex); }
    }

    static RawImage Logo()
    {
        return Resources.FindObjectsOfTypeAll<RawImage>().Single(x => x.gameObject.scene.IsValid() && x.name == "Logo");
    }

    static PlayableDirector Director()
    {
        return Resources.FindObjectsOfTypeAll<PlayableDirector>().Single(x => x.gameObject.scene.IsValid() && x.name == "Temple Cutscene");
    }

    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    static void SaveOpenGraph()
    {
        foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
        {
            if (window.GetType().Name != "MaterialGraphEditWindow") continue;
            var property = window.GetType().GetProperty("selectedGuid", Flags);
            string guid = property == null ? "" : property.GetValue(window) as string;
            if (AssetDatabase.GUIDToAssetPath(guid) != GraphPath) continue;
            if (window.hasUnsavedChanges) window.GetType().GetMethod("SaveAsset", Flags).Invoke(window, null);
        }
    }

    static void Probe()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play mode before setup.");
        SaveOpenGraph();
        var logo = Logo(); var canvas = logo.canvas; var director = Director();
        DumpShader();
        Write("probe", "scene=" + logo.gameObject.scene.path + "\ndirty=" + logo.gameObject.scene.isDirty
            + "\nlogo=" + PathOf(logo.transform) + "\nalpha=" + logo.color.a
            + "\nmaterial=" + AssetDatabase.GetAssetPath(logo.material)
            + "\nsize=" + logo.rectTransform.sizeDelta + "\ncanvas=" + canvas.renderMode
            + "\ncamera=" + (canvas.worldCamera ? canvas.worldCamera.name : "None")
            + "\ndirectorTime=" + director.time + "\ntimeline=" + AssetDatabase.GetAssetPath(director.playableAsset)
            + "\nduration=" + director.duration + "\ngraphHash=" + Hash(GraphPath));
    }

    static string PathOf(Transform t) { return t.parent ? PathOf(t.parent) + "/" + t.name : t.name; }

    static void Inspect()
    {
        DumpShader();
        Write("inspected", Details());
    }

    static void DumpShader()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.ShaderGraph.ShaderGraphImporter")).FirstOrDefault(t => t != null);
        var method = type.GetMethods(Flags).Single(m => m.Name == "GetShaderText" && m.GetParameters().Length == 2);
        object[] arguments = { GraphPath, null };
        File.WriteAllText(Work + "/Generated.shader", (string)method.Invoke(null, arguments));
    }

    static string Details()
    {
        var logo = Logo(); var canvas = logo.canvas; var camera = canvas.worldCamera;
        var mesh = logo.canvasRenderer.GetMesh();
        string info = "cull=" + logo.canvasRenderer.cull + " inheritedAlpha=" + logo.canvasRenderer.GetInheritedAlpha()
            + " rendererColor=" + logo.canvasRenderer.GetColor() + " vertices=" + (mesh ? mesh.vertexCount : 0)
            + " meshAlpha=" + (mesh && mesh.colors32.Length > 0 ? mesh.colors32[0].a.ToString() : "None")
            + " near=" + (camera ? camera.nearClipPlane : 0) + " plane=" + canvas.planeDistance + " canvasEnabled=" + canvas.enabled
            + " viewport=" + (camera ? camera.WorldToViewportPoint(logo.transform.position).ToString() : "Overlay")
            + " keywords=" + string.Join(",", logo.materialForRendering.shaderKeywords);
        Object.DestroyImmediate(mesh);
        return info;
    }

    static IEnumerable<TrackAsset> Tracks(IEnumerable<TrackAsset> tracks)
    {
        foreach (var track in tracks)
        {
            yield return track;
            foreach (var child in Tracks(track.GetChildTracks())) yield return child;
        }
    }

    static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play mode before setup.");
        SaveOpenGraph();
        string expected = File.ReadAllText(Work + "/source-hash.txt").Trim();
        if (Hash(GraphPath) != expected) throw new Exception("Graph changed after preparation. Rebuild from the saved graph.");
        var logo = Logo(); var director = Director(); var timeline = (TimelineAsset)director.playableAsset;
        var scene = logo.gameObject.scene;
        if (!scene.path.EndsWith("Ruins_KSW.unity")) throw new Exception("Unexpected scene.");
        Directory.CreateDirectory(Work + "/Backup");
        EditorSceneManager.SaveScene(scene, Work + "/Backup/Before.unity", true);
        AssetDatabase.SaveAssetIfDirty(timeline);
        File.Copy(AssetDatabase.GetAssetPath(timeline), Work + "/Backup/Before.playable", true);
        File.Copy(GraphPath, Work + "/Backup/Before.shadergraph", true);

        File.Copy(Work + "/LogoDissolve.shadergraph", GraphPath, true);
        AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(GraphPath);
        if (!shader || !shader.isSupported) throw new Exception("Logo shader is missing or unsupported.");
        var errors = ShaderUtil.GetShaderMessages(shader).Where(x => x.severity.ToString() == "Error").ToArray();
        if (errors.Length > 0) throw new Exception(string.Join("\n", errors.Select(x => x.message)));

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
        else { Undo.RecordObject(material, "Logo dissolve"); material.shader = shader; }
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/KSW/Logo.png");
        material.SetTexture("_LogoTexture", texture);
        material.SetFloat("_Distort", .04f);
        material.SetFloat("_EdgeWidth", .035f);
        material.SetFloat("_Glow", 2.5f);
        EditorUtility.SetDirty(material);
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
        if (importer && importer.wrapMode != TextureWrapMode.Clamp) { importer.wrapMode = TextureWrapMode.Clamp; importer.SaveAndReimport(); }

        Undo.RecordObject(logo, "Logo dissolve");
        logo.material = material; logo.texture = texture;
        Color color = logo.color; color.a = 0; logo.color = color;
        logo.raycastTarget = false;
        var canvas = logo.canvas;
        if (canvas.renderMode != RenderMode.ScreenSpaceCamera || !canvas.worldCamera)
        {
            Undo.RecordObject(canvas, "Logo camera");
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
        }

        var all = Tracks(timeline.GetRootTracks()).ToArray();
        var group = all.OfType<GroupTrack>().Single(x => x.name == "Cut05");
        var gate = all.Single(x => x.name == "Gate Drop").GetClips().Single();
        var gateAsset = (AnimationPlayableAsset)gate.asset;
        double impact = gate.start + (gateAsset.clip.length - gate.clipIn) / gate.timeScale;
        double start = Math.Round(impact + .23, 2);
        double length = 1.6;
        if (start + length > timeline.duration) throw new Exception("Timeline is too short for the logo.");
        Undo.RecordObject(timeline, "Logo timeline");
        var track = group.GetChildTracks().OfType<AnimationTrack>().SingleOrDefault(x => x.name == "LogoReveal");
        if (!track) track = timeline.CreateTrack<AnimationTrack>(group, "LogoReveal");
        if (track.GetClips().Any()) throw new Exception("LogoReveal already contains a clip; preserve and inspect it.");
        var animator = logo.GetComponent<Animator>();
        if (!animator) animator = Undo.AddComponent<Animator>(logo.gameObject);
        animator.applyRootMotion = false;
        director.SetGenericBinding(track, animator);
        var animation = new AnimationClip { name = "LogoReveal", frameRate = 30 };
        AnimationUtility.SetEditorCurve(animation, EditorCurveBinding.FloatCurve("", typeof(RawImage), "m_Color.a"), AnimationCurve.Linear(0, 0, (float)length, 1));
        var settings = AnimationUtility.GetAnimationClipSettings(animation);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(animation, settings);
        AssetDatabase.CreateAsset(animation, AnimationPath);
        var clip = track.CreateClip<AnimationPlayableAsset>();
        var playable = (AnimationPlayableAsset)clip.asset;
        playable.clip = animation;
        playable.loop = AnimationPlayableAsset.LoopMode.Off;
        playable.removeStartOffset = false;
        clip.displayName = "LogoReveal";
        clip.start = start;
        clip.duration = timeline.duration - start;
        typeof(TimelineClip).GetField("m_PreExtrapolationMode", Flags).SetValue(clip, TimelineClip.ClipExtrapolation.Hold);
        typeof(TimelineClip).GetField("m_PostExtrapolationMode", Flags).SetValue(clip, TimelineClip.ClipExtrapolation.Hold);
        var recordable = typeof(TimelineClip).GetField("m_Recordable", Flags);
        if (recordable != null) recordable.SetValue(clip, true);
        EditorUtility.SetDirty(track); EditorUtility.SetDirty(playable); EditorUtility.SetDirty(timeline);
        EditorUtility.SetDirty(director); EditorUtility.SetDirty(logo);
        AssetDatabase.SaveAssetIfDirty(material);
        AssetDatabase.SaveAssetIfDirty(animation);
        AssetDatabase.SaveAssetIfDirty(timeline);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        director.RebuildGraph();
        Selection.activeGameObject = logo.gameObject;
        EditorApplication.QueuePlayerLoopUpdate();
        Write("applied", "logo=" + PathOf(logo.transform) + "\ntrack=Cut05/LogoReveal\nstart=" + start
            + "\nendReveal=" + (start + length) + "\nend=" + timeline.duration + "\nkeys=2\nshaderErrors=" + errors.Length);
    }

    static void VerifyTick()
    {
        try
        {
            if (sampleIndex < 0)
            {
                sampleIndex = 0;
                Messages.Clear();
                SetTime(Times[sampleIndex]);
                sampleReady = EditorApplication.timeSinceStartup + 1.0;
                return;
            }
            if (EditorApplication.timeSinceStartup < sampleReady) return;
            var logo = Logo();
            var shader = logo.material.shader;
            var errors = ShaderUtil.GetShaderMessages(shader).Where(x => x.severity.ToString() == "Error").Select(x => x.message);
            string file = Capture("Play-" + sampleIndex + ".png");
            Messages.Add("time=" + Times[sampleIndex] + " alpha=" + logo.color.a + " active=" + logo.isActiveAndEnabled
                + " material=" + logo.material.name + " supported=" + shader.isSupported + " errors=" + string.Join(";", errors) + " capture=" + file + "\n" + Details());
            sampleIndex++;
            if (sampleIndex >= Times.Length)
            {
                Write("verified", string.Join("\n", Messages));
                SessionState.SetBool(VerifyKey, false);
                EditorApplication.isPlaying = false;
                return;
            }
            SetTime(Times[sampleIndex]);
            sampleReady = EditorApplication.timeSinceStartup + .7;
        }
        catch (Exception ex)
        {
            SessionState.SetBool(VerifyKey, false);
            Write("verify-error", ex.ToString());
            EditorApplication.isPlaying = false;
        }
    }

    static void SetTime(double time)
    {
        var director = Director();
        if (!director.playableGraph.IsValid()) director.Play();
        director.Pause(); director.time = time; director.Evaluate();
        if (sampleIndex == 4) Logo().material = null;
        if (sampleIndex == 5) Logo().canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Canvas.ForceUpdateCanvases();
        EditorApplication.QueuePlayerLoopUpdate();
        foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>()) if (window.GetType().Name == "GameView") window.Repaint();
    }

    static string Capture(string filename)
    {
        var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        RenderTexture rt = null;
        var getter = gameViewType.GetMethod("GetMainGameViewRenderTexture", Flags);
        if (getter != null) rt = getter.Invoke(null, null) as RenderTexture;
        if (!rt) rt = Resources.FindObjectsOfTypeAll<RenderTexture>().Where(x => x.name.Contains("GameView") && x.width > 100).OrderByDescending(x => x.width).FirstOrDefault();
        if (!rt) throw new Exception("Game View render texture was not found.");
        var previous = RenderTexture.active;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            string path = Work + "/" + filename;
            File.WriteAllBytes(path, image.EncodeToPNG());
            return path;
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }

    static void Finish()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Play mode is still changing.");
        var director = Director();
        director.time = 0; director.Evaluate(); director.Stop();
        var logo = Logo(); Color color = logo.color; color.a = 0; logo.color = color;
        Selection.activeGameObject = logo.gameObject;
        EditorSceneManager.MarkSceneDirty(logo.gameObject.scene);
        EditorSceneManager.SaveScene(logo.gameObject.scene);
        Write("finished", "alpha=" + logo.color.a + "\nscene=" + logo.gameObject.scene.path);
    }

    static void Write(string status, string body)
    {
        Directory.CreateDirectory(Work);
        File.WriteAllText(Work + "/report.txt", "status=" + status + "\n" + body, new UTF8Encoding(false));
    }
}
