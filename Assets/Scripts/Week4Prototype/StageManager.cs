using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameLab.Week4
{
    /// <summary>
    /// 공용 플레이 씬에 스테이지 데이터를 불러오고 큐브 생성, 재시작, 성공 판정, 다음 진행을 관리한다.
    /// 파괴·용해 상태는 되감지 않고 스테이지를 다시 생성해 항상 동일한 초기 상태를 보장한다.
    /// </summary>
    public sealed class StageManager : MonoBehaviour
    {
        private const string CampaignResourcePath = "Stages/PuzzleCampaign";
        private const string ProgressKey = "Week4.HighestCompletedStage";

        [Header("스테이지 데이터")]
        [SerializeField] private PuzzleCampaign campaign;
        [SerializeField] private int startingStageIndex;

        [Header("씬 연결")]
        [SerializeField] private GridBoard board;
        [SerializeField] private PrototypeGameManager gameManager;
        [SerializeField] private LightingSequenceController lightingController;
        [SerializeField] private GameObject legacyCubeRoot;

        [Header("색 조합표")]
        [Tooltip("7번 색 조합 소개 스테이지부터 계속 표시할 씬 오브젝트")]
        [SerializeField] private GameObject colorChart;
        [Tooltip("ColorChart가 처음 공개되는 스테이지 에셋")]
        [SerializeField] private PuzzleStageDefinition colorChartRevealStage;

        [Header("Reserve 표시")]
        [SerializeField] private GameObject reserveSlotFramePrefab;

        [Header("프로토타입 탐색")]
        [SerializeField] private bool showStageUI = true;
        [SerializeField] private bool allowUnlockedNavigation = true;

        private Transform runtimeCubeRoot;
        private Vector3 spawnOrigin;
        private PuzzleStageDefinition currentStage;
        private int currentStageIndex;
        private bool stageSolved;
        private bool hasStarted;
        private bool loadingFromRestart;
        private bool colorChartRevealed;

        public PuzzleStageDefinition CurrentStage => currentStage;
        public int CurrentStageIndex => currentStageIndex;
        public bool StageSolved => stageSolved;
        public event Action<int> StageLoaded;
        public event Action<int> StageCleared;
        public event Action<int> DeveloperStageAdvanceRequested;
        public event Action<int> DeveloperFinalStageRewardRequested;

        public void Initialize(
            PuzzleCampaign targetCampaign,
            GridBoard targetBoard,
            PrototypeGameManager manager,
            LightingSequenceController lighting,
            GameObject originalCubeRoot)
        {
            // ? 참일 때 값
            // : 거짓일 때 값
            // targetCampaign이 존재한다면 → campaign에 targetCampaign을 넣는다.
            // targetCampaign이 null이라면 → 기존 campaign 값을 그대로 유지한다.
            campaign = targetCampaign != null ? targetCampaign : campaign;

            board = targetBoard;
            gameManager = manager;
            legacyCubeRoot = originalCubeRoot;
            spawnOrigin = legacyCubeRoot != null
                ? legacyCubeRoot.transform.position
                : board != null ? board.transform.position + board.transform.right : transform.position;

            BindLightingController(lighting);
            if (hasStarted && Application.isPlaying)
            {
                LoadStage(currentStageIndex);
            }
        }

        private void Awake()
        {
            // 스테이지 탐색 패널은 일반 플레이에서는 숨기고 F12 개발자 토글로만 연다.
            showStageUI = false;

            // 첫 렌더링 전에 숨겨 두고, 공개 스테이지를 불러올 때만 활성화한다.
            if (colorChart != null) colorChart.SetActive(false);

            if (campaign == null)
            {
                campaign = Resources.Load<PuzzleCampaign>(CampaignResourcePath);
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f12Key.wasPressedThisFrame)
            {
                showStageUI = !showStageUI;
            }
        }

        private void Start()
        {
            hasStarted = true;
            if (board == null) board = FindFirstObjectByType<GridBoard>();
            if (gameManager == null) gameManager = FindFirstObjectByType<PrototypeGameManager>();
            BindLightingController(
                lightingController != null
                    ? lightingController
                    : FindFirstObjectByType<LightingSequenceController>());

            if (legacyCubeRoot != null)
            {
                spawnOrigin = legacyCubeRoot.transform.position;
                legacyCubeRoot.SetActive(false);
            }

            currentStageIndex = Mathf.Clamp(startingStageIndex, 0, Mathf.Max(0, (campaign?.StageCount ?? 1) - 1));
            LoadStage(currentStageIndex);
        }

        private void OnDestroy()
        {
            if (lightingController != null)
            {
                lightingController.LightEffectsResolved -= HandleLightEffectsResolved;
            }
        }

        /// <summary>지정한 순서의 스테이지를 완전히 새 상태로 불러온다.</summary>
        public void LoadStage(int index)
        {
            if (campaign == null || campaign.StageCount == 0 || board == null)
            {
                Debug.LogWarning("StageManager: 캠페인 또는 GridBoard가 연결되지 않아 스테이지를 불러올 수 없습니다.", this);
                return;
            }

            currentStageIndex = Mathf.Clamp(index, 0, campaign.StageCount - 1);
            currentStage = campaign.GetStage(currentStageIndex);
            if (currentStage == null) return;

            UpdateColorChartVisibility();

            lightingController?.ResetToPlayerView();
            gameManager?.SetInteractionEnabled(false);
            ClearRuntimeCubes();
            board.ResetBoard();

            // SampleScene_Test에서 측정해 둔 기존 GridLines/Cubes_Invisible의 크기를 그대로 사용한다.
            // 스테이지 데이터가 논리 보드를 재설정하면 기존 검은 격자와 스냅 위치가 어긋나므로 변경하지 않는다.
            if (currentStage.GridSize.x != board.Width || currentStage.GridSize.y != board.Depth)
            {
                Debug.LogWarning(
                    $"{currentStage.DisplayName}: 스테이지 데이터 {currentStage.GridSize.x}x{currentStage.GridSize.y} 대신 " +
                    $"씬의 기존 격자 {board.Width}x{board.Depth}를 사용합니다.",
                    this);
            }
            gameManager?.ConfigureStage(currentStage);

            EnsureRuntimeRoot();
            ValidateReserveSlotAssignments(currentStage.CubeSpawns);
            foreach (StageCubeSpawn spawn in currentStage.CubeSpawns)
            {
                if (spawn.prefab == null) continue;

                Vector3 initialPosition = spawn.usesReserveSlot
                    ? GetReserveSlotWorldPosition(
                        spawn.reserveSide,
                        spawn.reserveSlot,
                        spawn.reserveLevel)
                    : spawnOrigin + spawn.localPosition;
                Quaternion initialRotation = spawn.usesReserveSlot
                    ? board.transform.rotation * Quaternion.Euler(spawn.localEulerAngles)
                    : Quaternion.Euler(spawn.localEulerAngles);

                GameObject cubeObject = Instantiate(
                    spawn.prefab,
                    initialPosition,
                    initialRotation,
                    runtimeCubeRoot);
                // 수동 제작된 Refractor를 포함해 모든 큐브의 프리팹 원본 비율을 그대로 유지한다.
                cubeObject.transform.localScale = spawn.prefab.transform.localScale;
                cubeObject.name = spawn.prefab.name;
                DraggableCube cube = cubeObject.GetComponent<DraggableCube>() ??
                                     cubeObject.AddComponent<DraggableCube>();
                cube.Initialize(board);
                // Reserve 큐브는 항상 고정하고, 보드 위 큐브는 스테이지 데이터에서 요청한 경우에만 고정한다.
                bool lockedOnBoard = spawn.startsOnBoard && spawn.lockOnBoard;
                bool interactionLocked = spawn.usesReserveSlot || lockedOnBoard;
                cube.SetInteractionLocked(interactionLocked);
                if (spawn.usesReserveSlot)
                {
                    board.RegisterExternalCube(cube);
                    CreateReserveSlotFrame(cubeObject, initialPosition);
                }
                if (spawn.startsOnBoard)
                {
                    cube.PlaceAtStageStart(spawn.boardCell);
                    if (lockedOnBoard && !spawn.usesReserveSlot)
                    {
                        // 초기 localPosition이 아니라 실제 격자 스냅 위치에 고정 표시 프레임을 씌운다.
                        CreateReserveSlotFrame(cubeObject, cubeObject.transform.position);
                    }
                }
            }

            stageSolved = false;
            gameManager?.SetInteractionEnabled(true);
            SoundManager.Play(loadingFromRestart
                ? SoundEventId.StageRestart
                : SoundEventId.StageLoad);
            Debug.Log($"스테이지 로드: {currentStageIndex + 1}. {currentStage.DisplayName}", this);
            StageLoaded?.Invoke(currentStageIndex);
        }

        /// <summary>
        /// 색 조합 소개 스테이지에 도달했거나 그 뒤 스테이지에서 시작하면 조합표를 공개한다.
        /// 한 번 공개된 뒤에는 이전 스테이지로 이동해도 플레이 세션 동안 계속 유지한다.
        /// </summary>
        private void UpdateColorChartVisibility()
        {
            if (colorChart == null || colorChartRevealStage == null || campaign == null) return;

            int revealStageIndex = -1;
            for (int index = 0; index < campaign.StageCount; index++)
            {
                if (campaign.GetStage(index) != colorChartRevealStage) continue;

                revealStageIndex = index;
                break;
            }

            if (revealStageIndex >= 0 && currentStageIndex >= revealStageIndex)
            {
                colorChartRevealed = true;
            }

            colorChart.SetActive(colorChartRevealed);
        }

        /// <summary>
        /// Player Camera에서 보이는 격자의 가상 바깥 한 칸을 Reserve 자리로 사용한다.
        /// Left/Right는 화면 좌우, Near/Far는 화면 아래/위 방향이다.
        /// </summary>
        private Vector3 GetReserveSlotWorldPosition(
            CubeReserveSide side,
            int requestedSlot,
            int requestedLevel)
        {
            GetPlayerViewAxes(out Vector3Int screenRight, out Vector3Int screenNear);
            Vector3Int edgeDirection = side switch
            {
                CubeReserveSide.Left => -screenRight,
                CubeReserveSide.Right => screenRight,
                CubeReserveSide.Far => -screenNear,
                _ => screenNear
            };
            Vector3Int slotDirection = side == CubeReserveSide.Left || side == CubeReserveSide.Right
                ? screenNear
                : screenRight;

            int edgeAxisCount = edgeDirection.x != 0 ? board.Width : board.Depth;
            int slotCount = slotDirection.x != 0 ? board.Width : board.Depth;
            // Inspector에서는 사람이 읽기 쉽게 1부터 센다. Left 2는 3칸짜리 왼쪽 변의 정중앙이다.
            int slot = Mathf.Clamp(requestedSlot, 1, Mathf.Max(1, slotCount)) - 1;
            float edgeDistance = (edgeAxisCount + 1) * 0.5f * board.CellSize;
            float slotOffset = (slot - (slotCount - 1) * 0.5f) * board.CellSize;
            Vector3 localPosition = (Vector3)edgeDirection * edgeDistance +
                                    (Vector3)slotDirection * slotOffset;
            // Inspector는 1층부터 세고, 논리 좌표는 0층부터 세므로 1을 뺀 뒤 기존 격자 높이식을 사용한다.
            int levelIndex = Mathf.Max(1, requestedLevel) - 1;
            float y = board.SurfaceY + board.CubeHeight * (levelIndex + 0.5f);
            localPosition.y = y;
            return board.transform.TransformPoint(localPosition);
        }

        /// <summary>실제로 같은 외부 3차원 좌표를 사용하는 Reserve 설정을 로드 전에 경고한다.</summary>
        private void ValidateReserveSlotAssignments(IReadOnlyList<StageCubeSpawn> spawns)
        {
            if (spawns == null || board == null) return;

            var occupiedPositions = new HashSet<Vector3Int>();
            foreach (StageCubeSpawn spawn in spawns)
            {
                if (!spawn.usesReserveSlot || spawn.prefab == null) continue;

                Vector3 worldPosition = GetReserveSlotWorldPosition(
                    spawn.reserveSide,
                    spawn.reserveSlot,
                    spawn.reserveLevel);
                Vector3Int gridPosition = board.WorldToGridCoordinate(worldPosition);
                if (occupiedPositions.Add(gridPosition)) continue;

                Debug.LogWarning(
                    $"{currentStage.DisplayName}: Reserve {spawn.reserveSide} " +
                    $"Slot {Mathf.Max(1, spawn.reserveSlot)}, Level {Mathf.Max(1, spawn.reserveLevel)}이 " +
                    "다른 Reserve 큐브와 겹칩니다.",
                    currentStage);
            }
        }

        private void GetPlayerViewAxes(out Vector3Int screenRight, out Vector3Int screenNear)
        {
            Camera camera = gameManager != null ? gameManager.PlayerCamera : Camera.main;
            if (camera == null)
            {
                screenRight = Vector3Int.right;
                screenNear = new Vector3Int(0, 0, -1);
                return;
            }

            Vector3 localCameraRight = board.transform.InverseTransformDirection(camera.transform.right);
            Vector3 localToCamera = board.transform.InverseTransformPoint(camera.transform.position);
            localCameraRight.y = 0f;
            localToCamera.y = 0f;

            if (Mathf.Abs(localCameraRight.x) >= Mathf.Abs(localCameraRight.z))
            {
                screenRight = localCameraRight.x >= 0f ? Vector3Int.right : Vector3Int.left;
                screenNear = localToCamera.z >= 0f
                    ? new Vector3Int(0, 0, 1)
                    : new Vector3Int(0, 0, -1);
            }
            else
            {
                screenRight = localCameraRight.z >= 0f
                    ? new Vector3Int(0, 0, 1)
                    : new Vector3Int(0, 0, -1);
                screenNear = localToCamera.x >= 0f ? Vector3Int.right : Vector3Int.left;
            }
        }

        /// <summary>
        /// 고정 Reserve 큐브와 같은 위치에 시각 전용 프레임을 생성한다.
        /// 프레임은 큐브와 별개로 보드 방향에 정렬하고, 입력과 퍼즐 판정을 방해하는 컴포넌트는 제거한다.
        /// </summary>
        private void CreateReserveSlotFrame(GameObject cubeObject, Vector3 worldPosition)
        {
            if (reserveSlotFramePrefab == null || runtimeCubeRoot == null) return;

            Quaternion frameRotation = board != null ? board.transform.rotation : Quaternion.identity;
            GameObject frame = Instantiate(
                reserveSlotFramePrefab,
                worldPosition,
                frameRotation,
                runtimeCubeRoot);
            frame.name = $"{cubeObject.name}_ReserveSlotFrame";

            // 제작 기준으로 사용된 발광 방향 표식은 실제 Reserve 큐브가 이미 가지고 있으므로 숨긴다.
            Transform opticMarkers = frame.transform.Find("OpticDirectionMarkers");
            if (opticMarkers != null) opticMarkers.gameObject.SetActive(false);

            foreach (Collider frameCollider in frame.GetComponentsInChildren<Collider>(true))
            {
                frameCollider.enabled = false;
            }

            foreach (Rigidbody frameBody in frame.GetComponentsInChildren<Rigidbody>(true))
            {
                frameBody.detectCollisions = false;
                frameBody.isKinematic = true;
                Destroy(frameBody);
            }

            foreach (PuzzleCubeProperties properties in
                     frame.GetComponentsInChildren<PuzzleCubeProperties>(true))
            {
                properties.enabled = false;
                Destroy(properties);
            }

            foreach (DraggableCube draggable in frame.GetComponentsInChildren<DraggableCube>(true))
            {
                draggable.enabled = false;
                Destroy(draggable);
            }
        }

        public void RestartStage()
        {
            loadingFromRestart = true;
            LoadStage(currentStageIndex);
            loadingFromRestart = false;
        }

        public void LoadNextStage()
        {
            if (campaign == null || currentStageIndex >= campaign.StageCount - 1) return;
            if (!allowUnlockedNavigation && !stageSolved) return;
            LoadStage(currentStageIndex + 1);
        }

        public void LoadPreviousStage()
        {
            if (currentStageIndex <= 0) return;
            LoadStage(currentStageIndex - 1);
        }

        private void HandleLightEffectsResolved()
        {
            if (currentStage == null || board == null) return;

            bool wasSolved = stageSolved;
            stageSolved = gameManager != null && gameManager.IsCurrentStageSolved;
            if (stageSolved)
            {
                int highestCompleted = PlayerPrefs.GetInt(ProgressKey, -1);
                if (currentStageIndex > highestCompleted)
                {
                    PlayerPrefs.SetInt(ProgressKey, currentStageIndex);
                    PlayerPrefs.Save();
                }

                if (!wasSolved)
                {
                    StageCleared?.Invoke(currentStageIndex);
                }
            }
        }

        private void BindLightingController(LightingSequenceController controller)
        {
            if (lightingController != null)
            {
                lightingController.LightEffectsResolved -= HandleLightEffectsResolved;
            }

            lightingController = controller;
            if (lightingController != null)
            {
                lightingController.LightEffectsResolved -= HandleLightEffectsResolved;
                lightingController.LightEffectsResolved += HandleLightEffectsResolved;
            }
        }

        private void EnsureRuntimeRoot()
        {
            if (runtimeCubeRoot != null) return;

            var root = new GameObject("RuntimeStageCubes");
            root.transform.SetParent(transform, false);
            runtimeCubeRoot = root.transform;
        }

        private void ClearRuntimeCubes()
        {
            if (runtimeCubeRoot == null) return;

            for (int index = runtimeCubeRoot.childCount - 1; index >= 0; index--)
            {
                GameObject child = runtimeCubeRoot.GetChild(index).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        private void OnGUI()
        {
            if (!showStageUI || currentStage == null || campaign == null) return;

            Rect area = new Rect(18f, 18f, 310f, 245f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.035f, 0.03f, 0.045f, 0.9f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 12f, area.width - 28f, area.height - 24f));
            GUILayout.Label($"STAGE {currentStageIndex + 1} / {campaign.StageCount}");
            GUILayout.Label(currentStage.DisplayName);
            GUILayout.Space(4f);
            GUILayout.Label($"목표 전등\n{currentStage.BuildTargetPreview()}");
            if (!string.IsNullOrWhiteSpace(currentStage.Hint)) GUILayout.Label(currentStage.Hint);
            if (stageSolved) GUILayout.Label("완성! 모든 전등이 올바르게 켜졌습니다.");
            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            GUI.enabled = currentStageIndex > 0;
            if (GUILayout.Button("이전"))
            {
                SoundManager.Play(SoundEventId.UiClick);
                LoadPreviousStage();
            }
            GUI.enabled = true;
            if (GUILayout.Button("다시 시작"))
            {
                SoundManager.Play(SoundEventId.UiClick);
                RestartStage();
            }
            GUI.enabled = allowUnlockedNavigation || stageSolved;
            if (GUILayout.Button("다음"))
            {
                SoundManager.Play(SoundEventId.UiClick);
                if (currentStageIndex < campaign.StageCount - 1)
                {
                    DeveloperStageAdvanceRequested?.Invoke(currentStageIndex);
                    LoadNextStage();
                }
                else
                {
                    // 마지막 스테이지에서는 이동하거나 자동 배치하지 않고 보상 KeyCube만 떨어뜨린다.
                    DeveloperFinalStageRewardRequested?.Invoke(currentStageIndex);
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
