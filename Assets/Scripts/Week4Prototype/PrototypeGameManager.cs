using System;
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

        private DraggableCube selectedCube;
        private PuzzleStageDefinition currentStage;
        private LightSimulationResult lastLightResult;
        private bool isDragging;
        private bool hasWon;
        private bool interactionEnabled = true;

        public bool IsCurrentStageSolved => hasWon;
        public LightSimulationResult LastLightResult => lastLightResult;

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
            currentStage = stage;
            lastLightResult = null;
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
            HandleRotation();
        }

        /// <summary>클릭한 큐브를 선택하고 수평 드래그 평면을 따라 이동시킨다.</summary>
        private void HandleMouse()
        {
            Vector2 pointer = Mouse.current.position.ReadValue();

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Ray ray = playerCamera.ScreenPointToRay(pointer);
                if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.TryGetComponent(out DraggableCube cube))
                {
                    selectedCube = cube;
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
                    selectedCube.EndDrag();
                }

                isDragging = false;
            }
        }

        /// <summary>
        /// 마우스로 큐브를 잡고 있는 동안 W/S는 기존 화면 기준 앞뒤 회전을 유지하고,
        /// A/D는 보드의 수직축을 중심으로 좌우(횡 방향) 90도 회전한다.
        /// </summary>
        private void HandleRotation()
        {
            if (!isDragging || selectedCube == null ||
                Mouse.current == null || !Mouse.current.leftButton.isPressed)
            {
                return;
            }

            Vector3 cameraRight = Vector3.ProjectOnPlane(playerCamera.transform.right, Vector3.up).normalized;
            Vector3 boardUp = board != null ? board.transform.up : Vector3.up;

            // 카메라가 정확히 수직을 보는 예외에서도 회전축이 0이 되지 않게 월드축을 사용한다.
            if (cameraRight.sqrMagnitude < 0.001f) cameraRight = Vector3.right;
            if (boardUp.sqrMagnitude < 0.001f) boardUp = Vector3.up;

            if (Keyboard.current.wKey.wasPressedThisFrame) selectedCube.RotateBy(cameraRight, 90f);
            if (Keyboard.current.sKey.wasPressedThisFrame) selectedCube.RotateBy(cameraRight, -90f);
            if (Keyboard.current.aKey.wasPressedThisFrame) selectedCube.RotateBy(boardUp, -90f);
            if (Keyboard.current.dKey.wasPressedThisFrame) selectedCube.RotateBy(boardUp, 90f);
        }

        /// <summary>배치를 바꾸면 이전 점등 결과를 무효화한다.</summary>
        private void InvalidateLightResult()
        {
            lastLightResult = null;
            hasWon = false;
        }

        private void HandleLightSimulationStep(LightSimulationResult result)
        {
            lastLightResult = result;
            hasWon = currentStage != null && currentStage.Matches(result);
            lightVisualizer?.ShowResult(result);
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
                : "큐브: 마우스로 드래그 / W·S: 앞뒤 회전 / A·D: 수평 회전\nSpace: 점등하고 전등 결과 확인";
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
