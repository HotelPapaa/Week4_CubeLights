using System.Collections.Generic;
using UnityEngine;

namespace GameLab.Week4
{
    /// <summary>
    /// 실제 조명 그림자 대신 격자의 투영 높이에 맞춰 미리 만든 임시 그림자 조각을 켜고 끈다.
    /// 나중에는 shadowPieces를 최종 그림자 애셋으로 교체하면 된다.
    /// </summary>
    public sealed class FakeShadowDisplay : MonoBehaviour
    {
        [SerializeField] private GridBoard board;
        [SerializeField] private List<GameObject> shadowPieces = new();
        [SerializeField] private List<int> pieceColumns = new();
        [SerializeField] private List<int> pieceLevels = new();
        [SerializeField] private List<int> pieceDepthBands = new();
        [SerializeField] private List<int> pieceBandOrdinals = new();
        [SerializeField] private bool useDepthBandMapping;
        [SerializeField] private bool bandsUseWidthAxis;
        [SerializeField] private bool useCompositePattern;
        [SerializeField] private bool compositeUsesDepthAxis;
        [SerializeField] private List<int> requiredProjection = new();
        [Min(1)] [SerializeField] private int maximumPreviewHeight = 3;
        [SerializeField] private bool projectionVisible = true;

        private Vector3 compositeOrigin;
        private Vector3 compositeHorizontalStep;
        private Vector3 compositeVerticalStep;
        private bool hasCompositeLayout;

        public void Initialize(GridBoard targetBoard, List<GameObject> pieces, int previewHeight)
        {
            if (board != null)
            {
                board.BoardChanged -= Refresh;
            }

            board = targetBoard;
            shadowPieces = pieces;
            maximumPreviewHeight = Mathf.Max(1, previewHeight);
            useDepthBandMapping = false;
            useCompositePattern = false;

            if (isActiveAndEnabled && board != null)
            {
                board.BoardChanged -= Refresh;
                board.BoardChanged += Refresh;
                Refresh();
            }
        }

        /// <summary>
        /// 기존 씬에 이미 배치된 그림자 조각을 열/층 좌표와 직접 연결한다.
        /// 조각을 복제하거나 원본 프리팹을 수정하지 않고도 불규칙한 기존 구성을 재사용할 수 있다.
        /// </summary>
        public void InitializeSparse(
            GridBoard targetBoard,
            List<GameObject> pieces,
            List<int> columns,
            List<int> levels)
        {
            if (board != null)
            {
                board.BoardChanged -= Refresh;
            }

            board = targetBoard;
            shadowPieces = pieces;
            pieceColumns = columns;
            pieceLevels = levels;
            useDepthBandMapping = false;
            useCompositePattern = false;

            if (isActiveAndEnabled && board != null)
            {
                board.BoardChanged -= Refresh;
                board.BoardChanged += Refresh;
                Refresh();
            }
        }

        /// <summary>
        /// 카메라가 바라보는 보드 축을 앞/중간/뒤 3개 구간으로 나눠 기존 그림자 조각과 연결한다.
        /// 각 구간에 놓인 큐브 수만큼 해당 구간의 조각을 공간 순서대로 활성화한다.
        /// </summary>
        public void InitializeRowBands(
            GridBoard targetBoard,
            List<GameObject> pieces,
            List<int> depthBands,
            List<int> bandOrdinals,
            bool useWidthAxis)
        {
            if (board != null)
            {
                board.BoardChanged -= Refresh;
            }

            board = targetBoard;
            shadowPieces = pieces;
            pieceDepthBands = depthBands;
            pieceBandOrdinals = bandOrdinals;
            useDepthBandMapping = true;
            useCompositePattern = false;
            bandsUseWidthAxis = useWidthAxis;

            if (isActiveAndEnabled && board != null)
            {
                board.BoardChanged -= Refresh;
                board.BoardChanged += Refresh;
                Refresh();
            }
        }

