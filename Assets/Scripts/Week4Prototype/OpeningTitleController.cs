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

        [Header("씬 전환")]
        [SerializeField] private string targetSceneName = "KHP_Puzzles";
        [Min(0f)] [SerializeField] private float transitionDelay = 0.5f;

        private readonly List<TitleCube> titleCubes = new();
        private TitleCube activeCube;
        private Plane dragPlane;
        private Vector3 dragOffset;
        private Vector3 dragStartPosition;
        private Quaternion dragStartRotation;
        private Coroutine returnSequence;
        private bool isDragging;
        private bool isTransitioning;

        private sealed class TitleCube
        {
            public GameObject Object;
            public Rigidbody Body;
            public Collider Collider;
            public bool IsStart;
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

            if (isDragging && Mouse.current.leftButton.isPressed && dragPlane.Raycast(pointerRay, out float distance))
            {
                Vector3 planarPosition = pointerRay.GetPoint(distance) + dragOffset;
                if (TryGetPlacement(activeCube, planarPosition, snapToSupportCenter: false,
                        out Vector3 hoverPosition, out _))
                {
                    activeCube.Object.transform.position = hoverPosition;
                }
                else
                {
                    activeCube.Object.transform.position = planarPosition;
                }
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
                placementSurface = GameObject.Find("PuppeteersBoard")?.GetComponent<Collider>();
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
            }
        }

        private void RegisterTitleCube(GameObject cubeObject, DraggableCube draggable)
        {
            if (cubeObject == null || titleCubes.Exists(cube => cube.Object == cubeObject)) return;

            Collider cubeCollider = cubeObject.GetComponent<Collider>() ??
                                    cubeObject.GetComponentInChildren<Collider>();
            if (cubeCollider == null) return;

            if (draggable != null) draggable.enabled = false;
            Rigidbody body = cubeObject.GetComponent<Rigidbody>();
            bool isStart = cubeObject == startCube;
            ConfigureBody(body, freezeAll: isStart);
            titleCubes.Add(new TitleCube
            {
                Object = cubeObject,
                Body = body,
                Collider = cubeCollider,
                IsStart = isStart
            });
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
                if (selectedCube == null || selectedCube.IsStart) continue;

                activeCube = selectedCube;
                dragStartPosition = selectedCube.Object.transform.position;
                dragStartRotation = selectedCube.Object.transform.rotation;
                dragPlane = new Plane(Vector3.up, selectedCube.Object.transform.position);
                dragOffset = dragPlane.Raycast(pointerRay, out float distance)
                    ? selectedCube.Object.transform.position - pointerRay.GetPoint(distance)
                    : Vector3.zero;
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

            Vector3 currentPosition = activeCube.Object.transform.position;
            if (TryGetPlacement(activeCube, currentPosition, snapToSupportCenter: true,
                    out Vector3 placementPosition, out TitleCube supportCube))
            {
                TitleCube placedCube = activeCube;
                placedCube.Object.transform.position = placementPosition;
                activeCube = null;

                if (placedCube.Object == gameCube && supportCube != null && supportCube.Object == startCube)
                {
                    ConfigureBody(placedCube.Body, freezeAll: true);
                    StartCoroutine(LoadTargetScene());
                }
                return;
            }

            TitleCube returningCube = activeCube;
            activeCube = null;
            returnSequence = StartCoroutine(ReturnCube(returningCube, dragStartPosition, dragStartRotation));
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

            Bounds movingBounds = movingCube.Collider.bounds;
            Vector3 colliderCenterOffset = movingBounds.center - movingCube.Object.transform.position;
            Vector3 desiredBoundsCenter = desiredTransformPosition + colliderCenterOffset;
            float supportTop = float.NegativeInfinity;

            foreach (TitleCube candidate in titleCubes)
            {
                if (candidate == movingCube || candidate.Collider == null) continue;
                Bounds candidateBounds = candidate.Collider.bounds;
                if (!IsOverSupport(desiredBoundsCenter, candidateBounds)) continue;
                if (candidateBounds.max.y <= supportTop) continue;

                supportTop = candidateBounds.max.y;
                supportCube = candidate;
            }

            if (supportCube == null)
            {
                if (placementSurface == null || !IsOverSurface(desiredBoundsCenter, placementSurface.bounds))
                {
                    return false;
                }
                supportTop = placementSurface.bounds.max.y;
            }

            if (snapToSupportCenter && supportCube != null)
            {
                Bounds supportBounds = supportCube.Collider.bounds;
                desiredBoundsCenter.x = supportBounds.center.x;
                desiredBoundsCenter.z = supportBounds.center.z;
            }

            desiredBoundsCenter.y = supportTop + movingBounds.extents.y + stackGap;
            placementPosition = desiredBoundsCenter - colliderCenterOffset;
            return true;
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

        private IEnumerator ReturnCube(
            TitleCube cube,
            Vector3 targetPosition,
            Quaternion targetRotation)
        {
            Vector3 startPosition = cube.Object.transform.position;
            Quaternion startRotation = cube.Object.transform.rotation;
            float duration = Mathf.Max(0.01f, returnDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                cube.Object.transform.position = Vector3.Lerp(startPosition, targetPosition, progress);
                cube.Object.transform.rotation = Quaternion.Slerp(startRotation, targetRotation, progress);
                yield return null;
            }

            cube.Object.transform.SetPositionAndRotation(targetPosition, targetRotation);
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
