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
        private readonly HashSet<Vector3Int> soundedInteractionCells = new();
        private GridBoard board;
        private Transform laserRoot;
        private Coroutine shootCoroutine;
        private Func<LightBeamSegment, Vector3> resolveEndPosition;
        private Action playbackCompleted;
        private SoundHandle laserSoundHandle;

        private sealed class BeamAnimation
        {
            public Transform Transform;
            public Vector3 BaseScale;
            public float TargetScaleY;
            public float Duration;
        }

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

        /// <summary>
        /// 기존 광선을 지우고 모든 Emitter의 직선 구간을 동시에 재생한다.
        /// 굴절기와 분배기를 통과해 생긴 다음 단계 광선만 앞 단계가 끝난 뒤 재생한다.
        /// </summary>
        public void Play(
            IReadOnlyList<LightBeamSegment> route,
            Func<LightBeamSegment, Vector3> endPositionResolver = null,
            Action onCompleted = null)
        {
            Clear();
            if (!CanRender || route == null || route.Count == 0)
            {
                onCompleted?.Invoke();
                return;
            }

            resolveEndPosition = endPositionResolver;
            playbackCompleted = onCompleted;
            soundedInteractionCells.Clear();
            laserRoot.gameObject.SetActive(true);
            laserSoundHandle = SoundManager.Play(
                SoundEventId.LaserLoop,
                board.GridToWorld(route[0].From));
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
                StopLaserSound();
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
            soundedInteractionCells.Clear();
            resolveEndPosition = null;
            StopLaserSound();
        }

        private void OnDestroy()
        {
            StopPlayback();
            StopLaserSound();
        }

        private void StopLaserSound()
        {
            if (!laserSoundHandle.IsValid) return;
            SoundManager.Stop(laserSoundHandle);
            laserSoundHandle = SoundHandle.Invalid;
        }

        private void StopPlayback()
        {
            if (shootCoroutine != null)
            {
                StopCoroutine(shootCoroutine);
            }
            shootCoroutine = null;
            playbackCompleted = null;
        }

        private IEnumerator ShootLaser(IReadOnlyList<LightBeamSegment> route)
        {
            int maximumSequenceStep = 0;
            for (int index = 0; index < route.Count; index++)
            {
                maximumSequenceStep = Mathf.Max(maximumSequenceStep, route[index].SequenceStep);
            }

            for (int sequenceStep = 0; sequenceStep <= maximumSequenceStep; sequenceStep++)
            {
                yield return AnimateSequenceStep(route, sequenceStep);
            }

            shootCoroutine = null;
            Action completed = playbackCompleted;
            playbackCompleted = null;
            completed?.Invoke();
        }

        private IEnumerator AnimateSequenceStep(IReadOnlyList<LightBeamSegment> route, int sequenceStep)
        {
            var animations = new List<BeamAnimation>();
            for (int index = 0; index < route.Count; index++)
            {
                if (route[index].SequenceStep != sequenceStep) continue;

                BeamAnimation animation = CreateSegmentAnimation(route, index);
                if (animation != null)
                {
                    animations.Add(animation);
                }
            }

            if (animations.Count == 0) yield break;

            float elapsed = 0f;
            bool isAnimating = true;
            while (isAnimating)
            {
                elapsed += Time.deltaTime;
                isAnimating = false;

                foreach (BeamAnimation animation in animations)
                {
                    float progress = Mathf.Clamp01(elapsed / animation.Duration);
                    animation.Transform.localScale = new Vector3(
                        animation.BaseScale.x,
                        animation.TargetScaleY * progress,
                        animation.BaseScale.z);
                    isAnimating |= progress < 1f;
                }

                if (isAnimating) yield return null;
            }
        }

        private BeamAnimation CreateSegmentAnimation(IReadOnlyList<LightBeamSegment> route, int index)
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
            if (distance <= Mathf.Epsilon) return null;

            PlayInteractionSound(segment);

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

            return new BeamAnimation
            {
                Transform = laserTransform,
                BaseScale = baseScale,
                TargetScaleY = targetScaleY,
                Duration = distance / Mathf.Max(0.01f, growthSpeed)
            };
        }

        /// <summary>광선 애니메이션이 해당 큐브에 도달한 순간 굴절·분기·색 변환음을 한 번만 재생한다.</summary>
        private void PlayInteractionSound(LightBeamSegment segment)
        {
            if (!soundedInteractionCells.Add(segment.From)) return;

            DraggableCube cube = board.GetCubeAt(segment.From);
            if (cube == null || !cube.TryGetComponent(out PuzzleCubeProperties properties)) return;

            SoundEventId eventId = properties.CubeType switch
            {
                PuzzleCubeType.Refractor => SoundEventId.LightRefract,
                PuzzleCubeType.LightSplitter => SoundEventId.LightSplit,
                PuzzleCubeType.ColoredGlass => SoundEventId.LightColorChange,
                _ => SoundEventId.None
            };
            if (eventId != SoundEventId.None)
            {
                SoundManager.Play(eventId, board.GridToWorld(segment.From));
            }
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