        /// <summary>
        /// 여러 평면 조각으로 미리 구성된 그림자를 하나의 누적 애셋으로 취급한다.
        /// 격자에 놓인 큐브 수만큼 아래쪽 조각부터 켜서 미완성 상태에서도 그림자가 보이게 한다.
        /// </summary>
        public void InitializeCompositePattern(
            GridBoard targetBoard,
            List<GameObject> pieces,
            IReadOnlyList<int> targetProjection,
            Camera projectionCamera)
        {
            if (board != null)
            {
                board.BoardChanged -= Refresh;
            }

            board = targetBoard;
            shadowPieces = pieces;
            requiredProjection = targetProjection != null
                ? new List<int>(targetProjection)
                : new List<int>();
            useDepthBandMapping = false;
            useCompositePattern = true;
            ConfigureCompositeLayout(projectionCamera);

            if (isActiveAndEnabled && board != null)
            {
                board.BoardChanged -= Refresh;
                board.BoardChanged += Refresh;
                Refresh();
            }
        }

        private void OnEnable()
        {
            if (board != null)
            {
                board.BoardChanged += Refresh;
                Refresh();
            }
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.BoardChanged -= Refresh;
            }
        }

        /// <summary>열별 최대 쌓기 높이에 해당하는 임시 그림자 조각만 활성화한다.</summary>
        public void Refresh()
        {
            if (board == null)
            {
                return;
            }

            if (!projectionVisible)
            {
                SetAllPiecesActive(false);
                return;
            }

            if (useCompositePattern)
            {
                RefreshProgressiveComposite();
                return;
            }

            if (useDepthBandMapping &&
                pieceDepthBands.Count == shadowPieces.Count &&
                pieceBandOrdinals.Count == shadowPieces.Count)
            {
                RefreshRowBands();
                return;
            }

            bool hasSparseBindings = pieceColumns.Count == shadowPieces.Count &&
                                     pieceLevels.Count == shadowPieces.Count;
            if (hasSparseBindings)
            {
                for (int index = 0; index < shadowPieces.Count; index++)
                {
                    if (shadowPieces[index] != null)
                    {
                        int column = pieceColumns[index];
                        int level = pieceLevels[index];
                        shadowPieces[index].SetActive(column >= 0 && column < board.Width &&
                                                      level >= 0 && level < board.GetProjectedHeight(column));
                    }
                }

                return;
            }

            for (int column = 0; column < board.Width; column++)
            {
                int projectedHeight = board.GetProjectedHeight(column);
                for (int level = 0; level < maximumPreviewHeight; level++)
                {
                    int index = column * maximumPreviewHeight + level;
                    if (index < shadowPieces.Count && shadowPieces[index] != null)
                    {
                        shadowPieces[index].SetActive(level < projectedHeight);
                    }
                }
            }
        }

        /// <summary>조명이 켜졌을 때만 계산된 그림자 조각을 화면에 표시한다.</summary>
        public void SetProjectionVisible(bool visible)
        {
            projectionVisible = visible;
            if (visible)
            {
                Refresh();
            }
            else
            {
                SetAllPiecesActive(false);
            }
        }

        private void SetAllPiecesActive(bool active)
        {
            foreach (GameObject piece in shadowPieces)
            {
                if (piece != null)
                {
                    piece.SetActive(active);
                }
            }
        }

        private void RefreshProgressiveComposite()
        {
            if (!hasCompositeLayout)
            {
                SetAllPiecesActive(false);
                return;
            }

            int pieceIndex = 0;
            int columnCount = compositeUsesDepthAxis ? board.Depth : board.Width;
            int maximumLevel = board.GetMaximumStackHeight();
            for (int column = 0; column < columnCount; column++)
            {
                for (int level = 0; level < maximumLevel && pieceIndex < shadowPieces.Count; level++)
                {
                    if (!board.DoesProjectedCellCastShadow(column, level, compositeUsesDepthAxis))
                    {
                        continue;
                    }

                    GameObject piece = shadowPieces[pieceIndex++];
                    if (piece == null) continue;

                    float centeredColumn = column - (columnCount - 1) * 0.5f;
                    piece.transform.position = compositeOrigin +
                                               compositeHorizontalStep * centeredColumn +
                                               compositeVerticalStep * level;
                    piece.SetActive(true);
                }
            }

            for (; pieceIndex < shadowPieces.Count; pieceIndex++)
            {
                GameObject piece = shadowPieces[pieceIndex];
                if (piece != null)
                {
                    piece.SetActive(false);
                }
            }
        }

