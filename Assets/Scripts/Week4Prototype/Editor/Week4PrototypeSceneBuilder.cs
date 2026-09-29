//


#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameLab.Week4.Editor
{
    /// <summary>기존 애셋을 재사용해 독립적인 4주차 프로토타입 씬을 자동 생성한다.</summary>
    public static class Week4PrototypeSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Week4_Prototype.unity";
        private const int GridWidth = 5;
        private const int GridDepth = 3;
        private const float CellSize = 1.15f;

        /// <summary>
        /// 스크립트 컴파일이 끝났을 때 프로토타입 씬이 아직 없다면 한 번만 자동 생성한다.
        /// 기존에 열어 둔 씬은 유지하고 새 씬을 Additive 방식으로 잠시 열어 저장한다.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void QueueFirstBuild()
        {
            // Domain reloads can also happen while entering Play Mode. Scene-authoring
            // APIs such as EditorSceneManager.NewScene cannot be used at that time.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                EditorApplication.delayCall += BuildScene;
            }
        }

        [MenuItem("Tools/GameLab/Build Week 4 Prototype Scene")]
        public static void BuildScene()
        {
            // A delayed callback may have been queued immediately before Play Mode
            // started, so guard the execution point as well as the scheduling point.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("플레이 모드 중에는 4주차 프로토타입 씬을 생성할 수 없습니다.");
                return;
            }

            // 현재 작업 중인 씬을 보존하고, 새 씬을 추가 모드로 생성해 그 안에만 오브젝트를 만든다.
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            Material gridMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Gridlines.mat");
            Material cubeMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Cubes.mat");
            Material blackMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_TrueBlack.mat");
            GameObject cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cube.prefab");

            CreateLighting();
            CreateTable();
            Camera playerCamera = CreatePlayerCamera();
            GridBoard board = CreateGrid(gridMaterial);
            CreateMovableCubes(board, cubePrefab, cubeMaterial);
            CreateShadowScreen(board, blackMaterial);
            CreateTargetPaper(blackMaterial);
            CreateGameManager(board, playerCamera);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            SceneManager.SetActiveScene(previousActiveScene);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"4주차 프로토타입 씬 생성 완료: {ScenePath}");
        }

        private static void CreateLighting()
        {
            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.91f, 0.78f);
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        }

        private static void CreateTable()
        {
            GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Table";
            table.transform.SetPositionAndRotation(new Vector3(0f, -0.25f, -0.6f), Quaternion.identity);
            table.transform.localScale = new Vector3(12f, 0.4f, 10f);
            SetColor(table, new Color(0.24f, 0.12f, 0.055f));
        }

        private static Camera CreatePlayerCamera()
        {
            GameObject cameraObject = new GameObject("Player Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.transform.position = new Vector3(0f, 7.7f, -10.5f);
            camera.transform.LookAt(new Vector3(0f, 1.1f, 0.7f));
            camera.fieldOfView = 52f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.045f, 0.06f);
            return camera;
        }

        private static GridBoard CreateGrid(Material gridMaterial)
        {
            GameObject boardObject = new GameObject("GridBoard_5x3");
            GridBoard board = boardObject.AddComponent<GridBoard>();
            board.Configure(GridWidth, GridDepth, CellSize, 0.08f, 1f);

            GameObject cellsRoot = new GameObject("GridCells");
            cellsRoot.transform.SetParent(boardObject.transform);
            for (int row = 0; row < GridDepth; row++)
            {
                for (int column = 0; column < GridWidth; column++)
                {
                    GameObject cell = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cell.name = $"GridCell_{column}_{row}";
                    cell.transform.SetParent(cellsRoot.transform);
                    cell.transform.position = board.CellToWorld(new Vector2Int(column, row), 0) + Vector3.down * 0.48f;
                    cell.transform.localScale = new Vector3(CellSize * 0.92f, 0.035f, CellSize * 0.92f);
                    Object.DestroyImmediate(cell.GetComponent<Collider>());
                    ApplyMaterialOrColor(cell, gridMaterial, new Color(0.67f, 0.42f, 0.12f));
                }
            }

            return board;
        }

        private static void CreateMovableCubes(GridBoard board, GameObject cubePrefab, Material cubeMaterial)
        {
            GameObject root = new GameObject("MovableCubes");
            for (int index = 0; index < 9; index++)
            {
                GameObject cube = cubePrefab != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"PuzzleCube_{index + 1:00}";
                cube.transform.SetParent(root.transform);
                int trayColumn = index % 5;
                int trayRow = index / 5;
                cube.transform.position = new Vector3((trayColumn - 2) * 1.15f, 0.62f, -3.7f - trayRow * 1.15f);
                cube.transform.localScale = Vector3.one;
                ApplyMaterialOrColor(cube, cubeMaterial, new Color(0.9f, 0.5f, 0.16f));

                DraggableCube draggable = cube.GetComponent<DraggableCube>() ?? cube.AddComponent<DraggableCube>();
                draggable.Initialize(board);
            }
        }

        private static void CreateShadowScreen(GridBoard board, Material blackMaterial)
        {
            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name = "ProjectionScreen";
            screen.transform.position = new Vector3(0f, 2.25f, 3.65f);
            screen.transform.localScale = new Vector3(7.2f, 4.4f, 0.12f);
            SetColor(screen, new Color(0.88f, 0.84f, 0.72f));

            GameObject shadowsRoot = new GameObject("TemporaryShadowAssets");
            shadowsRoot.transform.position = new Vector3(0f, 0f, 3.55f);
            var pieces = new List<GameObject>();
            const int previewHeight = 3;
            for (int column = 0; column < GridWidth; column++)
            {
                for (int level = 0; level < previewHeight; level++)
                {
                    GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    piece.name = $"Shadow_C{column}_H{level}";
                    piece.transform.SetParent(shadowsRoot.transform);
                    piece.transform.position = new Vector3((column - 2) * 1.12f, 0.75f + level * 1.08f, 3.55f);
                    piece.transform.localScale = new Vector3(1.02f, 1.02f, 0.04f);
                    Object.DestroyImmediate(piece.GetComponent<Collider>());
                    ApplyMaterialOrColor(piece, blackMaterial, new Color(0.025f, 0.02f, 0.03f));
                    piece.SetActive(false);
                    pieces.Add(piece);
                }
            }

            FakeShadowDisplay display = shadowsRoot.AddComponent<FakeShadowDisplay>();
            display.Initialize(board, pieces, previewHeight);
        }

        private static void CreateTargetPaper(Material blackMaterial)
        {
            GameObject paper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            paper.name = "TargetPaper";
            paper.transform.position = new Vector3(-4.6f, 0.02f, -1.2f);
            paper.transform.localScale = new Vector3(2.7f, 0.035f, 2.7f);
            SetColor(paper, new Color(0.94f, 0.91f, 0.8f));

            int[] target = { 1, 2, 3, 2, 1 };
            for (int column = 0; column < target.Length; column++)
            {
                for (int level = 0; level < target[column]; level++)
                {
                    GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    mark.name = $"TargetMark_{column}_{level}";
                    mark.transform.SetParent(paper.transform);
                    mark.transform.localPosition = new Vector3((column - 2) * 0.18f, 0.55f, (level - 1) * 0.28f);
                    mark.transform.localScale = new Vector3(0.15f, 0.12f, 0.24f);
                    Object.DestroyImmediate(mark.GetComponent<Collider>());
                    ApplyMaterialOrColor(mark, blackMaterial, new Color(0.025f, 0.02f, 0.03f));
                }
            }
        }

        private static void CreateGameManager(GridBoard board, Camera playerCamera)
        {
            GameObject managerObject = new GameObject("PrototypeGameManager");
            PrototypeGameManager manager = managerObject.AddComponent<PrototypeGameManager>();
            manager.Initialize(board, playerCamera, new[] { 1, 2, 3, 2, 1 });
        }

        private static void ApplyMaterialOrColor(GameObject target, Material material, Color fallbackColor)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;
            if (material != null) renderer.sharedMaterial = material;
            else SetColor(target, fallbackColor);
        }

        private static void SetColor(GameObject target, Color color)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color;
            renderer.sharedMaterial = material;
        }
    }
}
#endif
