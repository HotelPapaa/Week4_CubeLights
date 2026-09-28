using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameLab.Week4
{
    /// <summary>
    /// GridBoard가 계산한 논리 광선 구간을 Laser 프리팹의 성장 애니메이션으로 표시한다.
    /// 광선 판정은 담당하지 않으며 LightSimulationResult의 시각화만 담당한다.
    /// </summary>
    public sealed class LaserPointer : MonoBehaviour
    {
        [Header("레이저 프리팹")]
        [SerializeField] private GameObject laserPrefab;
        [Tooltip("프리팹의 로컬 +Y축이 나타내는 기본 길이")]
        [Min(0.01f)] [SerializeField] private float prefabUnitLength = 1f;

        [Header("재생")]
        [Tooltip("초당 레이저가 자라는 월드 거리")]
        [Min(0.01f)] [SerializeField] private float growthSpeed = 6f;
        [Min(0f)] [SerializeField] private float emissionMultiplier = 3.5f;

        private readonly List<GameObject> spawnedLasers = new();
        private readonly List<Material> runtimeMaterials = new();
        private GridBoard board;
        private Transform laserRoot;
        private Coroutine shootCoroutine;
        private Func<LightBeamSegment, Vector3> resolveEndPosition;

        public bool CanRender => board != null && laserRoot != null && laserPrefab != null;

        public void Configure(GridBoard targetBoard, Transform targetRoot, GameObject targetPrefab)
        {
            board = targetBoard;
            laserRoot = targetRoot;
            if (targetPrefab != null)
            {
                laserPrefab = targetPrefab;
            }
        }

        /// <summary>기존 광선을 지우고 계산된 구간을 순서대로 재생한다.</summary>
        public void Play(
            IReadOnlyList<LightBeamSegment> route,
            Func<LightBeamSegment, Vector3> endPositionResolver = null)
        {
            Clear();
            if (!CanRender || route == null || route.Count == 0) return;

            resolveEndPosition = endPositionResolver;
            laserRoot.gameObject.SetActive(true);
            shootCoroutine = StartCoroutine(ShootLaser(route));
        }

        public void SetVisible(bool visible)
        {
            if (laserRoot != null)
            {
                laserRoot.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                StopPlayback();
            }
        }

        /// <summary>현재 재생과 생성된 런타임 레이저를 모두 정리한다.</summary>
        public void Clear()
        {
            StopPlayback();

            foreach (GameObject laser in spawnedLasers)
            {
                if (laser == null) continue;
                laser.SetActive(false);
                laser.transform.SetParent(null);
                if (Application.isPlaying) Destroy(laser);
                else DestroyImmediate(laser);
            }

            spawnedLasers.Clear();

            foreach (Material material in runtimeMaterials)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }

            runtimeMaterials.Clear();
            resolveEndPosition = null;
        }

        private void OnDestroy()
        {
            StopPlayback();
        }

        private void StopPlayback()
        {
            if (shootCoroutine == null) return;

            StopCoroutine(shootCoroutine);
            shootCoroutine = null;
        }

        private IEnumerator ShootLaser(IReadOnlyList<LightBeamSegment> route)
        {
            for (int index = 0; index < route.Count; index++)
            {
                yield return AnimateSegment(route, index);
            }

            shootCoroutine = null;
        }

        private IEnumerator AnimateSegment(IReadOnlyList<LightBeamSegment> route, int index)
        {
            LightBeamSegment segment = route[index];
            Vector3 startPosition = board.GridToWorld(segment.From);
            Vector3 endPosition = resolveEndPosition != null
                ? resolveEndPosition(segment)
                : board.GridToWorld(segment.To);

            // 색유리에서 바뀐 색이 큐브 중심부터 보이면 앞뒤 색이 반씩 겹쳐 보인다.
            // 연속 구간의 색 경계를 두 격자 중심의 중간, 즉 큐브가 시작되는 면으로 옮긴다.
            if (index > 0 && LightBeamVisualUtility.HasColorBoundary(route[index - 1], segment))
            {
                LightBeamSegment previous = route[index - 1];
                startPosition = Vector3.Lerp(
                    board.GridToWorld(previous.From),
                    board.GridToWorld(previous.To),
                    0.5f);
            }

            if (index + 1 < route.Count &&
                LightBeamVisualUtility.HasColorBoundary(segment, route[index + 1]))
            {
                endPosition = Vector3.Lerp(
                    board.GridToWorld(segment.From),
                    board.GridToWorld(segment.To),
                    0.5f);
            }

            Vector3 displacement = endPosition - startPosition;
            float distance = displacement.magnitude;
            if (distance <= Mathf.Epsilon) yield break;

            GameObject laserObject = Instantiate(laserPrefab, laserRoot);
            laserObject.name =
                $"Laser_{segment.From.x}_{segment.From.y}_{segment.From.z}_" +
                $"{segment.To.x}_{segment.To.y}_{segment.To.z}";
            spawnedLasers.Add(laserObject);

            Transform laserTransform = laserObject.transform;
            Vector3 baseScale = laserTransform.localScale;
            float targetScaleY = baseScale.y * (distance / Mathf.Max(0.01f, prefabUnitLength));

            laserTransform.SetPositionAndRotation(
                startPosition,
                Quaternion.FromToRotation(Vector3.up, displacement.normalized));
            laserTransform.localScale = new Vector3(baseScale.x, 0f, baseScale.z);

            Color color = LightDirectionUtility.ToDisplayColor(segment.Color);
            ConfigureRenderers(laserObject, color);

            float duration = distance / Mathf.Max(0.01f, growthSpeed);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                laserTransform.localScale = new Vector3(
                    baseScale.x,
                    targetScaleY * progress,
                    baseScale.z);
                yield return null;
            }

            laserTransform.localScale = new Vector3(baseScale.x, targetScaleY, baseScale.z);
        }

        private void ConfigureRenderers(GameObject laserObject, Color color)
        {
            foreach (Renderer renderer in laserObject.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                Material material = renderer.material;
                runtimeMaterials.Add(material);
                SetMaterialColor(material, color);
            }
        }

        private void SetMaterialColor(Material material, Color color)
        {
            if (material == null) return;

            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_EmissionColor"))
            {
                // 기존 Scene에 저장된 낮은 값도 선명하게 보이도록 최소 발광 강도를 보장한다.
                material.SetColor("_EmissionColor", color * Mathf.Max(3.5f, emissionMultiplier));
                material.EnableKeyword("_EMISSION");
            }
        }
    }
}
