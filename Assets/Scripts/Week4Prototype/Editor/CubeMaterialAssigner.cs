#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameLab.Week4.Editor
{
    /// <summary>큐브 타입마다 구분되는 URP 재질을 생성하고 해당 프리팹 렌더러에 연결한다.</summary>
    public static class CubeMaterialAssigner
    {
        private const string MaterialFolder = "Assets/Materials/CubeTypes";

        private readonly struct MaterialSpec
        {
            public readonly string PrefabName;
            public readonly string MaterialName;
            public readonly Color BaseColor;
            public readonly float Metallic;
            public readonly float Smoothness;
            public readonly bool Transparent;
            public readonly Color EmissionColor;

            public MaterialSpec(
                string prefabName,
                string materialName,
                Color baseColor,
                float metallic,
                float smoothness,
                bool transparent = false,
                Color emissionColor = default)
            {
                PrefabName = prefabName;
                MaterialName = materialName;
                BaseColor = baseColor;
                Metallic = metallic;
                Smoothness = smoothness;
                Transparent = transparent;
                EmissionColor = emissionColor;
            }
        }

        private static readonly IReadOnlyList<MaterialSpec> Specs = new[]
        {
            new MaterialSpec("Cube", "M_Cube_Normal", new Color(0.78f, 0.75f, 0.68f, 1f), 0.02f, 0.38f),
            new MaterialSpec("Adhesive", "M_Cube_Adhesive", new Color(0.82f, 0.55f, 0.08f, 1f), 0.05f, 0.78f,
                emissionColor: new Color(0.08f, 0.035f, 0f, 1f)),
            new MaterialSpec("Brittle", "M_Cube_Brittle", new Color(0.72f, 0.25f, 0.08f, 1f), 0f, 0.16f),
            new MaterialSpec("Cracked", "M_Cube_Cracked", new Color(0.28f, 0.09f, 0.12f, 1f), 0f, 0.22f),
            new MaterialSpec("Glass", "M_Cube_Glass", new Color(0.55f, 0.94f, 0.95f, 0.27f), 0.05f, 0.94f, true),
            new MaterialSpec("ColoredGlass_Blue", "M_Cube_ColoredGlass_Blue", new Color(0.08f, 0.38f, 1f, 0.43f), 0.08f, 0.92f, true,
                new Color(0f, 0.018f, 0.08f, 1f)),
            new MaterialSpec("ColoredGlass_Red", "M_Cube_ColoredGlass_Red", new Color(1f, 0.1f, 0.07f, 0.43f), 0.08f, 0.92f, true,
                new Color(0.09f, 0.004f, 0.002f, 1f)),
            new MaterialSpec("ColoredGlass_Green", "M_Cube_ColoredGlass_Green", new Color(0.08f, 0.9f, 0.2f, 0.43f), 0.08f, 0.92f, true,
                new Color(0.002f, 0.075f, 0.008f, 1f)),
            new MaterialSpec("ColoredGlass_Yellow", "M_Cube_ColoredGlass_Yellow", new Color(1f, 0.72f, 0.05f, 0.43f), 0.08f, 0.92f, true,
                new Color(0.08f, 0.04f, 0.001f, 1f)),
            new MaterialSpec("Ice", "M_Cube_Ice", new Color(0.45f, 0.78f, 1f, 0.7f), 0f, 0.88f, true,
                new Color(0.01f, 0.045f, 0.075f, 1f)),
            new MaterialSpec("Styrofoam", "M_Cube_Styrofoam", new Color(0.94f, 0.92f, 0.82f, 1f), 0f, 0.08f),
            new MaterialSpec("LightEmitter", "M_Cube_LightEmitter", new Color(0.72f, 0.84f, 1f, 1f), 0.1f, 0.72f,
                emissionColor: new Color(0.12f, 0.18f, 0.24f, 1f)),
            new MaterialSpec("Refractor", "M_Cube_Refractor", new Color(0.16f, 0.62f, 0.72f, 1f), 0.25f, 0.82f,
                emissionColor: new Color(0.01f, 0.055f, 0.07f, 1f))
        };

        [MenuItem("Tools/GameLab/Setup Cube Type Materials")]
        public static void SetupMaterials()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            EnsureFolder(MaterialFolder);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("큐브 재질 생성 실패: Universal Render Pipeline/Lit 셰이더를 찾지 못했습니다.");
                return;
            }

            foreach (MaterialSpec spec in Specs)
            {
                Material material = CreateOrUpdateMaterial(shader, spec);
                if (!string.IsNullOrEmpty(spec.PrefabName))
                {
                    ApplyMaterialToPrefab(spec.PrefabName, material);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("큐브 타입별 URP 재질 생성 및 프리팹 적용을 완료했습니다.");
        }

        private static Material CreateOrUpdateMaterial(Shader shader, MaterialSpec spec)
        {
            string path = $"{MaterialFolder}/{spec.MaterialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = spec.MaterialName };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                // 기존 머티리얼은 사용자가 직접 조정한 색과 표면 설정을 보존한다.
                return material;
            }

            material.SetColor("_BaseColor", spec.BaseColor);
            material.SetColor("_Color", spec.BaseColor);
            material.SetFloat("_Metallic", spec.Metallic);
            material.SetFloat("_Smoothness", spec.Smoothness);
            ConfigureSurface(material, spec.Transparent);

            bool hasEmission = spec.EmissionColor.maxColorComponent > 0f;
            material.SetColor("_EmissionColor", spec.EmissionColor);
            if (hasEmission)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureSurface(Material material, bool transparent)
        {
            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetShaderPassEnabled("ShadowCaster", false);
            }
            else
            {
                material.SetFloat("_Surface", 0f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                material.SetFloat("_ZWrite", 1f);
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = -1;
                material.SetShaderPassEnabled("ShadowCaster", true);
            }
        }

        private static void ApplyMaterialToPrefab(string prefabName, Material material)
        {
            string prefabPath = $"Assets/Prefabs/{prefabName}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                return;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform markerRoot = prefabRoot.transform.Find("OpticDirectionMarkers");
                bool preserveAuthoredOpticParts = prefabName == "LightEmitter" || prefabName == "Refractor";
                foreach (Renderer renderer in prefabRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (markerRoot != null && renderer.transform.IsChildOf(markerRoot))
                    {
                        continue;
                    }

                    // Emitter와 Refractor의 자식들은 각자 저작된 재질과 모양을 사용한다.
                    // 몸체 Renderer만 자동 재질을 적용하고 자식 외형은 프리팹 저작 상태를 보존한다.
                    if (preserveAuthoredOpticParts && renderer.gameObject != prefabRoot)
                    {
                        continue;
                    }

                    Material[] materials = renderer.sharedMaterials;
                    if (materials.Length == 0)
                    {
                        renderer.sharedMaterial = material;
                        continue;
                    }

                    for (int index = 0; index < materials.Length; index++)
                    {
                        materials[index] = material;
                    }

                    renderer.sharedMaterials = materials;
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }
    }
}
#endif
