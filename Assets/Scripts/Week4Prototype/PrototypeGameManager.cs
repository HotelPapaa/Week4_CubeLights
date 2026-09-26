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

        private DraggableCube selectedCube;
        private bool isDragging;
        private bool hasWon;
        private bool interactionEnabled = true;

        public void Initialize(GridBoard targetBoard, Camera targetCamera, int[] target)
        {
            if (board != null)
            {
                board.BoardChanged -= EvaluateVictory;
            }

            board = targetBoard;
            playerCamera = targetCamera;
            targetProjection = target;

            if (isActiveAndEnabled && board != null)
            {
                board.BoardChanged -= EvaluateVictory;
                board.BoardChanged += EvaluateVictory;
                EvaluateVictory();
            }
        }

        /// <summary>분위기 연출이 완성된 씬에서는 프로토타입 안내 UI를 숨길 수 있다.</summary>
        public void SetInstructionOverlayVisible(bool visible)
        {
            showInstructionOverlay = visible;
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

        /// <summary>조명 점등 직전에 얼음처럼 빛에 반응하는 큐브 효과를 계산한다.</summary>
        public void ApplyLightEffects(Action onCompleted = null)
        {
            if (board != null)
            {
                board.ApplyLightEffects(onCompleted);
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
                board.BoardChanged += EvaluateVictory;
            }
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.BoardChanged -= EvaluateVictory;
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
        /// 마우스로 큐브를 잡고 있는 동안 W/S는 화면 기준 앞뒤, A/D는 좌우로 90도 회전한다.
        /// 카메라가 월드축과 비스듬해도 플레이어가 보는 방향과 입력 방향이 일치한다.
        /// </summary>
        private void HandleRotation()
        {
            if (!isDragging || selectedCube == null ||
                Mouse.current == null || !Mouse.current.leftButton.isPressed)
            {
                return;
            }

            Vector3 cameraForward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
            Vector3 cameraRight = Vector3.ProjectOnPlane(playerCamera.transform.right, Vector3.up).normalized;

            // 카메라가 정확히 수직을 보는 예외에서도 회전축이 0이 되지 않게 월드축을 사용한다.
            if (cameraForward.sqrMagnitude < 0.001f) cameraForward = Vector3.forward;
            if (cameraRight.sqrMagnitude < 0.001f) cameraRight = Vector3.right;

            if (Keyboard.current.wKey.wasPressedThisFrame) selectedCube.RotateBy(cameraRight, 90f);
            if (Keyboard.current.sKey.wasPressedThisFrame) selectedCube.RotateBy(cameraRight, -90f);
            if (Keyboard.current.aKey.wasPressedThisFrame) selectedCube.RotateBy(cameraForward, 90f);
            if (Keyboard.current.dKey.wasPressedThisFrame) selectedCube.RotateBy(cameraForward, -90f);
        }

        /// <summary>현재 투영 높이가 종이의 목표 모양과 같은지 검사한다.</summary>
        private void EvaluateVictory()
        {
            hasWon = board != null && targetProjection != null && targetProjection.Length == board.Width;
            if (!hasWon)
            {
                return;
            }

            for (int column = 0; column < board.Width; column++)
            {
                if (board.GetProjectedHeight(column) != targetProjection[column])
                {
                    hasWon = false;
                    break;
                }
            }
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
                ? "완성! 종이의 모양과 그림자가 일치합니다."
                : "큐브: 마우스로 드래그 / 선택한 큐브 회전: W A S D\n목표 높이: 1 - 2 - 3 - 2 - 1";
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
