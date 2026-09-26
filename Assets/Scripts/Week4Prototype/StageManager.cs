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
            board.Configure(
                currentStage.GridSize.x,
                currentStage.GridSize.y,
                board.CellSize,
                board.SurfaceY,
                board.CubeHeight);

            EnsureRuntimeRoot();
            foreach (StageCubeSpawn spawn in currentStage.CubeSpawns)
            {
                if (spawn.prefab == null) continue;

                GameObject cubeObject = Instantiate(
                    spawn.prefab,
                    spawnOrigin + spawn.localPosition,
                    Quaternion.Euler(spawn.localEulerAngles),
                    runtimeCubeRoot);
                cubeObject.name = spawn.prefab.name;
                DraggableCube cube = cubeObject.GetComponent<DraggableCube>() ??
                                     cubeObject.AddComponent<DraggableCube>();
                cube.Initialize(board);
            }

            stageSolved = false;
            gameManager?.SetInteractionEnabled(true);
            Debug.Log($"스테이지 로드: {currentStageIndex + 1}. {currentStage.DisplayName}", this);
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

            stageSolved = currentStage.Matches(board);
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
            GUILayout.Label($"목표 그림자\n{currentStage.BuildTargetPreview()}");
            if (!string.IsNullOrWhiteSpace(currentStage.Hint)) GUILayout.Label(currentStage.Hint);
            if (stageSolved) GUILayout.Label("완성! 목표 그림자와 일치합니다.");
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
