using UnityEngine;

namespace GameLab.Week4
{
    /// <summary>퍼즐 규칙에서 구분하는 큐브 종류다. 아직 사용하지 않는 종류도 데이터 호환을 위해 함께 정의한다.</summary>
    public enum PuzzleCubeType
    {
        Normal,
        Adhesive,
        Glass,
        ColoredGlass,
        Brittle,
        Cracked,
        Styrofoam,
        Ice
    }

    /// <summary>큐브의 재질별 퍼즐 성질을 보관하고 파괴·용해 상태를 관리한다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DraggableCube))]
    public sealed class PuzzleCubeProperties : MonoBehaviour
    {
        [SerializeField] private PuzzleCubeType cubeType = PuzzleCubeType.Normal;
        [SerializeField] private Color shadowColor = Color.white;
        [Min(0f)] [SerializeField] private float iceMeltDelay = 1f;

        private DraggableCube draggableCube;
        private bool removedByRule;

        public PuzzleCubeType CubeType => cubeType;
        public Color ShadowColor => shadowColor;
        public bool CastsShadow => !removedByRule &&
                                   cubeType != PuzzleCubeType.Glass &&
                                   cubeType != PuzzleCubeType.Ice;
        public bool IsStyrofoam => cubeType == PuzzleCubeType.Styrofoam;
        public bool BreaksUnderWeight => cubeType == PuzzleCubeType.Brittle;
        public bool MeltsInLight => cubeType == PuzzleCubeType.Ice && !removedByRule;
        public float IceMeltDelay => iceMeltDelay;

        private void Awake()
        {
            draggableCube = GetComponent<DraggableCube>();
        }

        /// <summary>씬 통합기가 이름으로 추론한 초기 타입을 적용한다.</summary>
        public void Configure(PuzzleCubeType type)
        {
            cubeType = type;
        }

        /// <summary>바사삭 큐브를 격자에서 제거하고 시각 오브젝트도 숨긴다.</summary>
        public void Break()
        {
            RemoveFromPuzzle();
        }

        /// <summary>GridBoard가 용해 대기 시간을 처리한 뒤 얼음 큐브를 제거한다.</summary>
        public void ApplyLightEffectNow()
        {
            if (cubeType == PuzzleCubeType.Ice)
            {
                RemoveFromPuzzle(animateCollapse: true);
            }
        }

        private void RemoveFromPuzzle(bool animateCollapse = false)
        {
            if (removedByRule) return;

            removedByRule = true;
            if (draggableCube == null)
            {
                draggableCube = GetComponent<DraggableCube>();
            }

            draggableCube.RemoveFromBoardForRule(animateCollapse);
            gameObject.SetActive(false);
        }
    }
}
