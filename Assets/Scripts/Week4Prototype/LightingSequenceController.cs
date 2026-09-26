using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameLab.Week4
{
    /// <summary>
    /// Input System의 Player/TurnOnLight 액션을 받아 전등 퍼즐 결과와 카메라를 전환한다.
    /// 기존 Stage 스포트라이트와 그림자 모형은 사용하지 않는다.
    /// </summary>
    public sealed class LightingSequenceController : MonoBehaviour
    {
        private const string ActionPath = "Player/TurnOnLight";

        [Header("Input System")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("연출 연결")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Camera stageCamera;
        [SerializeField] private PrototypeGameManager gameManager;

        private InputAction turnOnLightAction;
        private bool ownsFallbackAction;
        private bool isLightOn;

        public event Action<bool> LightStateChanged;
        public event Action LightEffectsResolved;

        public void Initialize(
            InputActionAsset actions,
            Camera player,
            Camera stage,
            PrototypeGameManager manager)
        {
            inputActions = actions;
            playerCamera = player;
            stageCamera = stage;
            gameManager = manager;

            if (isActiveAndEnabled)
            {
                BindInputAction();
                ApplyInitialState();
            }
        }

        private void OnEnable()
        {
            BindInputAction();
            ApplyInitialState();
        }

        private void OnDisable()
        {
            ReleaseInputAction();
        }

        private void BindInputAction()
        {
            ReleaseInputAction();

            turnOnLightAction = inputActions != null ? inputActions.FindAction(ActionPath, false) : null;
            ownsFallbackAction = turnOnLightAction == null;

            // 씬 직렬화가 아직 끝나지 않은 경우에도 동일한 Input System 바인딩으로 안전하게 작동한다.
            if (ownsFallbackAction)
            {
                turnOnLightAction = new InputAction("TurnOnLight", InputActionType.Button, "<Keyboard>/space");
            }

            turnOnLightAction.performed += HandleTurnOnLight;
            turnOnLightAction.Enable();
        }

        private void ReleaseInputAction()
        {
            if (turnOnLightAction == null) return;

            turnOnLightAction.performed -= HandleTurnOnLight;
            turnOnLightAction.Disable();
            if (ownsFallbackAction) turnOnLightAction.Dispose();
            turnOnLightAction = null;
            ownsFallbackAction = false;
        }

        private void ApplyInitialState()
        {
            isLightOn = false;
            SetCameraState(showStage: false);
            gameManager?.SetLightVisualizationVisible(false);
            gameManager?.SetInteractionEnabled(true);
        }

        private void HandleTurnOnLight(InputAction.CallbackContext context)
        {
            if (!context.performed) return;

            if (isLightOn)
            {
                ReturnToPlayerView();
                return;
            }

            isLightOn = true;
            gameManager?.SetInteractionEnabled(false);

            // 새 전등판과 논리 광선만 표시하고, 얼음의 용해와 낙하를 Stage Camera에서 관찰한다.
            gameManager?.SetLightVisualizationVisible(true);
            gameManager?.PrepareStageCamera(stageCamera);
            SetCameraState(showStage: true);
            LightStateChanged?.Invoke(true);
            if (gameManager != null)
            {
                gameManager.ApplyLightEffects(HandleLightEffectsResolved);
            }
            else
            {
                HandleLightEffectsResolved();
            }
        }

        private void ReturnToPlayerView()
        {
            isLightOn = false;
            gameManager?.CancelLightEffects();
            SetCameraState(showStage: false);
            gameManager?.SetLightVisualizationVisible(false);
            gameManager?.SetInteractionEnabled(true);
            LightStateChanged?.Invoke(false);
        }

        /// <summary>스테이지 로드·재시작 시 점등 여부와 관계없이 조작 화면으로 복귀한다.</summary>
        public void ResetToPlayerView()
        {
            isLightOn = false;
            gameManager?.CancelLightEffects();
            SetCameraState(showStage: false);
            gameManager?.SetLightVisualizationVisible(false);
            gameManager?.SetInteractionEnabled(true);
            LightStateChanged?.Invoke(false);
        }

        private void HandleLightEffectsResolved()
        {
            if (!isLightOn) return;

            LightEffectsResolved?.Invoke();
        }

        private void SetCameraState(bool showStage)
        {
            SetSingleCameraState(playerCamera, !showStage);
            SetSingleCameraState(stageCamera, showStage);
        }

        private static void SetSingleCameraState(Camera camera, bool enabled)
        {
            if (camera == null) return;

            camera.enabled = enabled;
            camera.gameObject.tag = enabled ? "MainCamera" : "Untagged";

            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = enabled;
        }
    }
}
