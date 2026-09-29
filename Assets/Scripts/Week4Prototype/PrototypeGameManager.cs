using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameLab.Week4
{
    /// <summary>마우스 드래그, 선택 큐브 회전, 승리 판정을 한 곳에서 처리한다.</summary>
    public sealed class PrototypeGameManager : MonoBehaviour
    {
        [SerializeField] private GridBoard board;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private int[] targetProjection = { 1, 2, 3, 2, 1 };
        [SerializeField] private bool showInstructionOverlay = true;
        [SerializeField] private LightPuzzleVisualizer lightVisualizer;

        [Header("무르기")]
        [Min(1)] [SerializeField] private int undoCapacity = 30;

        private DraggableCube selectedCube;
        private DraggableCube hoveredCube;
        private PuzzleStageDefinition currentStage;
        private LightSimulationResult lastLightResult;
        private readonly HashSet<string> latchedLampKeys = new();
        private bool isDragging;
        private bool hasWon;
        private bool interactionEnabled = true;
        private readonly Stack<BoardUndoSnapshot> undoHistory = new();
        private BoardUndoSnapshot pendingDragSnapshot;

        private sealed class CubeUndoState
        {
            public DraggableCube Cube;
            public bool WasActive;
            public bool WasRemovedByRule;
            public bool WasPlaced;
            public Vector2Int Cell;
            public int StackIndex;
            public Vector3 WorldPosition;
            public Quaternion Rotation;
        }

        private sealed class BoardUndoSnapshot
        {
            public readonly List<CubeUndoState> Cubes = new();
        }

        public bool IsCurrentStageSolved => hasWon;
        public LightSimulationResult LastLightResult => lastLightResult;
        public Camera PlayerCamera => playerCamera;
        public bool CanOrbitCamera => interactionEnabled && !isDragging;

        public void Initialize(GridBoard targetBoard, Camera targetCamera, int[] target)
        {
            if (board != null)
            {
                board.BoardChanged -= InvalidateLightResult;
            }

            board = targetBoard;
            playerCamera = targetCamera;
            targetProjection = target;

            if (isActiveAndEnabled && board != null)
            {
                board.BoardChanged -= InvalidateLightResult;
                board.BoardChanged += InvalidateLightResult;
                InvalidateLightResult();
            }
        }

        /// <summary>분위기 연출이 완성된 씬에서는 프로토타입 안내 UI를 숨길 수 있다.</summary>
        public void SetInstructionOverlayVisible(bool visible)
        {
            showInstructionOverlay = visible;
        }

        /// <summary>StageManager가 새 전등 목표를 전달하고 표시 시스템을 같은 보드에 연결한다.</summary>
        public void ConfigureStage(PuzzleStageDefinition stage)
        {
            ClearUndoHistory();
            currentStage = stage;
            lastLightResult = null;
            latchedLampKeys.Clear();
            hasWon = false;

            if (lightVisualizer == null)
            {
                lightVisualizer = GetComponent<LightPuzzleVisualizer>() ??
                                  gameObject.AddComponent<LightPuzzleVisualizer>();
            }

            lightVisualizer.Initialize(board);
            lightVisualizer.ConfigureStage(stage);
            lightVisualizer.SetBeamsVisible(false);
        }

        /// <summary>스페이스바 전환 시 논리 광선 표시만 켜고 끈다.</summary>
        public void SetLightVisualizationVisible(bool visible)
        {
            lightVisualizer?.SetBeamsVisible(visible);
        }

        /// <summary>
        /// 조명 화면을 닫을 때 이번 점등에서 켜진 전등과 광선만 초기화한다.
        /// 이미 달성한 스테이지 클리어 기록은 유지한다.
        /// </summary>
        public void ResetLightAttempt()
        {
            latchedLampKeys.Clear();
            lastLightResult = null;
            lightVisualizer?.ResetLightAttempt();
        }

        /// <summary>점등 결과 화면에 들어가기 직전 Stage Camera를 5x3 전등판 정면으로 맞춘다.</summary>
        public void PrepareStageCamera(Camera stageCamera)
        {
            lightVisualizer?.FocusStageCamera(stageCamera);
        }

        /// <summary>조명 연출이 시작되면 현재 배치를 확정하고 큐브 추가 조작을 잠근다.</summary>
        public void SetInteractionEnabled(bool enabled)
        {
            if (!enabled && isDragging && selectedCube != null)
            {
                selectedCube.EndDrag();
                isDragging = false;
                CommitUndoSnapshot(pendingDragSnapshot);
                pendingDragSnapshot = null;
            }

            interactionEnabled = enabled;
        }

        /// <summary>광선을 계산하고, 빛에 닿은 얼음의 지연 용해가 끝날 때까지 연쇄 반응을 실행한다.</summary>
        public void ApplyLightEffects(Action onCompleted = null)
        {
            if (board != null)
            {
                board.ApplyLightEffects(
                    currentStage,
                    HandleLightSimulationStep,
                    _ => onCompleted?.Invoke());
            }
            else
            {
                onCompleted?.Invoke();
            }
        }

        public void CancelLightEffects()
        {
            board?.CancelLightEffects();
        }

        private void OnEnable()
        {
            if (board != null)
            {
                board.BoardChanged += InvalidateLightResult;
            }
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.BoardChanged -= InvalidateLightResult;
            }
        }

        private void Update()
        {
            if (!interactionEnabled || Mouse.current == null || Keyboard.current == null || playerCamera == null)
            {
                return;
            }

            HandleMouse();
            RefreshHoveredCube();
            HandleRotation();
        }

        /// <summary>클릭한 큐브를 선택하고 수평 드래그 평면을 따라 이동시킨다.</summary>
        private void HandleMouse()
        {
            Vector2 pointer = Mouse.current.position.ReadValue();

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (TryGetCubeUnderPointer(pointer, out DraggableCube cube))
                {
                    pendingDragSnapshot = CaptureBoardState();
                    selectedCube = cube;
                    SoundManager.Play(SoundEventId.CubePickup, selectedCube.transform.position);
                    selectedCube.BeginDrag();
                    isDragging = true;
                }
            }

            if (isDragging && selectedCube != null && Mouse.current.leftButton.isPressed)
            {
                Ray ray = playerCamera.ScreenPointToRay(pointer);
                if (board != null && board.TryProjectPointerToSurface(ray, out Vector3 surfacePoint))
                {
                    // 커서가 가리키는 스택의 다음 층 높이를 드래그 중에도 그대로 미리 보여준다.
                    if (board.TryGetPlacementPreview(surfacePoint, out _, out Vector3 placementPosition))
                    {
                        // X/Z도 칸 중심에 맞춰 실제 드롭 결과와 드래그 미리보기가 일치한다.
                        selectedCube.DragTo(placementPosition);
                    }
                    else
                    {
                        // 격자 밖에서는 마우스의 X/Z를 따라가되 테이블 1층 높이를 유지한다.
                        selectedCube.DragTo(board.GetLooseDragPosition(surfacePoint));
                    }
                }
            }

            if (isDragging && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                if (selectedCube != null)
                {
                    bool placedOnBoard = selectedCube.EndDrag();
                    SoundManager.Play(
                        placedOnBoard ? SoundEventId.CubePlace : SoundEventId.CubeReturn,
                        selectedCube.transform.position);
                }

                isDragging = false;
                CommitUndoSnapshot(pendingDragSnapshot);
                pendingDragSnapshot = null;
            }
        }

        /// <summary>
        /// 커서를 올린 큐브에 W/S는 화면 기준 앞뒤, A/D는 좌우로 90도 회전한다.
        /// Q/E는 보드 수직축을 중심으로 횡 방향 90도 회전한다.
        /// 카메라가 월드축과 비스듬해도 플레이어가 보는 방향과 입력 방향이 일치한다.
        /// </summary>
        private void HandleRotation()
        {
            if (hoveredCube == null)
            {
                return;
            }

            Vector3 cameraForward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
            Vector3 cameraRight = Vector3.ProjectOnPlane(playerCamera.transform.right, Vector3.up).normalized;
            Vector3 boardUp = board != null ? board.transform.up : Vector3.up;

            // 카메라가 정확히 수직을 보는 예외에서도 회전축이 0이 되지 않게 월드축을 사용한다.
            if (cameraForward.sqrMagnitude < 0.001f) cameraForward = Vector3.forward;
            if (cameraRight.sqrMagnitude < 0.001f) cameraRight = Vector3.right;
            if (boardUp.sqrMagnitude < 0.001f) boardUp = Vector3.up;

            if (Keyboard.current.wKey.wasPressedThisFrame) RotateHoveredCube(cameraRight, 90f);
            if (Keyboard.current.sKey.wasPressedThisFrame) RotateHoveredCube(cameraRight, -90f);
            if (Keyboard.current.aKey.wasPressedThisFrame) RotateHoveredCube(cameraForward, 90f);
            if (Keyboard.current.dKey.wasPressedThisFrame) RotateHoveredCube(cameraForward, -90f);
            if (Keyboard.current.qKey.wasPressedThisFrame) RotateHoveredCube(boardUp, -90f);
            if (Keyboard.current.eKey.wasPressedThisFrame) RotateHoveredCube(boardUp, 90f);
        }

        /// <summary>유효한 회전 입력을 받은 프레임에 효과음과 회전 동작을 함께 시작한다.</summary>
        private void RotateHoveredCube(Vector3 axis, float degrees)
        {
            if (hoveredCube == null) return;

            BoardUndoSnapshot beforeRotation = CaptureBoardState();
            SoundManager.Play(SoundEventId.CubeRotate, hoveredCube.transform.position);
            hoveredCube.RotateBy(axis, degrees);
            CommitUndoSnapshot(beforeRotation);
        }

        /// <summary>
        /// 마지막으로 완료한 큐브 이동 또는 회전 한 번을 되돌린다.
        /// 적층 붕괴와 규칙 제거까지 함께 복구하기 위해 전체 보드 스냅숏을 사용한다.
        /// </summary>
        public bool UndoLastAction()
        {
            if (!interactionEnabled || isDragging || board == null || undoHistory.Count == 0)
            {
                return false;
            }

            BoardUndoSnapshot snapshot = undoHistory.Pop();
            RestoreBoardState(snapshot);
            return true;
        }

        /// <summary>스테이지를 불러오거나 다시 시작할 때 이전 스테이지의 참조를 모두 버린다.</summary>
        public void ClearUndoHistory()
        {
            undoHistory.Clear();
            pendingDragSnapshot = null;
        }

        private BoardUndoSnapshot CaptureBoardState()
        {
            var snapshot = new BoardUndoSnapshot();
            if (board == null) return snapshot;

            DraggableCube[] cubes = FindObjectsByType<DraggableCube>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (DraggableCube cube in cubes)
            {
                if (cube == null || cube.Board != board || cube.gameObject.scene != gameObject.scene)
                {
                    continue;
                }

                PuzzleCubeProperties properties = cube.GetComponent<PuzzleCubeProperties>();
                bool removedByRule = properties != null && properties.RemovedByRule;
                // 비활성화된 레거시 원본은 제외하고, 규칙에 의해 사라진 런타임 큐브만 기록한다.
                if (!cube.gameObject.activeInHierarchy && !removedByRule)
                {
                    continue;
                }

                snapshot.Cubes.Add(new CubeUndoState
                {
                    Cube = cube,
                    WasActive = cube.gameObject.activeSelf,
                    WasRemovedByRule = removedByRule,
                    WasPlaced = cube.IsPlaced,
                    Cell = cube.Cell,
                    StackIndex = board.GetStackIndex(cube),
                    WorldPosition = cube.transform.position,
                    Rotation = cube.TargetRotation
                });
            }

            return snapshot;
        }

        private void CommitUndoSnapshot(BoardUndoSnapshot beforeAction)
        {
            if (beforeAction == null || !HasBoardStateChanged(beforeAction)) return;

            undoHistory.Push(beforeAction);
            int capacity = Mathf.Max(1, undoCapacity);
            while (undoHistory.Count > capacity)
            {
                // Stack은 오래된 항목을 직접 제거할 수 없으므로 최신 항목만 용량만큼 다시 쌓는다.
                BoardUndoSnapshot[] newestFirst = undoHistory.ToArray();
                undoHistory.Clear();
                int keepCount = Mathf.Min(capacity, newestFirst.Length);
                for (int index = keepCount - 1; index >= 0; index--)
                {
                    undoHistory.Push(newestFirst[index]);
                }
            }
        }

        private bool HasBoardStateChanged(BoardUndoSnapshot beforeAction)
        {
            BoardUndoSnapshot afterAction = CaptureBoardState();
            if (beforeAction.Cubes.Count != afterAction.Cubes.Count) return true;

            var afterById = new Dictionary<int, CubeUndoState>();
            foreach (CubeUndoState state in afterAction.Cubes)
            {
                if (state.Cube != null) afterById[state.Cube.GetInstanceID()] = state;
            }

            foreach (CubeUndoState before in beforeAction.Cubes)
            {
                if (before.Cube == null ||
                    !afterById.TryGetValue(before.Cube.GetInstanceID(), out CubeUndoState after))
                {
                    return true;
                }

                if (before.WasActive != after.WasActive ||
                    before.WasRemovedByRule != after.WasRemovedByRule ||
                    before.WasPlaced != after.WasPlaced ||
                    before.Cell != after.Cell ||
                    before.StackIndex != after.StackIndex ||
                    Mathf.Abs(Quaternion.Dot(before.Rotation, after.Rotation)) < 0.9999f)
                {
                    return true;
                }
            }

            return false;
        }

        private void RestoreBoardState(BoardUndoSnapshot snapshot)
        {
            board.CancelLightEffects();
            board.ResetBoard();

            var placedCubes = new List<CubeUndoState>();
            foreach (CubeUndoState state in snapshot.Cubes)
            {
                if (state.Cube == null) continue;

                PuzzleCubeProperties properties = state.Cube.GetComponent<PuzzleCubeProperties>();
                if (properties != null)
                {
                    properties.RestoreUndoState(state.WasRemovedByRule, state.WasActive);
                }
                else
                {
                    state.Cube.gameObject.SetActive(state.WasActive);
                }

                state.Cube.RestoreUndoState(
                    state.WasPlaced,
                    state.Cell,
                    state.WorldPosition,
                    state.Rotation);

                if (!state.WasActive) continue;
                if (state.WasPlaced) placedCubes.Add(state);
                else if (state.Cube.InteractionLocked) board.RegisterExternalCube(state.Cube);
            }

            placedCubes.Sort((left, right) =>
            {
                int cellX = left.Cell.x.CompareTo(right.Cell.x);
                if (cellX != 0) return cellX;
                int cellY = left.Cell.y.CompareTo(right.Cell.y);
                return cellY != 0 ? cellY : left.StackIndex.CompareTo(right.StackIndex);
            });

            foreach (CubeUndoState state in placedCubes)
            {
                state.Cube.SnapImmediately(board.PlaceCube(state.Cube, state.Cell));
            }

            selectedCube = null;
            hoveredCube = null;
            lastLightResult = null;
            latchedLampKeys.Clear();
            hasWon = false;
            lightVisualizer?.ResetLightAttempt();
            SoundManager.Play(SoundEventId.CubeReturn);
        }

        /// <summary>
        /// 드래그 중에는 잡은 큐브를 유지하고, 평상시에는 커서 아래 Collider의 부모 큐브를 찾는다.
        /// </summary>
        private void RefreshHoveredCube()
        {
            if (isDragging && selectedCube != null)
            {
                hoveredCube = selectedCube;
                return;
            }

            hoveredCube = TryGetCubeUnderPointer(
                Mouse.current.position.ReadValue(),
                out DraggableCube cube)
                ? cube
                : null;
        }

        /// <summary>프리팹의 자식 Collider를 눌러도 부모의 DraggableCube를 반환한다.</summary>
        private bool TryGetCubeUnderPointer(Vector2 pointer, out DraggableCube cube)
        {
            Ray ray = playerCamera.ScreenPointToRay(pointer);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                cube = hit.collider.GetComponentInParent<DraggableCube>();
                if (cube != null && !cube.InteractionLocked)
                {
                    return true;
                }
            }

            cube = null;
            return false;
        }

        /// <summary>배치를 바꾸면 광선 경로만 무효화하고 이미 켜진 전등 상태는 유지한다.</summary>
        private void InvalidateLightResult()
        {
            lastLightResult = null;
        }

        private void HandleLightSimulationStep(LightSimulationResult result)
        {
            bool wasWon = hasWon;
            if (result != null)
            {
                foreach (LampLightResult lamp in result.Lamps)
                {
                    string key = BuildLampKey(lamp.Target);
                    if (lamp.HasCorrectHit)
                    {
                        if (latchedLampKeys.Add(key))
                        {
                            SoundManager.Play(SoundEventId.LampOn);
                        }
                    }

                    if (latchedLampKeys.Contains(key))
                    {
                        lamp.LatchOn();
                    }
                }
            }

            lastLightResult = result;
            hasWon = currentStage != null && currentStage.Matches(result);
            if (!wasWon && hasWon)
            {
                SoundManager.Play(SoundEventId.PuzzleSolved);
            }
            lightVisualizer?.ShowResult(result);
        }

        private static string BuildLampKey(LampTarget lamp)
        {
            return $"{lamp.gridPosition.x}:{lamp.gridPosition.y}:{lamp.gridPosition.z}:" +
                   $"{(int)lamp.frontDirection}:{(int)lamp.requiredColor}";
        }

        /// <summary>프로토타입 조작법과 승리 상태를 별도 UI 애셋 없이 표시한다.</summary>
        private void OnGUI()
        {
            if (!showInstructionOverlay)
            {
                return;
            }

            // GUI.skin의 이름 기반 스타일은 Inspector 갱신 중 유효하지 않을 수 있어 독립 스타일만 사용한다.
            GUIStyle labelStyle = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 18,
                padding = new RectOffset(16, 16, 12, 12),
                wordWrap = true
            };
            labelStyle.normal.textColor = Color.white;

            string message = hasWon
                ? "완성! 모든 전등에 올바른 빛이 정면으로 들어왔습니다."
                : "큐브: 마우스로 드래그 / 커서를 올리고 W·S: 앞뒤 / A·D: 좌우 / Q·E: 횡 회전\n휠: 카메라 회전 / Ctrl+R: 다시 시작 / Z: 무르기 / Space: 점등";
            Rect panelRect = new Rect(20, 20, 500, 78);

            // 흰색 기본 텍스처에 색만 입혀 반투명 패널 배경을 그린다.
            Color previousColor = GUI.color;
            GUI.color = new Color(0.04f, 0.03f, 0.05f, 0.82f);
            GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Label(panelRect, message, labelStyle);
        }
    }
}
