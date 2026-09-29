using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
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

        [Header("시네머신 카메라들")]
        [SerializeField] public CinemachineCamera defaultCamera;
        [SerializeField] public CinemachineCamera StageCIneCamera;
        [SerializeField] public CinemachineCamera RuleCamera;
        [SerializeField] public CinemachineCamera RightCamera;
        [SerializeField] public CinemachineCamera TopCamera;
        [SerializeField] public CinemachineCamera LeftCamera;
        [SerializeField] public CinemachineCamera EasterEggCamera;


        private CinemachineCamera[] sceneCameras;
        private CinemachineCamera currentCamera;
        private int orbitCameraIndex;

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

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.rKey.wasPressedThisFrame)
            {
                int nextIndex = (orbitCameraIndex + 1) % 4;
                CinemachineCamera nextCamera = nextIndex switch
                {
                    1 => RightCamera,
                    2 => TopCamera,
                    3 => LeftCamera,
                    _ => defaultCamera
                };
                if (SelectCamera(nextCamera)) orbitCameraIndex = nextIndex;
            }
            else if (keyboard.tKey.wasPressedThisFrame)
            {
                if (RuleCamera != null && currentCamera == RuleCamera)
                {
                    SetCameraState(showStage: false);
                }
                else
                {
                    SelectCamera(RuleCamera);
                }
            }
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
            ResolveSceneCameras();
            isLightOn = false;
            SetCameraState(showStage: false);
            gameManager?.SetLightVisualizationVisible(false);
            gameManager?.SetInteractionEnabled(true);
        }

        private void HandleTurnOnLight(InputAction.CallbackContext context)
        {
            if (!context.performed) return;

            // 블렌드 도중에도 마지막으로 선택한 시점을 기준으로 토글한다.
            bool isStageView = defaultCamera != null
                ? StageCIneCamera != null && currentCamera == StageCIneCamera
                : stageCamera != null && stageCamera.enabled;
            SetCameraState(showStage: !isStageView);

            if (isLightOn)
            {
                TurnOffLight();
                return;
            }

            isLightOn = true;
            SoundManager.Play(SoundEventId.LightOn);
            gameManager?.SetInteractionEnabled(false);

            // 새 전등판과 논리 광선만 표시하고, 얼음의 용해와 낙하를 Stage Camera에서 관찰한다.
            gameManager?.SetLightVisualizationVisible(true);
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

        private void TurnOffLight()
        {
            isLightOn = false;
            SoundManager.Play(SoundEventId.LightOff);
            gameManager?.CancelLightEffects();
            gameManager?.SetLightVisualizationVisible(false);
            gameManager?.SetInteractionEnabled(true);
            LightStateChanged?.Invoke(false);
        }

        /// <summary>스테이지 로드·재시작 시 점등 여부와 관계없이 조작 화면으로 복귀한다.</summary>
        public void ResetToPlayerView()
        {
            bool wasLightOn = isLightOn;
            isLightOn = false;
            if (wasLightOn)
            {
                SoundManager.Play(SoundEventId.LightOff);
            }
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
            if (!showStage) orbitCameraIndex = 0;
            if (defaultCamera != null)
            {
                SelectCamera(showStage ? StageCIneCamera : defaultCamera);
                return;
            }

            // 시네머신을 사용하지 않는 기존 씬의 카메라 연결도 유지한다.
            SetSingleCameraState(playerCamera, !showStage);
            SetSingleCameraState(stageCamera, showStage);
        }

        private void ResolveSceneCameras()
        {
            // Inspector 할당을 우선하고, 비어 있는 슬롯만 현재 씬에서 연결한다.
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (CinemachineCamera camera in root.GetComponentsInChildren<CinemachineCamera>(true))
                {
                    switch (camera.name)
                    {
                        case "For MainCamera":
                            if (defaultCamera == null) defaultCamera = camera;
                            break;
                        case "For Stag Camera":
                            if (StageCIneCamera == null) StageCIneCamera = camera;
                            break;
                        case "Rule Camera":
                            if (RuleCamera == null) RuleCamera = camera;
                            break;
                        case "Right Camera":
                            if (RightCamera == null) RightCamera = camera;
                            break;
                        case "TOPCamera":
                            if (TopCamera == null) TopCamera = camera;
                            break;
                        case "Left Camera":
                            if (LeftCamera == null) LeftCamera = camera;
                            break;
                        case "EasterEggCamera":
                            if (EasterEggCamera == null) EasterEggCamera = camera;
                            break;
                    }
                }
            }

            sceneCameras = new[] { defaultCamera, RightCamera, TopCamera, LeftCamera, StageCIneCamera, RuleCamera, EasterEggCamera };
        }

        public bool SelectCamera(CinemachineCamera selectedCamera)
        {
            if (selectedCamera == null || playerCamera == null) return false;

            // 모든 시점은 Player Camera의 Brain 하나로 출력한다.
            // 별도의 Stage Camera를 켜면 시네머신 출력 위에 중복 렌더링된다.
            SetSingleCameraState(stageCamera, false);
            SetSingleCameraState(playerCamera, true);
            selectedCamera.gameObject.SetActive(true);
            selectedCamera.enabled = true;
            foreach (CinemachineCamera camera in sceneCameras)
            {
                if (camera != null) camera.Priority = camera == selectedCamera ? 100 : 0;
            }

            currentCamera = selectedCamera;
            return true;
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
