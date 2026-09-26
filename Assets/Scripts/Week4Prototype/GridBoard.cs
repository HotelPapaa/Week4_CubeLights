using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GameLab.Week4
{
    /// <summary>
    /// 5x3 격자의 좌표 변환과 각 칸에 쌓인 큐브 목록을 관리한다.
    /// width, depth, cellSize를 인스펙터에서 바꿀 수 있어 격자 크기를 확장할 수 있다.
    /// </summary>
    public sealed class GridBoard : MonoBehaviour
    {
        [Header("격자 크기")]
        [Min(1)] [SerializeField] private int width = 5;
        [Min(1)] [SerializeField] private int depth = 3;
        [Min(0.1f)] [SerializeField] private float cellSize = 1.15f;

        [Header("배치 높이")]
        [SerializeField] private float surfaceY = 0.12f;
        [Min(0.1f)] [SerializeField] private float cubeHeight = 1f;
        [Min(0f)] [SerializeField] private float snapMargin = 0.45f;

        // 좌표별 스택을 분리해 높이 제한 없이 큐브를 쌓는다.
        private readonly Dictionary<Vector2Int, List<DraggableCube>> stacks = new();
        private Coroutine lightEffectCoroutine;

        public int Width => width;
        public int Depth => depth;
        public float CellSize => cellSize;
        public float SurfaceY => surfaceY;
        public float CubeHeight => cubeHeight;

        public event Action BoardChanged;

        /// <summary>에디터 씬 생성기가 격자 설정을 한 번에 적용할 때 사용한다.</summary>
        public void Configure(int newWidth, int newDepth, float newCellSize, float newSurfaceY, float newCubeHeight)
        {
            width = Mathf.Max(1, newWidth);
            depth = Mathf.Max(1, newDepth);
            cellSize = Mathf.Max(0.1f, newCellSize);
            surfaceY = newSurfaceY;
            cubeHeight = Mathf.Max(0.1f, newCubeHeight);
            // 작은 기존 격자에서도 바깥 영역을 칸으로 오인하지 않도록 허용 거리를 셀 크기에 비례시킨다.
            snapMargin = cellSize * 0.35f;
        }

        /// <summary>스테이지를 다시 불러올 때 모든 배치 상태를 비우고 그림자·판정 시스템에 알린다.</summary>
        public void ResetBoard()
        {
            CancelLightEffects();
            stacks.Clear();
            BoardChanged?.Invoke();
        }

        /// <summary>격자 좌표의 월드 중심점을 반환한다.</summary>
        public Vector3 CellToWorld(Vector2Int cell, int stackIndex)
        {
            float x = (cell.x - (width - 1) * 0.5f) * cellSize;
            float z = (cell.y - (depth - 1) * 0.5f) * cellSize;
            float y = surfaceY + cubeHeight * (stackIndex + 0.5f);
            return transform.TransformPoint(new Vector3(x, y, z));
        }

        /// <summary>
        /// 카메라의 마우스 광선을 격자의 바닥 높이에 투영한다.
        /// 드래그 계산을 고정된 공중 평면이 아닌 실제 보드 기준으로 처리하기 위해 사용한다.
        /// </summary>
        public bool TryProjectPointerToSurface(Ray pointerRay, out Vector3 surfacePoint)
        {
            Vector3 planePoint = transform.TransformPoint(new Vector3(0f, surfaceY, 0f));
            Plane boardPlane = new Plane(transform.up, planePoint);
            if (boardPlane.Raycast(pointerRay, out float distance))
            {
                surfacePoint = pointerRay.GetPoint(distance);
                return true;
            }

            surfacePoint = default;
            return false;
        }

        /// <summary>
        /// 커서가 가리키는 칸에 지금 큐브를 놓았을 때의 층 중심 높이를 반환한다.
        /// 빈 칸은 1층, 큐브가 하나 있으면 2층 높이가 된다.
        /// </summary>
        public bool TryGetPlacementPreview(Vector3 surfacePoint, out Vector2Int cell, out Vector3 placementPosition)
        {
            if (TryGetNearestCell(surfacePoint, out cell))
            {
                placementPosition = CellToWorld(cell, GetHeight(cell));
                return true;
            }

            placementPosition = GetLooseDragPosition(surfacePoint);
            return false;
        }

        /// <summary>격자 밖에서 드래그할 때도 큐브가 테이블의 1층 중심 높이를 유지하게 한다.</summary>
        public Vector3 GetLooseDragPosition(Vector3 surfacePoint)
        {
            return surfacePoint + transform.up * (cubeHeight * 0.5f);
        }

        /// <summary>마우스가 놓인 월드 위치에서 가장 가까운 유효 격자 칸을 찾는다.</summary>
        public bool TryGetNearestCell(Vector3 worldPosition, out Vector2Int cell)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            int x = Mathf.RoundToInt(local.x / cellSize + (width - 1) * 0.5f);
            int z = Mathf.RoundToInt(local.z / cellSize + (depth - 1) * 0.5f);
            cell = new Vector2Int(x, z);

            if (!IsInside(cell))
            {
                return false;
            }

            Vector3 center = transform.InverseTransformPoint(CellToWorld(cell, 0));
            float allowedDistance = cellSize * 0.5f + snapMargin;
            return Mathf.Abs(local.x - center.x) <= allowedDistance &&
                   Mathf.Abs(local.z - center.z) <= allowedDistance;
        }

        /// <summary>큐브를 해당 칸의 맨 위에 등록하고 스냅 위치를 반환한다.</summary>
        public Vector3 PlaceCube(DraggableCube cube, Vector2Int cell)
        {
            if (!stacks.TryGetValue(cell, out List<DraggableCube> stack))
            {
                stack = new List<DraggableCube>();
                stacks.Add(cell, stack);
            }

            // 새 큐브 바로 아래의 바사삭 큐브는 스티로폼이 올라온 경우를 제외하고 파괴된다.
            if (stack.Count > 0)
            {
                DraggableCube supportCube = stack[stack.Count - 1];
                PuzzleCubeProperties supportProperties = supportCube.GetComponent<PuzzleCubeProperties>();
                PuzzleCubeProperties incomingProperties = cube.GetComponent<PuzzleCubeProperties>();
                if (supportProperties != null && supportProperties.BreaksUnderWeight &&
                    (incomingProperties == null || !incomingProperties.IsStyrofoam))
                {
                    supportProperties.Break();
                    if (!stacks.TryGetValue(cell, out stack))
                    {
                        stack = new List<DraggableCube>();
                        stacks.Add(cell, stack);
                    }
                }
            }

            // 중복 등록을 막아 같은 큐브가 높이에 두 번 계산되지 않게 한다.
            if (!stack.Contains(cube))
            {
                stack.Add(cube);
            }

            BoardChanged?.Invoke();
            return CellToWorld(cell, stack.IndexOf(cube));
        }

        /// <summary>이동을 시작한 큐브를 기존 스택에서 제거하고 위 큐브들을 내려 정렬한다.</summary>
        public void RemoveCube(DraggableCube cube, Vector2Int cell, bool animateCollapse = false)
        {
            if (!stacks.TryGetValue(cell, out List<DraggableCube> stack) || !stack.Remove(cube))
            {
                return;
            }

            for (int index = 0; index < stack.Count; index++)
            {
                Vector3 targetPosition = CellToWorld(cell, index);
                if (animateCollapse)
                {
                    stack[index].FallTo(targetPosition);
                }
                else
                {
                    stack[index].SnapImmediately(targetPosition);
                }
            }

            if (stack.Count == 0)
            {
                stacks.Remove(cell);
            }

            BoardChanged?.Invoke();
        }

        /// <summary>지정한 칸에 쌓인 큐브 수를 반환한다.</summary>
        public int GetHeight(Vector2Int cell)
        {
            return stacks.TryGetValue(cell, out List<DraggableCube> stack) ? stack.Count : 0;
        }

        /// <summary>빛의 방향에서 같은 열을 보았을 때 나타나는 최대 실루엣 높이를 계산한다.</summary>
        public int GetProjectedHeight(int column)
        {
            int maximum = 0;
            for (int row = 0; row < depth; row++)
            {
                maximum = Mathf.Max(maximum, GetHeight(new Vector2Int(column, row)));
            }

            return maximum;
        }

        /// <summary>
        /// 지정한 투영축에서 보이는 열의 높이를 반환한다.
        /// depth 축을 사용하면 X 방향을 겹쳐 보고 Z 방향의 실루엣을 만든다.
        /// </summary>
        public int GetProjectedHeight(int column, bool useDepthAxis)
        {
            if (!useDepthAxis)
            {
                return GetProjectedHeight(column);
            }

            int maximum = 0;
            for (int x = 0; x < width; x++)
            {
                maximum = Mathf.Max(maximum, GetHeight(new Vector2Int(x, column)));
            }

            return maximum;
        }

        /// <summary>현재 모든 칸 중 가장 높은 실제 스택 높이를 반환한다.</summary>
        public int GetMaximumStackHeight()
        {
            int maximum = 0;
            foreach (List<DraggableCube> stack in stacks.Values)
            {
                maximum = Mathf.Max(maximum, stack.Count);
            }

            return maximum;
        }

        /// <summary>
        /// 투영된 특정 열·층에 그림자를 만드는 큐브가 하나라도 있는지 확인한다.
        /// 유리처럼 자리는 차지하지만 그림자를 만들지 않는 큐브는 건너뛴다.
        /// </summary>
        public bool DoesProjectedCellCastShadow(int column, int level, bool useDepthAxis)
        {
            int collapsedAxisCount = useDepthAxis ? width : depth;
            for (int collapsed = 0; collapsed < collapsedAxisCount; collapsed++)
            {
                Vector2Int cell = useDepthAxis
                    ? new Vector2Int(collapsed, column)
                    : new Vector2Int(column, collapsed);
                if (!stacks.TryGetValue(cell, out List<DraggableCube> stack) || level >= stack.Count)
                {
                    continue;
                }

                DraggableCube cube = stack[level];
                PuzzleCubeProperties properties = cube != null
                    ? cube.GetComponent<PuzzleCubeProperties>()
                    : null;
                if (cube != null && (properties == null || properties.CastsShadow))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>점등 후 1초 동안 얼음을 유지한 뒤 동시에 녹이고 완료 콜백을 호출한다.</summary>
        public void ApplyLightEffects(Action onCompleted = null)
        {
            CancelLightEffects();
            var placedCubes = new List<DraggableCube>();
            foreach (List<DraggableCube> stack in stacks.Values)
            {
                placedCubes.AddRange(stack);
            }

            var meltingCubes = new List<PuzzleCubeProperties>();
            float delay = 0f;
            foreach (DraggableCube cube in placedCubes)
            {
                if (cube != null && cube.TryGetComponent(out PuzzleCubeProperties properties) &&
                    properties.MeltsInLight)
                {
                    meltingCubes.Add(properties);
                    delay = Mathf.Max(delay, properties.IceMeltDelay);
                }
            }

            if (meltingCubes.Count == 0)
            {
                onCompleted?.Invoke();
                return;
            }

            lightEffectCoroutine = StartCoroutine(MeltIceAfterDelay(meltingCubes, delay, onCompleted));
        }

        public void CancelLightEffects()
        {
            if (lightEffectCoroutine == null) return;

            StopCoroutine(lightEffectCoroutine);
            lightEffectCoroutine = null;
        }

        private IEnumerator MeltIceAfterDelay(
            List<PuzzleCubeProperties> meltingCubes,
            float delay,
            Action onCompleted)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            foreach (PuzzleCubeProperties properties in meltingCubes)
            {
                if (properties != null && properties.gameObject.activeInHierarchy)
                {
                    properties.ApplyLightEffectNow();
                }
            }

            lightEffectCoroutine = null;
            onCompleted?.Invoke();
        }

        private bool IsInside(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < depth;
        }
    }
}
