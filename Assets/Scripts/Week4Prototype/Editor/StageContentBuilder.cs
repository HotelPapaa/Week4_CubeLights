#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameLab.Week4.Editor
{
    /// <summary>프리팹을 이용한 예시 퍼즐 4개와 캠페인 애셋을 생성·갱신한다.</summary>
    public static class StageContentBuilder
    {
        private const string StageFolder = "Assets/Resources/Stages";
        private const string CampaignPath = StageFolder + "/PuzzleCampaign.asset";

        [InitializeOnLoadMethod]
        private static void QueueBuild()
        {
            EditorApplication.delayCall += BuildExampleCampaign;
        }

        [MenuItem("Tools/GameLab/Build Example Puzzle Campaign")]
        public static void BuildExampleCampaign()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            EnsureFolder(StageFolder);
            GameObject normal = LoadPrefab("Cube");
            GameObject glass = LoadPrefab("Glass");
            GameObject ice = LoadPrefab("Ice");
            GameObject brittle = LoadPrefab("Brittle");
            GameObject styrofoam = LoadPrefab("Styrofoam");
            if (normal == null || glass == null || ice == null || brittle == null || styrofoam == null)
            {
                Debug.LogWarning("예시 캠페인 생성 보류: 필요한 큐브 프리팹이 아직 준비되지 않았습니다.");
                return;
            }

            Color black = Color.black;
            PuzzleStageDefinition stage1 = CreateOrUpdateStage(
                "Stage_001_ShadowBasics",
                "stage-001",
                "그림자의 시작",
                "세 개의 일반 큐브로 목표 실루엣을 만드세요.",
                new[] { Spawn(normal, 0), Spawn(normal, 1), Spawn(normal, 2) },
                new[] { Target(2, 0, black), Target(3, 0, black), Target(4, 0, black) });

            PuzzleStageDefinition stage2 = CreateOrUpdateStage(
                "Stage_002_InvisibleSupport",
                "stage-002",
                "보이지 않는 받침",
                "유리는 자리를 차지하지만 그림자를 만들지 않습니다.",
                new[] { Spawn(normal, 0), Spawn(normal, 1), Spawn(glass, 2) },
                new[] { Target(3, 1, black), Target(4, 0, black) });

            PuzzleStageDefinition stage3 = CreateOrUpdateStage(
                "Stage_003_AfterMelting",
                "stage-003",
                "녹은 뒤의 모양",
                "빛을 켜면 얼음이 사라지고 위의 큐브가 내려옵니다.",
                new[] { Spawn(normal, 0), Spawn(normal, 1), Spawn(ice, 2) },
                new[] { Target(3, 0, black), Target(3, 1, black) });

            PuzzleStageDefinition stage4 = CreateOrUpdateStage(
                "Stage_004_ProtectTheBrittle",
                "stage-004",
                "바사삭을 보호하라",
                "스티로폼은 바사삭 큐브 위에 놓여도 아래 큐브를 부수지 않습니다.",
                new[] { Spawn(brittle, 0), Spawn(styrofoam, 1), Spawn(normal, 2) },
                new[] { Target(3, 0, black), Target(3, 1, black), Target(3, 2, black) });

            PuzzleCampaign campaign = AssetDatabase.LoadAssetAtPath<PuzzleCampaign>(CampaignPath);
            if (campaign == null)
            {
                campaign = ScriptableObject.CreateInstance<PuzzleCampaign>();
                AssetDatabase.CreateAsset(campaign, CampaignPath);
            }

            campaign.Configure(new[] { stage1, stage2, stage3, stage4 });
            EditorUtility.SetDirty(campaign);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorApplication.delayCall += SampleSceneTestIntegrator.Integrate;
            Debug.Log("예시 퍼즐 캠페인 생성 완료: 4개 스테이지");
        }

        private static PuzzleStageDefinition CreateOrUpdateStage(
            string assetName,
            string id,
            string title,
            string hint,
            IEnumerable<StageCubeSpawn> spawns,
            IEnumerable<ShadowTargetCell> targets)
        {
            string path = $"{StageFolder}/{assetName}.asset";
            PuzzleStageDefinition stage = AssetDatabase.LoadAssetAtPath<PuzzleStageDefinition>(path);
            if (stage == null)
            {
                stage = ScriptableObject.CreateInstance<PuzzleStageDefinition>();
                AssetDatabase.CreateAsset(stage, path);
            }

            stage.Configure(id, title, hint, new Vector2Int(4, 7), true, spawns, targets);
            EditorUtility.SetDirty(stage);
            return stage;
        }

        private static StageCubeSpawn Spawn(GameObject prefab, int index)
        {
            int row = index / 4;
            int column = index % 4;
            return new StageCubeSpawn(
                prefab,
                new Vector3(0.08f + column * 0.26f, 0.1f, -0.52f + row * 0.28f),
                Vector3.zero);
        }

        private static ShadowTargetCell Target(int column, int level, Color color)
        {
            return new ShadowTargetCell(column, level, color);
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
