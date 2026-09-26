using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameLab.Week4
{
    /// <summary>스테이지 시작 시 생성할 큐브 프리팹과 보관대 기준 위치다.</summary>
    [Serializable]
    public struct StageCubeSpawn
    {
        public GameObject prefab;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;

        public StageCubeSpawn(GameObject cubePrefab, Vector3 position, Vector3 eulerAngles)
        {
            prefab = cubePrefab;
            localPosition = position;
            localEulerAngles = eulerAngles;
        }
    }

    /// <summary>목표 그림자에서 채워져야 하는 한 칸의 열·층 좌표다.</summary>
    [Serializable]
    public struct ShadowTargetCell
    {
        [Min(0)] public int column;
        [Min(0)] public int level;
        public Color color;

        public ShadowTargetCell(int targetColumn, int targetLevel, Color targetColor)
        {
            column = targetColumn;
            level = targetLevel;
            color = targetColor;
        }
    }

    /// <summary>
    /// 한 퍼즐의 격자, 제공 큐브, 목표 그림자를 코드와 분리해 보관하는 데이터 애셋이다.
    /// 새 퍼즐은 이 애셋만 추가하면 공용 플레이 씬에서 실행된다.
    /// </summary>
    [CreateAssetMenu(menuName = "GameLab/Puzzle Stage", fileName = "Stage_New")]
    public sealed class PuzzleStageDefinition : ScriptableObject
    {
        [Header("표시 정보")]
        [SerializeField] private string stageId = "stage-new";
        [SerializeField] private string displayName = "새 스테이지";
        [TextArea] [SerializeField] private string hint;

        [Header("격자")]
        [SerializeField] private Vector2Int gridSize = new(4, 7);
        [SerializeField] private bool projectionUsesDepthAxis = true;

        [Header("제공 큐브")]
        [SerializeField] private List<StageCubeSpawn> cubeSpawns = new();

        [Header("목표 그림자")]
        [SerializeField] private List<ShadowTargetCell> targetCells = new();

        [Header("선택 규칙")]
        [SerializeField] private bool enableIce = true;
        [SerializeField] private bool enableBrittle = true;
        [SerializeField] private bool enableAdhesive = true;
        [SerializeField] private bool enableColors = true;

        public string StageId => stageId;
        public string DisplayName => displayName;
        public string Hint => hint;
        public Vector2Int GridSize => gridSize;
        public bool ProjectionUsesDepthAxis => projectionUsesDepthAxis;
        public IReadOnlyList<StageCubeSpawn> CubeSpawns => cubeSpawns;
        public IReadOnlyList<ShadowTargetCell> TargetCells => targetCells;

        /// <summary>에디터의 예시 스테이지 생성기가 데이터를 일괄 기록할 때 사용한다.</summary>
        public void Configure(
            string id,
            string title,
            string stageHint,
            Vector2Int size,
            bool useDepthAxis,
            IEnumerable<StageCubeSpawn> spawns,
            IEnumerable<ShadowTargetCell> targets,
            bool ice = true,
            bool brittle = true,
            bool adhesive = true,
            bool colors = true)
        {
            stageId = id;
            displayName = title;
            hint = stageHint;
            gridSize = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
            projectionUsesDepthAxis = useDepthAxis;
            cubeSpawns = new List<StageCubeSpawn>(spawns);
            targetCells = new List<ShadowTargetCell>(targets);
            enableIce = ice;
            enableBrittle = brittle;
            enableAdhesive = adhesive;
            enableColors = colors;
        }

        /// <summary>현재 격자의 모든 그림자 셀이 목표의 채움 상태와 정확히 같은지 검사한다.</summary>
        public bool Matches(GridBoard board)
        {
            if (board == null) return false;

            int columnCount = projectionUsesDepthAxis ? board.Depth : board.Width;
            int maximumLevel = Mathf.Max(board.GetMaximumStackHeight(), GetTargetHeight());
            for (int column = 0; column < columnCount; column++)
            {
                for (int level = 0; level < maximumLevel; level++)
                {
                    bool actual = board.DoesProjectedCellCastShadow(column, level, projectionUsesDepthAxis);
                    bool expected = ContainsTargetCell(column, level);
                    if (actual != expected)
                    {
                        return false;
                    }
                }
            }

            return targetCells.Count > 0;
        }

        /// <summary>UI에서 목표 실루엣을 글자 격자로 보여준다.</summary>
        public string BuildTargetPreview()
        {
            int columns = projectionUsesDepthAxis ? gridSize.y : gridSize.x;
            int height = Mathf.Max(1, GetTargetHeight());
            var builder = new StringBuilder();
            for (int level = height - 1; level >= 0; level--)
            {
                for (int column = 0; column < columns; column++)
                {
                    builder.Append(ContainsTargetCell(column, level) ? '■' : '·');
                    builder.Append(' ');
                }

                if (level > 0) builder.AppendLine();
            }

            return builder.ToString();
        }

        private bool ContainsTargetCell(int column, int level)
        {
            foreach (ShadowTargetCell cell in targetCells)
            {
                if (cell.column == column && cell.level == level)
                {
                    return true;
                }
            }

            return false;
        }

        private int GetTargetHeight()
        {
            int height = 0;
            foreach (ShadowTargetCell cell in targetCells)
            {
                height = Mathf.Max(height, cell.level + 1);
            }

            return height;
        }
    }
}
