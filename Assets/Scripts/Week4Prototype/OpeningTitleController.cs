using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace GameLab.Week4
{
    /// <summary>
    /// 오프닝 타이틀의 큐브를 자유롭게 드래그하고 서로 쌓는다.
    /// Game 큐브를 Start 큐브 바로 위에 놓으면 지정한 씬으로 이동한다.
    /// </summary>
    public sealed class OpeningTitleController : MonoBehaviour
    {
        [Header("씬 오브젝트")]
        [SerializeField] private Camera inputCamera;
        [SerializeField] private GameObject gameCube;
        [SerializeField] private GameObject startCube;
        [SerializeField] private Collider placementSurface;

        [Header("배치")]
        [Min(0f)] [SerializeField] private float placementMargin = 0.04f;
        [Min(0f)] [SerializeField] private float stackGap = 0.002f;
        [Min(0.01f)] [SerializeField] private float returnDuration = 0.2f;
        [Min(0f)] [SerializeField] private float dragLiftHeight = 0.08f;
        [Min(0.01f)] [SerializeField] private float dropDuration = 0.18f;
        [Min(0f)] [SerializeField] private float fallbackSurfacePadding = 0.25f;

        [Header("씬 전환")]
        [SerializeField] private string targetSceneName = "KHP_Puzzles";
        [Min(0f)] [SerializeField] private float transitionDelay = 0.5f;

        private readonly List<TitleCube> titleCubes = new();
        private readonly HashSet<TitleCube> blockedSupports = new();
        private TitleCube activeCube;
        private Vector2 dragStartPointer;
        private Vector3 dragRightAxis;
        private Vector3 dragForwardAxis;
        private float dragUnitsPerPixelX;
        private float dragUnitsPerPixelY;
        private float dragHeldBaseY;
        private Vector3 dragStartPosition;
        private Quaternion dragStartRotation;
        private Coroutine returnSequence;
        private bool isDragging;
        private bool isTransitioning;
        private Bounds fallbackSurfaceBounds;
        private float fallbackSurfaceTop;
        private bool hasFallbackSurface;

        private sealed class TitleCube
        {
            public GameObject Object;
            public Rigidbody Body;
            public Collider Collider;
            public Vector3 LocalBoundsCenter;
            public Vector3 BoundsExtents;
        }

        private void Awake()
        {
            ResolveReferences();
            ConfigureTitleCubes();
        }

        private void Update()
        {
            if (isTransitioning || inputCamera == null || Mouse.current == null) return;

            Ray pointerRay = inputCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!isDragging && returnSequence == null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                TryBeginDrag(pointerRay);
            }

            if (isDragging && Mouse.current.leftButton.isPressed)
            {
                Vector2 pointer = Mouse.current.position.ReadValue();
                Vector2 pointerDelta = pointer - dragStartPointer;
                Vector3 planarPosition = dragStartPosition +
                                         dragRightAxis * (pointerDelta.x * dragUnitsPerPixelX) +
                                         dragForwardAxis * (pointerDelta.y * dragUnitsPerPixelY);
                planarPosition.y = dragStartPosition.y;

                Vector3 heldPosition = planarPosition;
                heldPosition.y = dragHeldBaseY;
                if (TryGetPlacement(activeCube, planarPosition, snapToSupportCenter: false,
                        out Vector3 hoverPosition, out _))
                {
                    // 높은 받침을 지날 때도 큐브가 받침을 관통하지 않도록
                    // 착지 위치보다 집어 든 높이만큼 위에 유지한다.
                    heldPosition.y = Mathf.Max(heldPosition.y, hoverPosition.y + dragLiftHeight);
                }

                SetCubePosition(activeCube, heldPosition);
            }

            if (isDragging && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                EndDrag();
            }
        }

        private void ResolveReferences()
        {
            if (inputCamera == null) inputCamera = Camera.main;
            if (gameCube == null) gameCube = GameObject.Find("Game");
            if (startCube == null) startCube = GameObject.Find("Start");
            if (placementSurface == null)
            {
                GameObject board = GameObject.Find("PuppeteersBoard");
                placementSurface = board != null
                    ? board.GetComponent<BoxCollider>() ?? board.GetComponent<Collider>()
                    : null;
            }
        }

        private void ConfigureTitleCubes()
        {
            if (gameCube == null || startCube == null)
            {
                Debug.LogError("OpeningTitleController: Game 또는 Start 큐브 연결을 확인하세요.", this);
                enabled = false;
                return;
            }

            DraggableCube[] puzzleCubes = FindObjectsByType<DraggableCube>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            foreach (DraggableCube draggable in puzzleCubes)
            {
                RegisterTitleCube(draggable.gameObject, draggable);
            }

            // 프리팹 구성이 바뀌어도 핵심 두 큐브는 반드시 등록한다.
            RegisterTitleCube(gameCube, gameCube.GetComponent<DraggableCube>());
            RegisterTitleCube(startCube, startCube.GetComponent<DraggableCube>());

            if (titleCubes.Find(cube => cube.Object == gameCube) == null ||
                titleCubes.Find(cube => cube.Object == startCube) == null)
            {
                Debug.LogError("OpeningTitleController: Game 또는 Start 큐브의 Collider를 찾지 못했습니다.", this);
                enabled = false;
                return;
            }

            ConfigureFallbackSurface();
        }

        private void RegisterTitleCube(GameObject cubeObject, DraggableCube draggable)
        {
            if (cubeObject == null || titleCubes.Exists(cube => cube.Object == cubeObject)) return;

            Collider cubeCollider = cubeObject.GetComponent<Collider>() ??
                                    cubeObject.GetComponentInChildren<Collider>();
            if (cubeCollider == null) return;

            if (draggable != null) draggable.enabled = false;
            Rigidbody body = cubeObject.GetComponent<Rigidbody>();
            ConfigureBody(body, freezeAll: false);
            Bounds initialBounds = cubeCollider.bounds;
            titleCubes.Add(new TitleCube
            {
                Object = cubeObject,
                Body = body,
                Collider = cubeCollider,
                LocalBoundsCenter = cubeObject.transform.InverseTransformPoint(initialBounds.center),
                BoundsExtents = initialBounds.extents
            });
        }

        private void ConfigureFallbackSurface()
        {
            if (titleCubes.Count == 0) return;

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;
            fallbackSurfaceTop = float.PositiveInfinity;

            foreach (TitleCube cube in titleCubes)
            {
                Bounds bounds = GetCurrentBounds(cube);
                minX = Mathf.Min(minX, bounds.min.x);
                maxX = Mathf.Max(maxX, bounds.max.x);
                minZ = Mathf.Min(minZ, bounds.min.z);
                maxZ = Mathf.Max(maxZ, bounds.max.z);
                fallbackSurfaceTop = Mathf.Min(fallbackSurfaceTop, bounds.min.y);
            }

            float padding = Mathf.Max(0f, fallbackSurfacePadding);
            minX -= padding;
            maxX += padding;
            minZ -= padding;
            maxZ += padding;
            fallbackSurfaceBounds = new Bounds(
                new Vector3((minX + maxX) * 0.5f, fallbackSurfaceTop, (minZ + maxZ) * 0.5f),
                new Vector3(maxX - minX, 0f, maxZ - minZ));
            hasFallbackSurface = true;
        }

        private static void ConfigureBody(Rigidbody body, bool freezeAll)
        {
            if (body == null) return;

            // Unity does not allow velocity writes while a Rigidbody is kinematic.
            // Title cubes are normally already kinematic, so only clear residual
            // physics velocity when the body is still dynamic.
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.useGravity = false;
            body.isKinematic = true;
            body.constraints = freezeAll ? RigidbodyConstraints.FreezeAll : RigidbodyConstraints.None;
        }

        private void TryBeginDrag(Ray pointerRay)
        {
            RaycastHit[] hits = Physics.RaycastAll(pointerRay);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                TitleCube selectedCube = FindTitleCube(hit.collider);
                if (selectedCube == null) continue;

                activeCube = selectedCube;
                dragStartPosition = selectedCube.Object.transform.position;
                dragStartRotation = selectedCube.Object.transform.rotation;
                dragStartPointer = Mouse.current.position.ReadValue();
                ConfigureDragAxes(selectedCube);
                BuildBlockedSupports(selectedCube);
                ConfigureBody(selectedCube.Body, freezeAll: false);
                isDragging = true;
                return;
            }
        }

        private TitleCube FindTitleCube(Collider hitCollider)
        {
            foreach (TitleCube cube in titleCubes)
            {
                if (hitCollider == cube.Collider || hitCollider.transform.IsChildOf(cube.Object.transform))
                {
                    return cube;
                }
            }

            return null;
        }

        private void EndDrag()
        {
            isDragging = false;
            if (activeCube == null) return;

            Vector3 currentPosition = GetCubePosition(activeCube);
            if (TryGetPlacement(activeCube, currentPosition, snapToSupportCenter: true,
                    out Vector3 placementPosition, out TitleCube supportCube))
            {
                TitleCube placedCube = activeCube;
                activeCube = null;
                blockedSupports.Clear();

                bool startsGame = placedCube.Object == gameCube &&
                                  supportCube != null &&
                                  supportCube.Object == startCube;
                returnSequence = StartCoroutine(
                    DropCube(placedCube, placementPosition, startsGame));
                return;
            }

            TitleCube returningCube = activeCube;
            activeCube = null;
            blockedSupports.Clear();
            returnSequence = StartCoroutine(ReturnCube(returningCube, dragStartPosition, dragStartRotation));
        }

        private IEnumerator DropCube(TitleCube cube, Vector3 targetPosition, bool startsGame)
        {
            Vector3 startPosition = GetCubePosition(cube);
            Quaternion targetRotation = GetCubeRotation(cube);
            float duration = Mathf.Max(0.01f, dropDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / duration);
                float easedTime = normalizedTime * normalizedTime;
                SetCubePosition(cube, Vector3.LerpUnclamped(startPosition, targetPosition, easedTime));
                yield return null;
            }

            SetCubePose(cube, targetPosition, targetRotation);
            returnSequence = null;

            if (startsGame)
            {
                ConfigureBody(cube.Body, freezeAll: true);
            }

            if (!startsGame) yield break;

            StartCoroutine(LoadTargetScene());
        }

        private void BuildBlockedSupports(TitleCube movingCube)
        {
            blockedSupports.Clear();
            Bounds movingBounds = GetCurrentBounds(movingCube);

            // 현재 큐브와 같은 기둥에서 위에 놓인 큐브를 다시 받침으로 고르면
            // 집는 순간 기둥 꼭대기로 순간 이동한다. 드래그가 끝날 때까지 그 큐브들을 제외한다.
            foreach (TitleCube candidate in titleCubes)
            {
                if (candidate == movingCube) continue;
                Bounds candidateBounds = GetCurrentBounds(candidate);
                if (candidateBounds.center.y <= movingBounds.center.y + stackGap) continue;
                if (!FootprintsOverlap(movingBounds, candidateBounds)) continue;
                blockedSupports.Add(candidate);
            }
        }

        private void ConfigureDragAxes(TitleCube selectedCube)
        {
            // 화면 가로 이동은 카메라의 수평축, 화면 세로 이동은 카메라가 바라보는
            // 바닥 방향에 대응시킨다. 무한 평면 Ray 교차를 사용하지 않아 깊이 이동도 안정적이다.
            dragForwardAxis = Vector3.ProjectOnPlane(inputCamera.transform.forward, Vector3.up);
            if (dragForwardAxis.sqrMagnitude < 0.001f)
            {
                dragForwardAxis = Vector3.forward;
            }
            else
            {
                dragForwardAxis.Normalize();
            }

            dragRightAxis = Vector3.Cross(Vector3.up, dragForwardAxis).normalized;

            float pixelHeight = Mathf.Max(1f, inputCamera.pixelHeight);
            float pixelWidth = Mathf.Max(1f, inputCamera.pixelWidth);
            float verticalWorldSize;
            if (inputCamera.orthographic)
            {
                verticalWorldSize = inputCamera.orthographicSize * 2f;
            }
            else
            {
                float depth = Vector3.Dot(
                    GetCubePosition(selectedCube) - inputCamera.transform.position,
                    inputCamera.transform.forward);
                depth = Mathf.Max(inputCamera.nearClipPlane + 0.01f, depth);
                verticalWorldSize = 2f * depth *
                                    Mathf.Tan(inputCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            }

            dragUnitsPerPixelY = verticalWorldSize / pixelHeight;
            dragUnitsPerPixelX = verticalWorldSize * inputCamera.aspect / pixelWidth;
            dragHeldBaseY = dragStartPosition.y + Mathf.Max(0f, dragLiftHeight);
        }

        private bool TryGetPlacement(
            TitleCube movingCube,
            Vector3 desiredTransformPosition,
            bool snapToSupportCenter,
            out Vector3 placementPosition,
            out TitleCube supportCube)
        {
            placementPosition = desiredTransformPosition;
            supportCube = null;
            if (movingCube == null || movingCube.Collider == null) return false;

            Bounds movingBounds = GetCurrentBounds(movingCube);
            Vector3 colliderCenterOffset = movingBounds.center - GetCubePosition(movingCube);
            Vector3 desiredBoundsCenter = desiredTransformPosition + colliderCenterOffset;
            float supportTop = float.NegativeInfinity;

            foreach (TitleCube candidate in titleCubes)
            {
                if (candidate == movingCube || candidate.Collider == null || blockedSupports.Contains(candidate))
                {
                    continue;
                }

                Bounds candidateBounds = GetCurrentBounds(candidate);
                if (!IsOverSupport(desiredBoundsCenter, candidateBounds)) continue;
                if (candidateBounds.max.y <= supportTop) continue;

                supportTop = candidateBounds.max.y;
                supportCube = candidate;
            }

            if (supportCube == null)
            {
                if (!TryGetSurfaceTop(desiredBoundsCenter, out supportTop))
                {
                    return false;
                }
            }

            if (snapToSupportCenter && supportCube != null)
            {
                Bounds supportBounds = GetCurrentBounds(supportCube);
                desiredBoundsCenter.x = supportBounds.center.x;
                desiredBoundsCenter.z = supportBounds.center.z;
            }

            // 바닥에는 정확히 맞닿게 하고, 큐브끼리 쌓을 때만 겹침 방지 간격을 둔다.
            float contactGap = supportCube != null ? stackGap : 0f;
            desiredBoundsCenter.y = supportTop + movingBounds.extents.y + contactGap;
            placementPosition = desiredBoundsCenter - colliderCenterOffset;
            return true;
        }

        private bool TryGetSurfaceTop(Vector3 movingCenter, out float surfaceTop)
        {
            if (placementSurface != null && IsOverSurface(movingCenter, placementSurface.bounds))
            {
                Bounds surfaceBounds = placementSurface.bounds;
                float rayPadding = Mathf.Max(1f, surfaceBounds.size.y + 0.5f);
                Ray downwardRay = new Ray(
                    new Vector3(movingCenter.x, surfaceBounds.max.y + rayPadding, movingCenter.z),
                    Vector3.down);

                if (placementSurface.Raycast(
                        downwardRay,
                        out RaycastHit surfaceHit,
                        surfaceBounds.size.y + rayPadding * 2f))
                {
                    surfaceTop = surfaceHit.point.y;
                    return true;
                }
            }

            if (hasFallbackSurface && IsOverSurface(movingCenter, fallbackSurfaceBounds))
            {
                surfaceTop = fallbackSurfaceTop;
                return true;
            }

            surfaceTop = 0f;
            return false;
        }

        private static Bounds GetCurrentBounds(TitleCube cube)
        {
            Vector3 scaledLocalCenter = Vector3.Scale(
                cube.LocalBoundsCenter,
                cube.Object.transform.lossyScale);
            Vector3 center = GetCubePosition(cube) + GetCubeRotation(cube) * scaledLocalCenter;
            return new Bounds(center, cube.BoundsExtents * 2f);
        }

        private static Vector3 GetCubePosition(TitleCube cube)
        {
            return cube.Body != null ? cube.Body.position : cube.Object.transform.position;
        }

        private static Quaternion GetCubeRotation(TitleCube cube)
        {
            return cube.Body != null ? cube.Body.rotation : cube.Object.transform.rotation;
        }

        private bool FootprintsOverlap(Bounds left, Bounds right)
        {
            return Mathf.Abs(left.center.x - right.center.x) <=
                   left.extents.x + right.extents.x + placementMargin &&
                   Mathf.Abs(left.center.z - right.center.z) <=
                   left.extents.z + right.extents.z + placementMargin;
        }

        private bool IsOverSupport(Vector3 movingCenter, Bounds supportBounds)
        {
            return Mathf.Abs(movingCenter.x - supportBounds.center.x) <= supportBounds.extents.x + placementMargin &&
                   Mathf.Abs(movingCenter.z - supportBounds.center.z) <= supportBounds.extents.z + placementMargin;
        }

        private static bool IsOverSurface(Vector3 movingCenter, Bounds surfaceBounds)
        {
            return movingCenter.x >= surfaceBounds.min.x && movingCenter.x <= surfaceBounds.max.x &&
                   movingCenter.z >= surfaceBounds.min.z && movingCenter.z <= surfaceBounds.max.z;
        }

        private static void SetCubePosition(TitleCube cube, Vector3 position)
        {
            if (cube.Body != null)
            {
                cube.Body.position = position;
            }
            else
            {
                cube.Object.transform.position = position;
            }
        }

        private static void SetCubePose(TitleCube cube, Vector3 position, Quaternion rotation)
        {
            if (cube.Body != null)
            {
                cube.Body.position = position;
                cube.Body.rotation = rotation;
            }
            else
            {
                cube.Object.transform.SetPositionAndRotation(position, rotation);
            }
        }

        private IEnumerator ReturnCube(
            TitleCube cube,
            Vector3 targetPosition,
            Quaternion targetRotation)
        {
            Vector3 startPosition = GetCubePosition(cube);
            Quaternion startRotation = GetCubeRotation(cube);
            float duration = Mathf.Max(0.01f, returnDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                SetCubePose(
                    cube,
                    Vector3.Lerp(startPosition, targetPosition, progress),
                    Quaternion.Slerp(startRotation, targetRotation, progress));
                yield return null;
            }

            SetCubePose(cube, targetPosition, targetRotation);
            returnSequence = null;
        }

        private IEnumerator LoadTargetScene()
        {
            isTransitioning = true;
            float delay = Mathf.Max(0f, transitionDelay);
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

            if (string.IsNullOrWhiteSpace(targetSceneName) ||
                !Application.CanStreamedLevelBeLoaded(targetSceneName))
            {
                Debug.LogError(
                    $"OpeningTitleController: '{targetSceneName}' 씬을 불러올 수 없습니다. Build Settings를 확인하세요.",
                    this);
                isTransitioning = false;
                yield break;
            }

            SceneManager.LoadScene(targetSceneName);
        }
    }
}
