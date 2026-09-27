#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace GameLab.Week4.Editor
{
    /// <summary>
    /// SampleScene_Test의 기존 오브젝트와 애셋을 보존한 채 Week4 프로토타입 규칙만 연결한다.
    /// 프리팹 원본은 수정하지 않고 테스트 씬의 인스턴스에만 컴포넌트와 설정을 추가한다.
    /// </summary>
    public static class SampleSceneTestIntegrator
    {
        private const string ScenePath = "Assets/Scenes/SampleScene_Test.unity";
        private const string GameplayRootName = "Week4Gameplay";
        private const string PanelCellMaterialPath = "Assets/Materials/CubeTypes/M_LightTargetStage_Cell.mat";
        private const string LampOffMaterialPath = "Assets/Materials/CubeTypes/M_LightTargetStage_LampOff.mat";
        private const int TargetGridWidth = 3;
        private const int TargetGridDepth = 5;

        [MenuItem("Tools/GameLab/Integrate Rules Into SampleScene Test")]
        public static void Integrate()
        {
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene targetScene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForIntegration = !targetScene.IsValid() || !targetScene.isLoaded;

            if (openedForIntegration)
            {
                targetScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }

            SceneManager.SetActiveScene(targetScene);
            GameObject existingGameplay = FindRoot(targetScene, GameplayRootName);
            if (existingGameplay != null)
            {
                Transform existingGridLines = FindTransform(targetScene, "GridLines");
                if (existingGridLines != null)
                {
                    StabilizeGridLines(existingGridLines);
                    EditorSceneManager.MarkSceneDirty(targetScene);
                }

                GridBoard existingBoard = existingGameplay.GetComponentInChildren<GridBoard>();
                Transform existingCubes = FindTransform(targetScene, "Cubes");
                if (existingBoard != null)
                {
                    existingBoard.Configure(
                        TargetGridWidth,
                        TargetGridDepth,
                        existingBoard.CellSize,
                        existingBoard.SurfaceY,
                        existingBoard.CubeHeight);
                    existingBoard.gameObject.name = "GridBoard_3x5";
                    ConfigureGridLineLayout(existingGridLines, existingBoard);
                }

                if (existingBoard != null && existingCubes != null)
                {
                    AddCubeControls(existingCubes, existingBoard);
                }

                RemoveLegacyStageEffects(targetScene, existingGameplay);
                EnsureLightingSequence(targetScene, existingGameplay);
                EnsureStageSystem(targetScene, existingGameplay);

                Finish(targetScene, previousActiveScene, openedForIntegration);
                return;
            }

            Transform gridLines = FindTransform(targetScene, "GridLines");
            Transform invisibleCells = FindTransform(targetScene, "Cubes_Invisible");
            Transform movableCubes = FindTransform(targetScene, "Cubes");
            Camera playerCamera = FindTransform(targetScene, "Player Camera")?.GetComponent<Camera>();

            if (gridLines == null || invisibleCells == null || movableCubes == null || playerCamera == null)
            {
                Debug.LogError("SampleScene_Test 통합 실패: GridLines, Cubes_Invisible, Cubes, Player Camera가 필요합니다.");
                Finish(targetScene, previousActiveScene, openedForIntegration);
                return;
            }

            GameObject gameplayRoot = new GameObject(GameplayRootName);
            SceneManager.MoveGameObjectToScene(gameplayRoot, targetScene);

            List<Transform> cellMarkers = invisibleCells.Cast<Transform>().ToList();
            float cubeHeight = MeasureMovableCubeHeight(movableCubes);
            Vector3 boardCenter = MeasureBoardCenter(cellMarkers, cubeHeight);
            int width = TargetGridWidth;
            int depth = TargetGridDepth;
            float cellSize = MeasureCellSize(cellMarkers);

            GameObject boardObject = new GameObject($"GridBoard_{width}x{depth}");
            boardObject.transform.SetParent(gameplayRoot.transform);
            boardObject.transform.position = boardCenter;
            GridBoard board = boardObject.AddComponent<GridBoard>();
            board.Configure(width, depth, cellSize, 0f, cubeHeight);

            ConfigureGridLineLayout(gridLines, board);
            StabilizeGridLines(gridLines);

            // 보이지 않는 기존 셀 마커는 위치 측정용으로 남기고 물리 충돌만 차단한다.
            foreach (Transform marker in cellMarkers)
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

            // 기존 큐브 인스턴스에만 조작 기능을 추가해 프리팹 원본은 그대로 보존한다.
            AddCubeControls(movableCubes, board);

            ConfigureCameras(targetScene, playerCamera);

            int[] targetProjection = BuildTargetProjection(width, movableCubes.childCount);
            PrototypeGameManager manager = gameplayRoot.AddComponent<PrototypeGameManager>();
            manager.Initialize(board, playerCamera, targetProjection);
            manager.SetInstructionOverlayVisible(false);

            RemoveLegacyStageEffects(targetScene, gameplayRoot);
            EnsureLightingSequence(targetScene, gameplayRoot);
            EnsureStageSystem(targetScene, gameplayRoot);

            EditorSceneManager.MarkSceneDirty(targetScene);
            Finish(targetScene, previousActiveScene, openedForIntegration);
            Debug.Log($"SampleScene_Test 규칙 통합 완료: {width}x{depth}, 큐브 {movableCubes.childCount}개");
        }

        /// <summary>Input Actions와 두 카메라를 새 전등 퍼즐의 Space 연출에 연결한다.</summary>
        private static void EnsureLightingSequence(Scene scene, GameObject gameplayRoot)
        {
            InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/InputSystem_Actions.inputactions");
            Camera playerCamera = FindTransform(scene, "Player Camera")?.GetComponent<Camera>();
            Camera stageCamera = FindTransform(scene, "Stage Camera")?.GetComponent<Camera>();
            PrototypeGameManager manager = gameplayRoot.GetComponent<PrototypeGameManager>();

            LightingSequenceController controller = gameplayRoot.GetComponent<LightingSequenceController>() ??
                                                    gameplayRoot.AddComponent<LightingSequenceController>();
            controller.Initialize(inputActions, playerCamera, stageCamera, manager);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>이전 그림자 실험용 Stage 전용 조명과 모형을 씬에서 제거한다.</summary>
        private static void RemoveLegacyStageEffects(Scene scene, GameObject gameplayRoot)
        {
            Transform spotLight = FindTransform(scene, "Spot Light");
            if (spotLight != null)
            {
                UnityEngine.Object.DestroyImmediate(spotLight.gameObject);
            }

            Transform shadowModels = FindTransform(scene, "Blocks");
            if (shadowModels != null)
            {
                UnityEngine.Object.DestroyImmediate(shadowModels.gameObject);
            }

            foreach (FakeShadowDisplay display in gameplayRoot.GetComponents<FakeShadowDisplay>())
            {
                UnityEngine.Object.DestroyImmediate(display);
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>생성된 캠페인 애셋과 공용 StageManager를 SampleScene_Test에 저장한다.</summary>
        private static void EnsureStageSystem(Scene scene, GameObject gameplayRoot)
        {
            PuzzleCampaign campaign = AssetDatabase.LoadAssetAtPath<PuzzleCampaign>(
                "Assets/Resources/Stages/PuzzleCampaign.asset");
            GridBoard board = gameplayRoot.GetComponentInChildren<GridBoard>();
            PrototypeGameManager manager = gameplayRoot.GetComponent<PrototypeGameManager>();
            LightingSequenceController lighting = gameplayRoot.GetComponent<LightingSequenceController>();
            GameObject legacyCubes = FindTransform(scene, "Cubes")?.gameObject;
            if (board == null || manager == null || lighting == null) return;

            // 전등판을 런타임 생성물로만 두지 않고 씬에 저장해 Scene/Game 뷰에서 항상 확인한다.
            LightPuzzleVisualizer visualizer = gameplayRoot.GetComponent<LightPuzzleVisualizer>() ??
                                               gameplayRoot.AddComponent<LightPuzzleVisualizer>();
            Material cellMaterial = EnsurePreviewMaterial(
                PanelCellMaterialPath,
                new Color(0.045f, 0.055f, 0.07f, 1f),
                Color.black);
            Material lampMaterial = EnsurePreviewMaterial(
                LampOffMaterialPath,
                new Color(0.16f, 0.16f, 0.15f, 1f),
                new Color(0.025f, 0.025f, 0.022f, 1f));
            visualizer.Initialize(board);
            visualizer.ConfigureMaterials(cellMaterial, lampMaterial);
            if (campaign != null && campaign.StageCount > 0)
            {
                visualizer.ConfigureStage(campaign.GetStage(0));
                visualizer.SetBeamsVisible(false);
            }

            StageManager stageManager = gameplayRoot.GetComponent<StageManager>() ??
                                        gameplayRoot.AddComponent<StageManager>();
            stageManager.Initialize(campaign, board, manager, lighting, legacyCubes);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static Material EnsurePreviewMaterial(
            string assetPath,
            Color baseColor,
            Color emissionColor)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material != null) return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;

            material = new Material(shader)
            {
                name = System.IO.Path.GetFileNameWithoutExtension(assetPath),
                color = baseColor
            };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", emissionColor);
                if (emissionColor.maxColorComponent > 0f) material.EnableKeyword("_EMISSION");
            }

            AssetDatabase.CreateAsset(material, assetPath);
            return material;
        }

        /// <summary>격자선의 렌더러는 유지하고 씬 인스턴스의 낙하 물리와 클릭 충돌만 끈다.</summary>
        private static void StabilizeGridLines(Transform gridLines)
        {
            foreach (Rigidbody body in gridLines.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
            }

            foreach (Collider collider in gridLines.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
        }

        /// <summary>기존 GridLine 인스턴스를 재사용해 가로 3칸, 깊이 5칸의 선 10개를 구성한다.</summary>
        private static void ConfigureGridLineLayout(Transform gridLines, GridBoard board)
        {
            if (gridLines == null || board == null) return;

            List<Transform> lines = gridLines.Cast<Transform>().ToList();
            int horizontalLineCount = TargetGridDepth + 1;
            int verticalLineCount = TargetGridWidth + 1;
            int requiredLineCount = horizontalLineCount + verticalLineCount;
            if (lines.Count < requiredLineCount)
            {
                Debug.LogWarning($"GridLines에 선이 {requiredLineCount}개보다 적어 3x5 격자를 완성할 수 없습니다.");
                return;
            }

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

                float thickness = 0.01f;
                float height = Mathf.Max(0.01f, Mathf.Abs(line.localScale.y));
                if (index < horizontalLineCount)
                {
                    float z = localCenter.z - TargetGridDepth * localCellSize * 0.5f + index * localCellSize;
                    line.localPosition = new Vector3(localCenter.x, lineY, z);
                    line.localRotation = Quaternion.identity;
                    line.localScale = new Vector3(TargetGridWidth * localCellSize, height, thickness);
                }
                else
                {
                    int column = index - horizontalLineCount;
                    float x = localCenter.x - TargetGridWidth * localCellSize * 0.5f + column * localCellSize;
                    line.localPosition = new Vector3(x, lineY, localCenter.z);
                    line.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    line.localScale = new Vector3(TargetGridDepth * localCellSize, height, thickness);
                }

                foreach (Renderer renderer in line.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = true;
                }
            }
        }

        private static void ConfigureCameras(Scene scene, Camera playerCamera)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    bool isPlayerCamera = camera == playerCamera;
                    camera.enabled = isPlayerCamera;
                    camera.gameObject.tag = isPlayerCamera ? "MainCamera" : "Untagged";

                    AudioListener listener = camera.GetComponent<AudioListener>();
                    if (listener != null)
                    {
                        listener.enabled = isPlayerCamera;
                    }
                }
            }
        }

        private static int[] BuildTargetProjection(int width, int cubeCount)
        {
            var target = new int[width];
            int remaining = cubeCount;
            int layer = 0;
            while (remaining > 0)
            {
                int column = layer % 2 == 0 ? layer / 2 : width - 1 - layer / 2;
                column = Mathf.Clamp(column, 0, width - 1);
                target[column]++;
                remaining--;
                layer = (layer + 1) % Mathf.Max(1, width * 2);
            }

            return target;
        }

        private static PuzzleCubeType InferCubeType(string objectName)
        {
            string normalized = objectName.ToLowerInvariant();
            if (normalized.Contains("splitter") || normalized.Contains("분광") || normalized.Contains("분기"))
                return PuzzleCubeType.LightSplitter;
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

        private static Vector3 MeasureBoardCenter(List<Transform> markers, float cubeHeight)
        {
            float centerX = (markers.Min(marker => marker.position.x) + markers.Max(marker => marker.position.x)) * 0.5f;
            float centerZ = (markers.Min(marker => marker.position.z) + markers.Max(marker => marker.position.z)) * 0.5f;
            Transform firstMovable = FindTransform(SceneManager.GetActiveScene(), "Cubes")?.Cast<Transform>().FirstOrDefault();
            float surfaceY = firstMovable != null ? firstMovable.position.y - cubeHeight * 0.5f : markers.Average(marker => marker.position.y);
            return new Vector3(centerX, surfaceY, centerZ);
        }

        private static float MeasureMovableCubeHeight(Transform movableCubes)
        {
            Renderer renderer = movableCubes.GetComponentsInChildren<Renderer>(true).FirstOrDefault();
            return renderer != null ? Mathf.Max(0.1f, renderer.bounds.size.y) : 0.2f;
        }

        private static int CountDistinct(IEnumerable<float> values)
        {
            return values.Select(value => Mathf.RoundToInt(value * 1000f)).Distinct().Count();
        }

        private static float MeasureCellSize(List<Transform> markers)
        {
            List<float> xs = markers.Select(marker => marker.position.x).OrderBy(value => value).Distinct().ToList();
            List<float> zs = markers.Select(marker => marker.position.z).OrderBy(value => value).Distinct().ToList();
            var gaps = new List<float>();
            for (int index = 1; index < xs.Count; index++) gaps.Add(xs[index] - xs[index - 1]);
            for (int index = 1; index < zs.Count; index++) gaps.Add(zs[index] - zs[index - 1]);
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

        private static GameObject FindRoot(Scene scene, string objectName)
        {
            return scene.GetRootGameObjects().FirstOrDefault(root => root.name == objectName);
        }

        private static void Finish(Scene targetScene, Scene previousScene, bool closeAfterSave)
        {
            EditorSceneManager.SaveScene(targetScene, ScenePath);
            if (previousScene.IsValid() && previousScene.isLoaded && previousScene != targetScene)
            {
                SceneManager.SetActiveScene(previousScene);
            }

            if (closeAfterSave)
            {
                EditorSceneManager.CloseScene(targetScene, true);
            }
        }
    }
}
#endif
