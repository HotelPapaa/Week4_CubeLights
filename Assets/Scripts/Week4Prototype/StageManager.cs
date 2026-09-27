using UnityEngine;

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

        [Header("프로토타입 탐색")]
        [SerializeField] private bool showStageUI = true;
        [SerializeField] private bool allowUnlockedNavigation = true;

        private Transform runtimeCubeRoot;
        private Vector3 spawnOrigin;
        private PuzzleStageDefinition currentStage;
        private int currentStageIndex;
        private bool stageSolved;
        private bool hasStarted;

        public PuzzleStageDefinition CurrentStage => currentStage;
        public int CurrentStageIndex => currentStageIndex;
        public bool StageSolved => stageSolved;

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
            if (campaign == null)
            {
                campaign = Resources.Load<PuzzleCampaign>(CampaignResourcePath);
            }
        }

        private void Start()
        {
            hasStarted = true;
            if (board == null) board = FindFirstObjectByType<GridBoard>();
            if (gameManager == null) gameManager = FindFirstObjectByType<PrototypeGameManager>();
            if (lightingController == null) BindLightingController(FindFirstObjectByType<LightingSequenceController>());

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
            foreach (StageCubeSpawn spawn in currentStage.CubeSpawns)
            {
                if (spawn.prefab == null) continue;

                Vector3 initialPosition = spawn.usesReserveSlot
                    ? GetReserveSlotWorldPosition(spawn.reserveSide, spawn.reserveSlot)
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
                cube.SetInteractionLocked(spawn.usesReserveSlot);
                if (spawn.usesReserveSlot)
                {
                    board.RegisterExternalCube(cube);
                }
                if (spawn.startsOnBoard)
                {
                    cube.PlaceAtStageStart(spawn.boardCell);
                }
            }

            stageSolved = false;
            gameManager?.SetInteractionEnabled(true);
            Debug.Log($"스테이지 로드: {currentStageIndex + 1}. {currentStage.DisplayName}", this);
        }

        /// <summary>
        /// Player Camera에서 보이는 격자의 가상 바깥 한 칸을 Reserve 자리로 사용한다.
        /// Left/Right는 화면 좌우, Near/Far는 화면 아래/위 방향이다.
        /// </summary>
        private Vector3 GetReserveSlotWorldPosition(CubeReserveSide side, int requestedSlot)
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
            float y = board.SurfaceY + board.CubeHeight * 0.5f;
            localPosition.y = y;
            return board.transform.TransformPoint(localPosition);
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

        public void RestartStage()
        {
            LoadStage(currentStageIndex);
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

            stageSolved = gameManager != null && gameManager.IsCurrentStageSolved;
            if (stageSolved)
            {
                int highestCompleted = PlayerPrefs.GetInt(ProgressKey, -1);
                if (currentStageIndex > highestCompleted)
                {
                    PlayerPrefs.SetInt(ProgressKey, currentStageIndex);
                    PlayerPrefs.Save();
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
            if (GUILayout.Button("이전")) LoadPreviousStage();
            GUI.enabled = true;
            if (GUILayout.Button("다시 시작")) RestartStage();
            GUI.enabled = currentStageIndex < campaign.StageCount - 1 && (allowUnlockedNavigation || stageSolved);
            if (GUILayout.Button("다음")) LoadNextStage();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
