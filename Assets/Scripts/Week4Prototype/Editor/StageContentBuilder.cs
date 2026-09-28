#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameLab.Week4.Editor
{
    /// <summary>SampleScene_Test의 3x5 격자를 사용하는 예시 스테이지와 필요한 큐브 프리팹을 최초 생성한다.</summary>
    public static class StageContentBuilder
    {
        private const string StageFolder = "Assets/Resources/Stages";
        private const string CampaignPath = StageFolder + "/PuzzleCampaign.asset";

        [MenuItem("Tools/GameLab/Build Light Puzzle Campaign")]
        public static void BuildExampleCampaign()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            EnsureFolder(StageFolder);
            EnsureAllCubePrefabs();

            GameObject normal = LoadPrefab("Cube");
            GameObject emitter = LoadPrefab("LightEmitter");
            GameObject refractor = LoadPrefab("Refractor");
            GameObject splitter = LoadPrefab("LightSplitter");
            GameObject blueGlass = LoadPrefab("ColoredGlass_Blue");
            GameObject ice = LoadPrefab("Ice");
            if (normal == null || emitter == null || refractor == null || splitter == null ||
                blueGlass == null || ice == null)
            {
                Debug.LogWarning("전등 퍼즐 캠페인 생성 보류: 필요한 큐브 프리팹이 준비되지 않았습니다.");
                return;
            }

            Vector2Int gridSize = new(3, 5);
            LampTarget farCenterWhite = FrontLamp(2, 0);

            PuzzleStageDefinition stage1 = CreateStageIfMissing(
                "Stage_Light_001_Emitter",
                "light-001",
                "첫 번째 전등",
                "발광 면이 전등을 향하도록 큐브를 회전한 뒤 Space로 점등하세요.",
                gridSize,
                new[] { BoardSpawn(emitter, 2, 2) },
                new[] { farCenterWhite });

            PuzzleStageDefinition stage2 = CreateStageIfMissing(
                "Stage_Light_002_Refractor",
                "light-002",
                "모퉁이를 도는 빛",
                "두 굴절 큐브로 빛을 옆 칸으로 우회시킨 뒤 전등판까지 보내세요.",
                gridSize,
                new[] { BoardSpawn(emitter, 2, 1), TraySpawn(refractor, 0), TraySpawn(refractor, 1) },
                new[] { FrontLamp(0, 0) });

            PuzzleStageDefinition stage3 = CreateStageIfMissing(
                "Stage_Light_003_BlueFilter",
                "light-003",
                "파란 전등",
                "파란 색유리를 통과한 빛만 파란 전등을 켤 수 있습니다.",
                gridSize,
                new[] { BoardSpawn(emitter, 2, 2), TraySpawn(blueGlass, 0), TraySpawn(normal, 1) },
                new[] { FrontLamp(2, 0, PuzzleLightColor.Blue) });

            PuzzleStageDefinition stage4 = CreateStageIfMissing(
                "Stage_Light_004_MeltingIce",
                "light-004",
                "녹은 뒤의 빛",
                "얼음은 빛을 받으면 1초 뒤 녹습니다. 사라진 뒤 광선이 다시 진행합니다.",
                gridSize,
                new[] { BoardSpawn(emitter, 2, 2), BoardSpawn(ice, 1, 2) },
                new[] { farCenterWhite });

            PuzzleStageDefinition stage5 = CreateStageIfMissing(
                "Stage_Light_005_Splitter",
                "light-005",
                "두 갈래의 빛",
                "빛 분기 큐브의 한 입력 면으로 빛을 받아 두 전등을 동시에 켜세요.",
                gridSize,
                new[]
                {
                    ReserveSpawn(emitter, CubeReserveSide.Left, 2),
                    TraySpawn(splitter, 0),
                    TraySpawn(refractor, 1),
                    TraySpawn(refractor, 2)
                },
                new[] { FrontLamp(0, 0), FrontLamp(4, 0) });

            PuzzleCampaign campaign = AssetDatabase.LoadAssetAtPath<PuzzleCampaign>(CampaignPath);
            if (campaign == null)
            {
                campaign = ScriptableObject.CreateInstance<PuzzleCampaign>();
                campaign.Configure(new[] { stage1, stage2, stage3, stage4, stage5 });
                AssetDatabase.CreateAsset(campaign, CampaignPath);
                EditorUtility.SetDirty(campaign);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("전등 켜기 예시 캠페인 확인 완료: 없는 애셋만 생성하며 기존 Stage/Campaign 내용은 보존합니다.");
        }

        /// <summary>기존 프리팹은 유지하고, 없는 광학 큐브만 Cube 프리팹을 바탕으로 추가한다.</summary>
        private static void EnsureAllCubePrefabs()
        {
            EnsureCubePrefab("Cube", "Cube", PuzzleCubeType.Normal, PuzzleLightColor.White);
            EnsureCubePrefab("Glass", "Glass", PuzzleCubeType.Glass, PuzzleLightColor.White);
            EnsureCubePrefab("ColoredGlass_Blue", "ColoredGlass_Blue", PuzzleCubeType.ColoredGlass, PuzzleLightColor.Blue);
            EnsureCubePrefab("ColoredGlass_Red", "ColoredGlass_Blue", PuzzleCubeType.ColoredGlass, PuzzleLightColor.Red);
            EnsureCubePrefab("ColoredGlass_Green", "ColoredGlass_Blue", PuzzleCubeType.ColoredGlass, PuzzleLightColor.Green);
            EnsureCubePrefab("ColoredGlass_Yellow", "ColoredGlass_Blue", PuzzleCubeType.ColoredGlass, PuzzleLightColor.Yellow);
            EnsureCubePrefab("Brittle", "Brittle", PuzzleCubeType.Brittle, PuzzleLightColor.White);
            EnsureCubePrefab("Styrofoam", "Styrofoam", PuzzleCubeType.Styrofoam, PuzzleLightColor.White);
            EnsureCubePrefab("Ice", "Ice", PuzzleCubeType.Ice, PuzzleLightColor.White);
            EnsureCubePrefab("LightEmitter", "Cube", PuzzleCubeType.LightEmitter, PuzzleLightColor.White);
            EnsureCubePrefab("Refractor", "Cube", PuzzleCubeType.Refractor, PuzzleLightColor.White);
            // 기존 Refractor의 외형을 시작점으로 복제하되 원본 프리팹은 수정하지 않는다.
            EnsureCubePrefab("LightSplitter", "Refractor", PuzzleCubeType.LightSplitter, PuzzleLightColor.White);
        }

        private static void EnsureCubePrefab(
            string prefabName,
            string templateName,
            PuzzleCubeType type,
            PuzzleLightColor color)
        {
            string targetPath = $"Assets/Prefabs/{prefabName}.prefab";
            string templatePath = $"Assets/Prefabs/{templateName}.prefab";
            string editPath = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath) != null
                ? targetPath
                : templatePath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(editPath) == null) return;

            GameObject root = PrefabUtility.LoadPrefabContents(editPath);
            try
            {
                root.name = prefabName;
                DraggableCube draggable = root.GetComponent<DraggableCube>() ?? root.AddComponent<DraggableCube>();
                PuzzleCubeProperties properties = root.GetComponent<PuzzleCubeProperties>() ??
                                                  root.AddComponent<PuzzleCubeProperties>();
                properties.ConfigureOptics(type, color);
                if (type == PuzzleCubeType.LightSplitter && root.transform.Find("SplitterVisual") == null)
                {
                    properties.ConfigureSplitterDefaults();
                    ConfigureLightSplitterVisual(root);
                }

                EditorUtility.SetDirty(draggable);
                EditorUtility.SetDirty(properties);
                PrefabUtility.SaveAsPrefabAsset(root, targetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Refractor에서 복제된 포트와 Quad 메시만 이용해 T자형 분기 표시를 만든다.
        /// SplitterVisual이 이미 있으면 사용자가 편집한 외형을 보존하기 위해 다시 만들지 않는다.
        /// </summary>
        private static void ConfigureLightSplitterVisual(GameObject root)
        {
            Transform inputPort = root.transform.Find("Quad");
            Transform outputPortA = root.transform.Find("Quad (1)");
            if (inputPort == null || outputPortA == null) return;

            inputPort.name = "SplitterInputPort";
            PlaceSplitterPort(inputPort, Vector3Int.right, 0.46f);

            outputPortA.name = "SplitterOutputPortA";
            PlaceSplitterPort(outputPortA, new Vector3Int(0, 0, -1), 0.33f);

            GameObject outputPortBObject = Object.Instantiate(outputPortA.gameObject, root.transform);
            outputPortBObject.name = "SplitterOutputPortB";
            PlaceSplitterPort(outputPortBObject.transform, new Vector3Int(0, 0, 1), 0.33f);

            GameObject visualRootObject = new("SplitterVisual");
            visualRootObject.transform.SetParent(root.transform, false);
            Transform visualRoot = visualRootObject.transform;

            // 기존 포트 Quad를 얇은 막대로 재사용해 위·아래에서 보이는 추가 출력 화살표를 만든다.
            CreateBranchArrow(outputPortA.gameObject, visualRoot, Vector3.up, "TopBranch");
            CreateBranchArrow(outputPortA.gameObject, visualRoot, Vector3.down, "BottomBranch");
        }

        private static void PlaceSplitterPort(Transform port, Vector3Int faceDirection, float size)
        {
            Vector3 direction = ((Vector3)faceDirection).normalized;
            port.localPosition = direction * 0.505f + Vector3.up * 0.045f;
            port.localRotation = Quaternion.LookRotation(-direction, Vector3.up);
            port.localScale = Vector3.one * size;
        }

        private static void CreateBranchArrow(
            GameObject quadTemplate,
            Transform parent,
            Vector3 surfaceNormal,
            string prefix)
        {
            float surfaceY = surfaceNormal.y > 0f ? 0.515f : -0.515f;
            Quaternion surfaceRotation = Quaternion.LookRotation(surfaceNormal, Vector3.forward);

            CreateGuideQuad(
                quadTemplate,
                parent,
                $"{prefix}_Body",
                new Vector3(0f, surfaceY, 0.22f),
                new Vector3(0.05f, 0.38f, 0.4f),
                surfaceRotation);
            CreateGuideQuad(
                quadTemplate,
                parent,
                $"{prefix}_HeadLeft",
                new Vector3(-0.055f, surfaceY, 0.405f),
                new Vector3(0.045f, 0.16f, 0.4f),
                surfaceRotation * Quaternion.Euler(0f, 0f, 42f));
            CreateGuideQuad(
                quadTemplate,
                parent,
                $"{prefix}_HeadRight",
                new Vector3(0.055f, surfaceY, 0.405f),
                new Vector3(0.045f, 0.16f, 0.4f),
                surfaceRotation * Quaternion.Euler(0f, 0f, -42f));
        }

        private static void CreateGuideQuad(
            GameObject template,
            Transform parent,
            string objectName,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation)
        {
            GameObject part = Object.Instantiate(template, parent);
            part.name = objectName;
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }
        }

        private static PuzzleStageDefinition CreateStageIfMissing(
            string assetName,
            string id,
            string title,
            string hint,
            Vector2Int gridSize,
            IEnumerable<StageCubeSpawn> spawns,
            IEnumerable<LampTarget> lamps)
        {
            string path = $"{StageFolder}/{assetName}.asset";
            PuzzleStageDefinition stage = AssetDatabase.LoadAssetAtPath<PuzzleStageDefinition>(path);
            if (stage != null)
            {
                return stage;
            }

            stage = ScriptableObject.CreateInstance<PuzzleStageDefinition>();
            stage.Configure(id, title, hint, gridSize, spawns, lamps);
            AssetDatabase.CreateAsset(stage, path);
            EditorUtility.SetDirty(stage);
            return stage;
        }

        private static StageCubeSpawn TraySpawn(GameObject prefab, int index)
        {
            int row = index / 5;
            int column = index % 5;
            return new StageCubeSpawn(
                prefab,
                new Vector3(0.08f + column * 0.26f, 0.1f, -0.52f + row * 0.28f),
                Vector3.zero);
        }

        private static StageCubeSpawn ReserveSpawn(
            GameObject prefab,
            CubeReserveSide side,
            int slot,
            Vector3? eulerAngles = null)
        {
            return new StageCubeSpawn(prefab, side, slot, eulerAngles ?? Vector3.zero);
        }

        private static StageCubeSpawn BoardSpawn(GameObject prefab, int x, int z)
        {
            return new StageCubeSpawn(prefab, new Vector2Int(x, z), Vector3.zero);
        }

        private static LampTarget Lamp(
            Vector3Int position,
            CardinalDirection front,
            PuzzleLightColor color = PuzzleLightColor.White)
        {
            return new LampTarget(position, front, color);
        }

        /// <summary>
        /// 플레이어 카메라에서 멀어지는 보드 -X 경계에 전등을 놓는다.
        /// 5개 패널 열은 3x5 보드의 깊이 인덱스 0~4와 맞물린다.
        /// </summary>
        private static LampTarget FrontLamp(
            int panelColumn,
            int row,
            PuzzleLightColor color = PuzzleLightColor.White)
        {
            const int panelDepthStart = 0;
            return Lamp(
                new Vector3Int(-1, row, panelDepthStart + panelColumn),
                CardinalDirection.PositiveX,
                color);
        }

        private static GameObject LoadPrefab(string prefabName)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/{prefabName}.prefab");
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }
    }
}
#endif
