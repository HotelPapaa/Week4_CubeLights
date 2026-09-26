using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameLab.Week4
{
    /// <summary>
    /// SampleScene_Test를 실행할 때 기존 오브젝트를 찾아 게임 규칙을 자동 연결한다.
    /// 씬이나 프리팹 원본을 복제/변형하지 않고 런타임 인스턴스에만 적용하는 안전망이다.
    /// </summary>
    public static class SampleSceneTestRuntimeBootstrap
    {
        private const string TargetSceneName = "SampleScene_Test";
        private const string GameplayRootName = "Week4Gameplay";
        private const int TargetGridWidth = 3;
        private const int TargetGridDepth = 5;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != TargetSceneName)
            {
                return;
            }

            // 이미 규칙이 씬에 저장되어 있어도 기존 격자선 물리는 항상 다시 고정한다.
            Transform gridLines = FindTransform(scene, "GridLines");
            if (gridLines != null)
            {
                StabilizeGridLines(gridLines);
            }

            Transform existingGameplay = FindTransform(scene, GameplayRootName);
            if (existingGameplay != null)
            {
                GridBoard existingBoard = existingGameplay.GetComponentInChildren<GridBoard>();
                Transform existingCubes = FindTransform(scene, "Cubes");
                if (existingBoard != null)
                {
                    existingBoard.Configure(
                        TargetGridWidth,
                        TargetGridDepth,
                        existingBoard.CellSize,
                        existingBoard.SurfaceY,
                        existingBoard.CubeHeight);
                    existingBoard.gameObject.name = "GridBoard_3x5";
                    ConfigureGridLineLayout(gridLines, existingBoard);
                }

                if (existingBoard != null && existingCubes != null)
                {
                    AddCubeControls(existingCubes, existingBoard);
                }

                RemoveLegacyStageEffects(scene, existingGameplay.gameObject);
                EnsureLightingSequence(scene, existingGameplay.gameObject);
                EnsureStageSystem(scene, existingGameplay.gameObject);
                return;
            }

            Transform invisibleCells = FindTransform(scene, "Cubes_Invisible");
            Transform movableCubes = FindTransform(scene, "Cubes");
            Camera playerCamera = FindTransform(scene, "Player Camera")?.GetComponent<Camera>();

            if (invisibleCells == null || gridLines == null || movableCubes == null || playerCamera == null)
            {
                Debug.LogError("SampleScene_Test 런타임 통합 실패: 필수 오브젝트를 찾지 못했습니다.");
                return;
            }

            GameObject gameplayRoot = new GameObject(GameplayRootName);
            List<Transform> markers = invisibleCells.Cast<Transform>().ToList();
            float cubeHeight = MeasureCubeHeight(movableCubes);
            Vector3 boardCenter = MeasureBoardCenter(markers, movableCubes, cubeHeight);
            int width = TargetGridWidth;
            int depth = TargetGridDepth;
            float cellSize = MeasureCellSize(markers);

            GameObject boardObject = new GameObject($"GridBoard_{width}x{depth}");
            boardObject.transform.SetParent(gameplayRoot.transform);
            boardObject.transform.position = boardCenter;
            GridBoard board = boardObject.AddComponent<GridBoard>();
            board.Configure(width, depth, cellSize, 0f, cubeHeight);

            ConfigureGridLineLayout(gridLines, board);
            StabilizeGridLines(gridLines);
            DisableMarkerPhysics(markers);
            AddCubeControls(movableCubes, board);
            ConfigureCameras(scene, playerCamera);

            int[] target = BuildTargetProjection(width, movableCubes.childCount);
            PrototypeGameManager manager = gameplayRoot.AddComponent<PrototypeGameManager>();
            manager.Initialize(board, playerCamera, target);
            manager.SetInstructionOverlayVisible(false);

            RemoveLegacyStageEffects(scene, gameplayRoot);
            EnsureLightingSequence(scene, gameplayRoot);
            EnsureStageSystem(scene, gameplayRoot);

            Debug.Log($"SampleScene_Test 런타임 규칙 연결 완료: {width}x{depth}, 큐브 {movableCubes.childCount}개");
        }

        /// <summary>기존 통합 씬에도 Space 조명 연출 컨트롤러를 빠짐없이 연결한다.</summary>
        private static void EnsureLightingSequence(Scene scene, GameObject gameplayRoot)
        {
            Camera playerCamera = FindTransform(scene, "Player Camera")?.GetComponent<Camera>();
            Camera stageCamera = FindTransform(scene, "Stage Camera")?.GetComponent<Camera>();
            PrototypeGameManager manager = gameplayRoot.GetComponent<PrototypeGameManager>();

            LightingSequenceController controller = gameplayRoot.GetComponent<LightingSequenceController>() ??
                                                    gameplayRoot.AddComponent<LightingSequenceController>();
            controller.Initialize(null, playerCamera, stageCamera, manager);
        }

        /// <summary>에디터 통합 전 상태로 실행해도 이전 Stage 조명과 그림자 모형을 남기지 않는다.</summary>
        private static void RemoveLegacyStageEffects(Scene scene, GameObject gameplayRoot)
        {
            Transform spotLight = FindTransform(scene, "Spot Light");
            if (spotLight != null)
            {
                Object.Destroy(spotLight.gameObject);
            }

            Transform shadowModels = FindTransform(scene, "Blocks");
            if (shadowModels != null)
            {
                Object.Destroy(shadowModels.gameObject);
            }

            foreach (FakeShadowDisplay display in gameplayRoot.GetComponents<FakeShadowDisplay>())
            {
                Object.Destroy(display);
            }
        }

        /// <summary>공용 씬에 데이터 기반 스테이지 로더를 연결한다. 캠페인은 Resources에서 자동 로드된다.</summary>
        private static void EnsureStageSystem(Scene scene, GameObject gameplayRoot)
        {
            GridBoard board = gameplayRoot.GetComponentInChildren<GridBoard>();
            PrototypeGameManager manager = gameplayRoot.GetComponent<PrototypeGameManager>();
            LightingSequenceController lighting = gameplayRoot.GetComponent<LightingSequenceController>();
            GameObject legacyCubes = FindTransform(scene, "Cubes")?.gameObject;
            if (board == null || manager == null || lighting == null) return;

            StageManager stageManager = gameplayRoot.GetComponent<StageManager>() ??
                                        gameplayRoot.AddComponent<StageManager>();
            stageManager.Initialize(null, board, manager, lighting, legacyCubes);
        }

        /// <summary>
        /// 기존 GridLine 프리팹의 중력 Rigidbody가 Play 시작과 함께 떨어지지 않도록
        /// 테스트 씬의 런타임 인스턴스만 고정한다. 렌더러는 그대로 유지한다.
        /// </summary>
        private static void StabilizeGridLines(Transform gridLines)
        {
            foreach (Rigidbody body in gridLines.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
            }

            // 격자선 콜라이더가 큐브 선택 광선을 가로막지 않게 한다.
            foreach (Collider collider in gridLines.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
        }

        /// <summary>기존 선 오브젝트 10개를 3x5 격자로 재배치하고 남는 선은 숨긴다.</summary>
        private static void ConfigureGridLineLayout(Transform gridLines, GridBoard board)
        {
            if (gridLines == null || board == null) return;

            List<Transform> lines = gridLines.Cast<Transform>().ToList();
            int horizontalLineCount = TargetGridDepth + 1;
            int verticalLineCount = TargetGridWidth + 1;
            int requiredLineCount = horizontalLineCount + verticalLineCount;
            if (lines.Count < requiredLineCount) return;

            float rootScale = Mathf.Max(0.0001f, Mathf.Abs(gridLines.lossyScale.x));
            float localCellSize = board.CellSize / rootScale;
            Vector3 localCenter = gridLines.InverseTransformPoint(board.transform.position);
            float lineY = lines[0].localPosition.y;

            for (int index = 0; index < lines.Count; index++)
            {
                Transform line = lines[index];
                bool active = index < requiredLineCount;
                line.gameObject.SetActive(active);
                if (!active) continue;

                float height = Mathf.Max(0.01f, Mathf.Abs(line.localScale.y));
                if (index < horizontalLineCount)
                {
                    float z = localCenter.z - TargetGridDepth * localCellSize * 0.5f + index * localCellSize;
                    line.localPosition = new Vector3(localCenter.x, lineY, z);
                    line.localRotation = Quaternion.identity;
                    line.localScale = new Vector3(TargetGridWidth * localCellSize, height, 0.01f);
                }
                else
                {
                    int column = index - horizontalLineCount;
                    float x = localCenter.x - TargetGridWidth * localCellSize * 0.5f + column * localCellSize;
                    line.localPosition = new Vector3(x, lineY, localCenter.z);
                    line.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    line.localScale = new Vector3(TargetGridDepth * localCellSize, height, 0.01f);
                }

                foreach (Renderer renderer in line.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = true;
                }
            }
        }

        private static void DisableMarkerPhysics(List<Transform> markers)
        {
            foreach (Transform marker in markers)
            {
                foreach (Collider collider in marker.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                }

                foreach (Rigidbody body in marker.GetComponentsInChildren<Rigidbody>(true))
                {
                    body.isKinematic = true;
                    body.useGravity = false;
                }
            }
        }

        private static void AddCubeControls(Transform cubesRoot, GridBoard board)
        {
            foreach (Transform cubeTransform in cubesRoot.Cast<Transform>())
            {
                DraggableCube cube = cubeTransform.GetComponent<DraggableCube>() ??
                                     cubeTransform.gameObject.AddComponent<DraggableCube>();
                cube.Initialize(board);

                if (!cubeTransform.TryGetComponent(out PuzzleCubeProperties _))
                {
                    PuzzleCubeProperties properties = cubeTransform.gameObject.AddComponent<PuzzleCubeProperties>();
                    PuzzleCubeType type = InferCubeType(cubeTransform.name);
                    properties.ConfigureOptics(type, InferLightColor(cubeTransform.name, type));
                }
            }
        }

        private static PuzzleCubeType InferCubeType(string objectName)
        {
            string normalized = objectName.ToLowerInvariant();
            if (normalized.Contains("emitter") || normalized.Contains("발광")) return PuzzleCubeType.LightEmitter;
            if (normalized.Contains("refractor") || normalized.Contains("굴절")) return PuzzleCubeType.Refractor;
            if (normalized.Contains("coloredglass") || normalized.Contains("색유리")) return PuzzleCubeType.ColoredGlass;
            if (normalized.Contains("glass") || normalized.Contains("유리")) return PuzzleCubeType.Glass;
            if (normalized.Contains("ice") || normalized.Contains("얼음")) return PuzzleCubeType.Ice;
            if (normalized.Contains("brittle") || normalized.Contains("바사삭")) return PuzzleCubeType.Brittle;
            if (normalized.Contains("styrofoam") || normalized.Contains("스티로폼")) return PuzzleCubeType.Styrofoam;
            return PuzzleCubeType.Normal;
        }

        private static PuzzleLightColor InferLightColor(string objectName, PuzzleCubeType type)
        {
            if (type != PuzzleCubeType.ColoredGlass) return PuzzleLightColor.White;
            string normalized = objectName.ToLowerInvariant();
            if (normalized.Contains("red") || normalized.Contains("빨강")) return PuzzleLightColor.Red;
            if (normalized.Contains("yellow") || normalized.Contains("노랑")) return PuzzleLightColor.Yellow;
            return PuzzleLightColor.Blue;
        }

        private static void ConfigureCameras(Scene scene, Camera playerCamera)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    bool isPlayer = camera == playerCamera;
                    camera.enabled = isPlayer;
                    camera.gameObject.tag = isPlayer ? "MainCamera" : "Untagged";

                    AudioListener listener = camera.GetComponent<AudioListener>();
                    if (listener != null) listener.enabled = isPlayer;
                }
            }
        }

        private static int[] BuildTargetProjection(int width, int cubeCount)
        {
            var target = new int[width];
            int remaining = cubeCount;
            int step = 0;
            while (remaining-- > 0)
            {
                int column = step % 2 == 0 ? step / 2 : width - 1 - step / 2;
                target[Mathf.Clamp(column, 0, width - 1)]++;
                step = (step + 1) % Mathf.Max(1, width * 2);
            }

            return target;
        }

        private static float MeasureCubeHeight(Transform cubesRoot)
        {
            Renderer renderer = cubesRoot.GetComponentsInChildren<Renderer>(true).FirstOrDefault();
            return renderer != null ? Mathf.Max(0.1f, renderer.bounds.size.y) : 0.2f;
        }

        private static Vector3 MeasureBoardCenter(
            List<Transform> markers,
            Transform cubesRoot,
            float cubeHeight)
        {
            float x = (markers.Min(marker => marker.position.x) + markers.Max(marker => marker.position.x)) * 0.5f;
            float z = (markers.Min(marker => marker.position.z) + markers.Max(marker => marker.position.z)) * 0.5f;
            Transform firstCube = cubesRoot.Cast<Transform>().FirstOrDefault();
            float y = firstCube != null ? firstCube.position.y - cubeHeight * 0.5f : markers.Average(marker => marker.position.y);
            return new Vector3(x, y, z);
        }

        private static int CountDistinct(IEnumerable<float> values)
        {
            return values.Select(value => Mathf.RoundToInt(value * 1000f)).Distinct().Count();
        }

        private static float MeasureCellSize(List<Transform> markers)
        {
            List<int> xs = markers.Select(marker => Mathf.RoundToInt(marker.position.x * 1000f)).Distinct().OrderBy(value => value).ToList();
            List<int> zs = markers.Select(marker => Mathf.RoundToInt(marker.position.z * 1000f)).Distinct().OrderBy(value => value).ToList();
            var gaps = new List<float>();
            for (int index = 1; index < xs.Count; index++) gaps.Add((xs[index] - xs[index - 1]) / 1000f);
            for (int index = 1; index < zs.Count; index++) gaps.Add((zs[index] - zs[index - 1]) / 1000f);
            return gaps.Count > 0 ? gaps.Where(gap => gap > 0.001f).Average() : 0.2f;
        }

        private static Transform FindTransform(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindRecursive(root.transform, objectName);
                if (found != null) return found;
            }

            return null;
        }

        private static Transform FindRecursive(Transform current, string objectName)
        {
            if (current.name == objectName) return current;
            foreach (Transform child in current)
            {
                Transform found = FindRecursive(child, objectName);
                if (found != null) return found;
            }

            return null;
        }
    }
}