        private void ConfigureCompositeLayout(Camera projectionCamera)
        {
            hasCompositeLayout = projectionCamera != null && shadowPieces.Count > 0;
            if (!hasCompositeLayout)
            {
                return;
            }

            Vector3 viewDirection = Vector3.ProjectOnPlane(
                projectionCamera.transform.forward,
                board.transform.up).normalized;
            compositeUsesDepthAxis = Mathf.Abs(Vector3.Dot(viewDirection, board.transform.right)) >=
                                     Mathf.Abs(Vector3.Dot(viewDirection, board.transform.forward));

            Vector3 screenRight = projectionCamera.transform.right.normalized;
            Vector3 screenUp = projectionCamera.transform.up.normalized;
            Vector3 screenForward = projectionCamera.transform.forward.normalized;
            Vector3 center = Vector3.zero;
            float lowestVertical = float.PositiveInfinity;
            float horizontalSize = 0f;
            float verticalSize = 0f;
            int validPieceCount = 0;

            foreach (GameObject piece in shadowPieces)
            {
                if (piece == null) continue;

                center += piece.transform.position;
                lowestVertical = Mathf.Min(lowestVertical, Vector3.Dot(piece.transform.position, screenUp));
                Renderer renderer = piece.GetComponentInChildren<Renderer>(true);
                if (renderer != null)
                {
                    Vector3 extents = renderer.bounds.extents;
                    horizontalSize = Mathf.Max(horizontalSize, ProjectedBoundsSize(extents, screenRight));
                    verticalSize = Mathf.Max(verticalSize, ProjectedBoundsSize(extents, screenUp));
                }

                validPieceCount++;
            }

            if (validPieceCount == 0)
            {
                hasCompositeLayout = false;
                return;
            }

            center /= validPieceCount;
            horizontalSize = Mathf.Max(0.1f, horizontalSize);
            verticalSize = Mathf.Max(0.1f, verticalSize);
            float centerHorizontal = Vector3.Dot(center, screenRight);
            float centerDepth = Vector3.Dot(center, screenForward);
            compositeOrigin = screenRight * centerHorizontal +
                              screenUp * lowestVertical +
                              screenForward * centerDepth;
            compositeHorizontalStep = screenRight * horizontalSize;
            compositeVerticalStep = screenUp * verticalSize;
        }

        private static float ProjectedBoundsSize(Vector3 extents, Vector3 axis)
        {
            return 2f * (Mathf.Abs(axis.x) * extents.x +
                         Mathf.Abs(axis.y) * extents.y +
                         Mathf.Abs(axis.z) * extents.z);
        }

        /// <summary>모든 보드 셀을 유지한 채 3개 논리 행별 실제 큐브 수를 계산한다.</summary>
        private void RefreshRowBands()
        {
            int[] cubeCounts = new int[3];
            int rowCount = bandsUseWidthAxis ? board.Width : board.Depth;
            int crossCount = bandsUseWidthAxis ? board.Depth : board.Width;
            for (int row = 0; row < rowCount; row++)
            {
                int band = rowCount <= 1
                    ? 0
                    : Mathf.RoundToInt(row * 2f / (rowCount - 1));
                for (int cross = 0; cross < crossCount; cross++)
                {
                    Vector2Int cell = bandsUseWidthAxis
                        ? new Vector2Int(row, cross)
                        : new Vector2Int(cross, row);
                    cubeCounts[band] += board.GetHeight(cell);
                }
            }

            for (int index = 0; index < shadowPieces.Count; index++)
            {
                GameObject piece = shadowPieces[index];
                if (piece == null) continue;

                int band = Mathf.Clamp(pieceDepthBands[index], 0, 2);
                int ordinal = Mathf.Max(0, pieceBandOrdinals[index]);
                piece.SetActive(ordinal < cubeCounts[band]);
            }
        }
    }
}
