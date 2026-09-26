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
        public bool startsOnBoard;
        public Vector2Int boardCell;

        public StageCubeSpawn(GameObject cubePrefab, Vector3 position, Vector3 eulerAngles)
        {
            prefab = cubePrefab;
            localPosition = position;
            localEulerAngles = eulerAngles;
            startsOnBoard = false;
            boardCell = default;
        }

        public StageCubeSpawn(GameObject cubePrefab, Vector2Int initialBoardCell, Vector3 eulerAngles)
        {
            prefab = cubePrefab;
            localPosition = Vector3.zero;
            localEulerAngles = eulerAngles;
            startsOnBoard = true;
            boardCell = initialBoardCell;
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
    /// 한 퍼즐의 격자, 제공 큐브, 목표 전등을 코드와 분리해 보관하는 데이터 애셋이다.
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
        [SerializeField] private Vector2Int gridSize = new(3, 5);

        [Header("제공 큐브")]
        [SerializeField] private List<StageCubeSpawn> cubeSpawns = new();

        [Header("목표 전등")]
        [SerializeField] private List<LampTarget> lampTargets = new();

        [Header("이전 그림자 데이터 (호환용)")]
        [SerializeField] private bool projectionUsesDepthAxis = true;
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
        public IReadOnlyList<LampTarget> LampTargets => lampTargets;

        /// <summary>에디터의 예시 스테이지 생성기가 데이터를 일괄 기록할 때 사용한다.</summary>
        public void Configure(
            string id,
            string title,
            string stageHint,
            Vector2Int size,
            IEnumerable<StageCubeSpawn> spawns,
            IEnumerable<LampTarget> lamps,
            bool ice = true,
            bool brittle = true,
            bool adhesive = true,
            bool colors = true)
        {
            stageId = id;
            displayName = title;
            hint = stageHint;
            gridSize = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
            cubeSpawns = new List<StageCubeSpawn>(spawns);
            lampTargets = new List<LampTarget>(lamps);
            enableIce = ice;
            enableBrittle = brittle;
            enableAdhesive = adhesive;
            enableColors = colors;
        }

        /// <summary>이전 그림자 스테이지 생성 코드가 남아 있어도 애셋을 읽을 수 있게 유지하는 호환 오버로드다.</summary>
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
            lampTargets = new List<LampTarget>();
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

        /// <summary>모든 전등이 요구 색을 정면으로 받고, 잘못된 면에는 빛이 닿지 않았는지 검사한다.</summary>
        public bool Matches(LightSimulationResult result)
        {
            return result != null && result.IsSolved;
        }

        /// <summary>UI에서 전등의 위치, 정면 방향, 요구 색을 읽기 쉽게 보여준다.</summary>
        public string BuildTargetPreview()
        {
            if (lampTargets.Count > 0)
            {
                var lampBuilder = new StringBuilder();
                for (int index = 0; index < lampTargets.Count; index++)
                {
                    LampTarget lamp = lampTargets[index];
                    lampBuilder.Append($"등 {index + 1}: ({lamp.gridPosition.x}, {lamp.gridPosition.y}, {lamp.gridPosition.z}) ");
                    lampBuilder.Append($"{GetColorLabel(lamp.requiredColor)} / 정면 {GetDirectionLabel(lamp.frontDirection)}");
                    if (index < lampTargets.Count - 1) lampBuilder.AppendLine();
                }

                return lampBuilder.ToString();
            }

            // 이전에 만든 그림자 스테이지 애셋도 Inspector에서 내용을 잃지 않게 표시한다.
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

        private static string GetColorLabel(PuzzleLightColor color)
        {
            return color switch
            {
                PuzzleLightColor.Red => "빨강",
                PuzzleLightColor.Blue => "파랑",
                PuzzleLightColor.Yellow => "노랑",
                _ => "흰색"
            };
        }

        private static string GetDirectionLabel(CardinalDirection direction)
        {
            return direction switch
            {
                CardinalDirection.PositiveX => "+X",
                CardinalDirection.NegativeX => "-X",
                CardinalDirection.PositiveY => "위",
                CardinalDirection.NegativeY => "아래",
                CardinalDirection.PositiveZ => "+Z",
                _ => "-Z"
            };
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
