using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameLab.Week4
{
    /// <summary>
    /// Input System의 Player/TurnOnLight 액션을 받아 조명, 그림자, 카메라 전환을 한 번에 실행한다.
    /// SpaceBar를 누를 때마다 조명 결과 화면과 큐브 조작 화면을 전환한다.
    /// </summary>
    public sealed class LightingSequenceController : MonoBehaviour
    {
        private const string ActionPath = "Player/TurnOnLight";

        [Header("Input System")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("연출 연결")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Camera stageCamera;
        [SerializeField] private Light projectionLight;
        [SerializeField] private FakeShadowDisplay shadowDisplay;
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
            Light lightToEnable,
            FakeShadowDisplay display,
            PrototypeGameManager manager)
        {
            inputActions = actions;
            playerCamera = player;
            stageCamera = stage;
            projectionLight = lightToEnable;
            shadowDisplay = display;
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
            if (projectionLight != null) projectionLight.enabled = false;
            shadowDisplay?.SetProjectionVisible(false);
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

            // 먼저 결과 화면을 보여준 뒤 얼음의 용해 대기와 낙하를 Stage Camera에서 관찰하게 한다.
            shadowDisplay?.SetProjectionVisible(true);
            if (projectionLight != null) projectionLight.enabled = true;
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
            if (projectionLight != null) projectionLight.enabled = false;
            shadowDisplay?.SetProjectionVisible(false);
            gameManager?.SetInteractionEnabled(true);
            LightStateChanged?.Invoke(false);
        }

        /// <summary>스테이지 로드·재시작 시 점등 여부와 관계없이 조작 화면으로 복귀한다.</summary>
        public void ResetToPlayerView()
        {
            isLightOn = false;
            gameManager?.CancelLightEffects();
            SetCameraState(showStage: false);
            if (projectionLight != null) projectionLight.enabled = false;
            shadowDisplay?.SetProjectionVisible(false);
            gameManager?.SetInteractionEnabled(true);
            LightStateChanged?.Invoke(false);
        }

        private void HandleLightEffectsResolved()
        {
            if (!isLightOn) return;

            shadowDisplay?.Refresh();
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
