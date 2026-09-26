using System.Collections.Generic;
using UnityEngine;

namespace GameLab.Week4
{
    /// <summary>여러 퍼즐 스테이지의 진행 순서를 보관하는 캠페인 애셋이다.</summary>
    [CreateAssetMenu(menuName = "GameLab/Puzzle Campaign", fileName = "PuzzleCampaign")]
    public sealed class PuzzleCampaign : ScriptableObject
    {
        [SerializeField] private List<PuzzleStageDefinition> stages = new();

        public int StageCount => stages.Count;

        public PuzzleStageDefinition GetStage(int index)
        {
            return index >= 0 && index < stages.Count ? stages[index] : null;
        }

        /// <summary>에디터 생성기가 스테이지 순서를 갱신한다.</summary>
        public void Configure(IEnumerable<PuzzleStageDefinition> orderedStages)
        {
            stages = new List<PuzzleStageDefinition>(orderedStages);
        }
    }
}
