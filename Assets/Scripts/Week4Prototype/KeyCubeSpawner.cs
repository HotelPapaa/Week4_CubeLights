using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GameLab.Week4
{
    /// <summary>
    /// 스테이지 클리어 보상 KeyCube를 떨어뜨리고, 순서에 맞는 Placer에 놓으면 다음 스테이지를 연다.
    /// 완성된 KeyCube는 Holder의 자식으로 고정되어 이후 스테이지에서도 위치를 유지한다.
    /// </summary>
    public sealed class KeyCubeSpawner : MonoBehaviour
    {
        private const int PlacerColumns = 6;
        private const int PlacerRows = 2;
        private const int RequiredKeyCubeCount = PlacerColumns * PlacerRows;

        [Header("연결")]
        [SerializeField] private StageManager stageManager;
        [SerializeField] private LightingSequenceController lightingController;
        [SerializeField] private Transform keyCubePlacerHolder;
        [SerializeField] private GameObject keyCubePrefab;

        [Header("Placer 재질")]
        [SerializeField] private Material readyMaterial;
        [SerializeField] private Material occupiedMaterial;

        [Header("진행 연출")]
        [Min(0f)] [SerializeField] private float spawnDelayAfterClear = 1f;
        [Min(0.05f)] [SerializeField] private float readyBlinkInterval = 0.25f;
        [Min(0.01f)] [SerializeField] private float placementMargin = 0.08f;

        [Header("12개 완성 연출")]
        [Min(0f)] [SerializeField] private float completionEffectStartDelay;
        [Min(0.1f)] [SerializeField] private float completionEffectDuration = 1.5f;
        [Min(0f)] [SerializeField] private float completionLightIntensity = 8f;
        [Min(0.1f)] [SerializeField] private float completionLightRange = 4f;
        [ColorUsage(false, true)] [SerializeField] private Color completionEmissionColor = new(4f, 4f, 4f, 1f);
        [SerializeField] private AnimationCurve completionEmissionGradation =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private readonly List<PlacerSlot> slots = new();
        private readonly HashSet<int> completedStages = new();
        private readonly List<GameObject> completedKeyCubes = new();
        private Coroutine clearSequence;
        private Coroutine completionEffectSequence;
        private GameObject activeKeyCube;
        private Rigidbody activeBody;
        private int activeStageIndex = -1;
        private int activeSlotIndex = -1;
        private bool isDragging;
        private Plane dragPlane;
        private Vector3 dragOffset;
        private float nextBlinkTime;
        private bool blinkReadyOn;
        private bool completionEffectPlayed;

        private sealed class PlacerSlot
        {
            public Transform Transform;
            public Renderer Renderer;
            public Collider Collider;
            public Material OffMaterial;
        }

        private sealed class RuntimeEmission
        {
            public Material Material;
            public Color StartColor;
        }

        private void Awake()
        {
            ResolveReferences();
            BuildOrderedSlots();
        }

        private void OnEnable()
        {
            ResolveReferences();
            BindStageManager();
        }

        private void Start()
        {
            BuildOrderedSlots();
        }

        private void OnDisable()
        {
            UnbindStageManager();
            CancelPendingClearSequence();
            if (completionEffectSequence != null)
            {
                StopCoroutine(completionEffectSequence);
                completionEffectSequence = null;
            }
        }

        private void Update()
        {
            UpdateReadyBlink();
            HandleKeyCubeDrag();
        }

        private void ResolveReferences()
        {
            if (stageManager == null) stageManager = FindFirstObjectByType<StageManager>();
            if (lightingController == null)
            {
                lightingController = FindFirstObjectByType<LightingSequenceController>();
            }

            if (keyCubePlacerHolder == null)
            {
                GameObject holder = GameObject.Find("KeyCubePlacerHolder");
                if (holder != null) keyCubePlacerHolder = holder.transform;
            }
        }

        private void BindStageManager()
        {
            if (stageManager == null) return;
            stageManager.StageCleared -= HandleStageCleared;
            stageManager.StageLoaded -= HandleStageLoaded;
            stageManager.DeveloperStageAdvanceRequested -= HandleDeveloperStageAdvanceRequested;
            stageManager.DeveloperFinalStageRewardRequested -= HandleDeveloperFinalStageRewardRequested;
            stageManager.StageCleared += HandleStageCleared;
            stageManager.StageLoaded += HandleStageLoaded;
            stageManager.DeveloperStageAdvanceRequested += HandleDeveloperStageAdvanceRequested;
            stageManager.DeveloperFinalStageRewardRequested += HandleDeveloperFinalStageRewardRequested;
        }

        private void UnbindStageManager()
        {
            if (stageManager == null) return;
            stageManager.StageCleared -= HandleStageCleared;
            stageManager.StageLoaded -= HandleStageLoaded;
            stageManager.DeveloperStageAdvanceRequested -= HandleDeveloperStageAdvanceRequested;
            stageManager.DeveloperFinalStageRewardRequested -= HandleDeveloperFinalStageRewardRequested;
        }

        private void BuildOrderedSlots()
        {
            if (keyCubePlacerHolder == null || slots.Count > 0) return;

            foreach (Renderer renderer in keyCubePlacerHolder.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null ||
                    !renderer.gameObject.name.StartsWith("KeyCubePlacer") ||
                    renderer.gameObject == keyCubePlacerHolder.gameObject)
                {
                    continue;
                }

                slots.Add(new PlacerSlot
                {
                    Transform = renderer.transform,
                    Renderer = renderer,
                    Collider = renderer.GetComponent<Collider>(),
                    OffMaterial = renderer.sharedMaterial
                });
            }

            // Holder 제작 좌표: 큰 local Y가 윗줄이며, 같은 줄에서는 작은 local Z가 왼쪽이다.
            slots.Sort((left, right) =>
            {
                Vector3 leftPosition = keyCubePlacerHolder.InverseTransformPoint(left.Renderer.bounds.center);
                Vector3 rightPosition = keyCubePlacerHolder.InverseTransformPoint(right.Renderer.bounds.center);
                int rowOrder = rightPosition.y.CompareTo(leftPosition.y);
                return rowOrder != 0 ? rowOrder : leftPosition.z.CompareTo(rightPosition.z);
            });

            if (slots.Count == 0)
            {
                Debug.LogWarning("KeyCubeSpawner: KeyCubePlacerHolder에서 Placer를 찾지 못했습니다.", this);
            }
        }

        private void HandleStageCleared(int stageIndex)
        {
            if (stageIndex < 0 || completedStages.Contains(stageIndex) || activeKeyCube != null) return;

            BuildOrderedSlots();
            int slotIndex = GetSlotIndexForStage(stageIndex);
            if (slotIndex < 0 || slotIndex >= slots.Count)
            {
                Debug.LogWarning($"KeyCubeSpawner: {stageIndex + 1}스테이지에 대응하는 Placer가 없습니다.", this);
                return;
            }

            CancelPendingClearSequence();
            clearSequence = StartCoroutine(ReturnToMainCameraAndSpawn(stageIndex, slotIndex));
        }

        private IEnumerator ReturnToMainCameraAndSpawn(int stageIndex, int slotIndex)
        {
            float delay = Mathf.Max(0f, spawnDelayAfterClear);
            if (lightingController != null)
            {
                // KeyCube가 Stage Camera 연출 중 보이지 않는 곳에서 먼저 떨어지지 않도록 한다.
                delay = Mathf.Max(delay, lightingController.StageClearPresentationDuration);
            }

            if (delay > 0f) yield return new WaitForSeconds(delay);

            clearSequence = null;
            ActivateSlot(stageIndex, slotIndex);
            SpawnKeyCube();
        }

        private void ActivateSlot(int stageIndex, int slotIndex)
        {
            activeStageIndex = stageIndex;
            activeSlotIndex = slotIndex;
            nextBlinkTime = Time.unscaledTime;
            // 첫 번째 스테이지만 점멸하고, 이후 스테이지는 준비 재질을 계속 표시한다.
            blinkReadyOn = stageIndex != 0;
            ApplyActiveSlotMaterial();
        }

        private static int GetSlotIndexForStage(int stageIndex)
        {
            if (stageIndex < 0) return -1;

            int row = stageIndex / PlacerColumns;
            if (row >= PlacerRows) return -1;

            int column = stageIndex % PlacerColumns;
            // 1~6스테이지는 윗줄, 7~12스테이지는 아랫줄의 왼쪽부터 진행한다.
            return row * PlacerColumns + column;
        }

        private void SpawnKeyCube()
        {
            if (keyCubePrefab == null)
            {
                Debug.LogError("KeyCubeSpawner: KeyCube Prefab이 연결되지 않았습니다.", this);
                return;
            }

            activeKeyCube = Instantiate(keyCubePrefab, transform.position, transform.rotation);
            activeKeyCube.name = $"KeyCube_Stage_{activeStageIndex + 1}";

            if (activeKeyCube.TryGetComponent(out DraggableCube draggable))
            {
                // 보드용 드래그 시스템이 잡지 않게 하고 이 Spawner가 별도로 조작한다.
                draggable.SetInteractionLocked(true);
            }

            activeBody = activeKeyCube.GetComponent<Rigidbody>() ?? activeKeyCube.AddComponent<Rigidbody>();
            activeBody.isKinematic = false;
            activeBody.useGravity = true;
            activeBody.constraints = RigidbodyConstraints.None;
            activeBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        private void UpdateReadyBlink()
        {
            if (activeSlotIndex < 0 || activeSlotIndex >= slots.Count || activeKeyCube == null) return;
            if (activeStageIndex != 0) return;
            if (Time.unscaledTime < nextBlinkTime) return;

            blinkReadyOn = !blinkReadyOn;
            nextBlinkTime = Time.unscaledTime + Mathf.Max(0.05f, readyBlinkInterval);
            ApplyActiveSlotMaterial();
        }

        private void ApplyActiveSlotMaterial()
        {
            if (activeSlotIndex < 0 || activeSlotIndex >= slots.Count) return;
            PlacerSlot slot = slots[activeSlotIndex];
            if (slot.Renderer == null) return;

            slot.Renderer.sharedMaterial = blinkReadyOn && readyMaterial != null
                ? readyMaterial
                : slot.OffMaterial;
        }

        private void HandleKeyCubeDrag()
        {
            if (activeKeyCube == null || activeSlotIndex < 0 || Mouse.current == null) return;

            Camera camera = Camera.main;
            if (camera == null) return;

            Vector2 pointer = Mouse.current.position.ReadValue();
            Ray ray = camera.ScreenPointToRay(pointer);
            if (!isDragging && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (Physics.Raycast(ray, out RaycastHit hit) &&
                    (hit.collider.gameObject == activeKeyCube ||
                     hit.collider.transform.IsChildOf(activeKeyCube.transform)))
                {
                    BeginDrag(hit.point);
                }
            }

            if (isDragging && Mouse.current.leftButton.isPressed && dragPlane.Raycast(ray, out float distance))
            {
                activeKeyCube.transform.position = ray.GetPoint(distance) + dragOffset;
            }

            if (isDragging && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                EndDrag();
            }
        }

        private void BeginDrag(Vector3 hitPoint)
        {
            isDragging = true;
            dragPlane = new Plane(Vector3.up, activeKeyCube.transform.position);
            dragOffset = activeKeyCube.transform.position - hitPoint;
            if (activeBody != null)
            {
                activeBody.linearVelocity = Vector3.zero;
                activeBody.angularVelocity = Vector3.zero;
                activeBody.isKinematic = true;
                activeBody.useGravity = false;
            }
        }

        private void EndDrag()
        {
            isDragging = false;
            if (TryGetPlacementPosition(out Vector3 placementPosition))
            {
                CompletePlacement(placementPosition);
                return;
            }

            if (activeBody != null)
            {
                activeBody.isKinematic = false;
                activeBody.useGravity = true;
            }
        }

        private bool TryGetPlacementPosition(out Vector3 placementPosition)
        {
            placementPosition = Vector3.zero;
            if (activeSlotIndex < 0 || activeSlotIndex >= slots.Count || activeKeyCube == null) return false;

            PlacerSlot slot = slots[activeSlotIndex];
            placementPosition = GetPlacementPosition(slot);

            Vector3 topDirection = GetSlotTopDirection(slot);
            Bounds slotBounds = slot.Renderer.bounds;
            Vector3 offset = activeKeyCube.transform.position - placementPosition;
            Vector3 planarOffset = offset - Vector3.Project(offset, topDirection);
            float radius = Mathf.Max(slotBounds.extents.x, slotBounds.extents.z) + placementMargin;
            return planarOffset.magnitude <= radius;
        }

        private Vector3 GetPlacementPosition(PlacerSlot slot)
        {
            Bounds slotBounds = slot.Renderer.bounds;
            Vector3 topDirection = GetSlotTopDirection(slot);

            float slotExtent = ProjectedExtent(slotBounds, topDirection);
            Collider keyCollider = activeKeyCube.GetComponentInChildren<Collider>();
            float keyExtent = keyCollider != null
                ? ProjectedExtent(keyCollider.bounds, topDirection)
                : 0.1f;
            return slotBounds.center + topDirection * (slotExtent + keyExtent + 0.005f);
        }

        private static Vector3 GetSlotTopDirection(PlacerSlot slot)
        {
            Vector3 topDirection = slot.Transform.TransformDirection(Vector3.right).normalized;
            if (Vector3.Dot(topDirection, Vector3.up) < 0f) topDirection = -topDirection;
            if (Mathf.Abs(Vector3.Dot(topDirection, Vector3.up)) < 0.5f) topDirection = Vector3.up;
            return topDirection;
        }

        private static float ProjectedExtent(Bounds bounds, Vector3 axis)
        {
            Vector3 absoluteAxis = new(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            return Vector3.Dot(bounds.extents, absoluteAxis);
        }

        private void CompletePlacement(Vector3 placementPosition, bool loadNextStage = true)
        {
            int completedStageIndex = activeStageIndex;
            PlacerSlot slot = slots[activeSlotIndex];
            activeKeyCube.transform.SetPositionAndRotation(placementPosition, slot.Transform.rotation);
            activeKeyCube.transform.SetParent(keyCubePlacerHolder, true);

            if (activeBody != null)
            {
                activeBody.linearVelocity = Vector3.zero;
                activeBody.angularVelocity = Vector3.zero;
                activeBody.useGravity = false;
                activeBody.isKinematic = true;
                activeBody.constraints = RigidbodyConstraints.FreezeAll;
            }

            if (activeKeyCube.TryGetComponent(out DraggableCube draggable))
            {
                draggable.SetInteractionLocked(true);
            }

            slot.Renderer.sharedMaterial = occupiedMaterial != null
                ? occupiedMaterial
                : slot.OffMaterial;
            completedStages.Add(completedStageIndex);
            completedKeyCubes.Add(activeKeyCube);
            activeKeyCube = null;
            activeBody = null;
            activeStageIndex = -1;
            activeSlotIndex = -1;
            blinkReadyOn = false;

            if (!completionEffectPlayed && completedStages.Count >= RequiredKeyCubeCount)
            {
                completionEffectPlayed = true;
                completionEffectSequence = StartCoroutine(PlayAllKeyCubesCompletedEffect());
            }

            if (loadNextStage) stageManager?.LoadNextStage();
        }

        private void HandleDeveloperStageAdvanceRequested(int stageIndex)
        {
            if (stageIndex < 0 || completedStages.Contains(stageIndex)) return;

            BuildOrderedSlots();
            int slotIndex = GetSlotIndexForStage(stageIndex);
            if (slotIndex < 0 || slotIndex >= slots.Count)
            {
                Debug.LogWarning($"KeyCubeSpawner: {stageIndex + 1}스테이지에 대응하는 Placer가 없습니다.", this);
                return;
            }

            CancelPendingClearSequence();
            if (activeKeyCube != null && activeStageIndex != stageIndex)
            {
                Destroy(activeKeyCube);
                activeKeyCube = null;
                activeBody = null;
            }

            if (activeKeyCube == null)
            {
                ActivateSlot(stageIndex, slotIndex);
                SpawnKeyCube();
            }
            else
            {
                activeStageIndex = stageIndex;
                activeSlotIndex = slotIndex;
            }

            if (activeKeyCube == null) return;
            CompletePlacement(GetPlacementPosition(slots[slotIndex]), loadNextStage: false);
        }

        private void HandleDeveloperFinalStageRewardRequested(int stageIndex)
        {
            // 실제 클리어 보상과 같은 지연·낙하 흐름을 사용하되 다음 스테이지로는 이동하지 않는다.
            HandleStageCleared(stageIndex);
        }

        private IEnumerator PlayAllKeyCubesCompletedEffect()
        {
            float startDelay = Mathf.Max(0f, completionEffectStartDelay);
            if (startDelay > 0f) yield return new WaitForSecondsRealtime(startDelay);

            var completionLights = new List<Light>();
            var runtimeEmissions = new List<RuntimeEmission>();
            foreach (GameObject keyCube in completedKeyCubes)
            {
                if (keyCube == null) continue;

                CollectRuntimeEmissions(keyCube, runtimeEmissions);

                GameObject lightObject = new("CompletionWhiteLight");
                lightObject.transform.SetParent(keyCube.transform, false);
                Light pointLight = lightObject.AddComponent<Light>();
                pointLight.type = LightType.Point;
                pointLight.color = Color.white;
                pointLight.intensity = 0f;
                pointLight.range = 0.1f;
                pointLight.shadows = LightShadows.None;
                completionLights.Add(pointLight);
            }

            Image whiteoutImage = CreateWhiteoutImage();
            float duration = Mathf.Max(0.1f, completionEffectDuration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                float emissionProgress = completionEmissionGradation != null
                    ? Mathf.Clamp01(completionEmissionGradation.Evaluate(progress))
                    : eased;

                foreach (RuntimeEmission emission in runtimeEmissions)
                {
                    if (emission.Material == null) continue;
                    emission.Material.SetColor(
                        "_EmissionColor",
                        Color.Lerp(emission.StartColor, completionEmissionColor, emissionProgress));
                }

                foreach (Light pointLight in completionLights)
                {
                    if (pointLight == null) continue;
                    pointLight.intensity = Mathf.Lerp(0f, completionLightIntensity, eased);
                    pointLight.range = Mathf.Lerp(0.1f, completionLightRange, eased);
                }

                if (whiteoutImage != null)
                {
                    float whiteoutProgress = Mathf.InverseLerp(0.35f, 1f, progress);
                    whiteoutImage.color = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, whiteoutProgress));
                }

                yield return null;
            }

            foreach (RuntimeEmission emission in runtimeEmissions)
            {
                if (emission.Material != null)
                {
                    emission.Material.SetColor("_EmissionColor", completionEmissionColor);
                }
            }

            if (whiteoutImage != null) whiteoutImage.color = Color.white;
            completionEffectSequence = null;
        }

        private static void CollectRuntimeEmissions(
            GameObject keyCube,
            ICollection<RuntimeEmission> runtimeEmissions)
        {
            foreach (Renderer renderer in keyCube.GetComponentsInChildren<Renderer>(true))
            {
                Material[] runtimeMaterials = renderer.materials;
                foreach (Material material in runtimeMaterials)
                {
                    if (material == null || !material.HasProperty("_EmissionColor")) continue;
                    Color startColor = material.GetColor("_EmissionColor");
                    material.EnableKeyword("_EMISSION");
                    runtimeEmissions.Add(new RuntimeEmission
                    {
                        Material = material,
                        StartColor = startColor
                    });
                }
            }
        }

        private static Image CreateWhiteoutImage()
        {
            GameObject canvasObject = new("KeyCubeCompletionWhiteout");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            canvasObject.AddComponent<CanvasScaler>();

            GameObject imageObject = new("Whiteout");
            imageObject.transform.SetParent(canvasObject.transform, false);
            Image image = imageObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = false;

            RectTransform rectTransform = image.rectTransform;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            return image;
        }

        private void HandleStageLoaded(int stageIndex)
        {
            CancelPendingClearSequence();
            if (activeKeyCube == null) return;

            if (activeSlotIndex >= 0 && activeSlotIndex < slots.Count)
            {
                PlacerSlot slot = slots[activeSlotIndex];
                if (slot.Renderer != null) slot.Renderer.sharedMaterial = slot.OffMaterial;
            }

            Destroy(activeKeyCube);
            activeKeyCube = null;
            activeBody = null;
            activeStageIndex = -1;
            activeSlotIndex = -1;
            isDragging = false;
        }

        private void CancelPendingClearSequence()
        {
            if (clearSequence == null) return;
            StopCoroutine(clearSequence);
            clearSequence = null;
        }
    }
}
