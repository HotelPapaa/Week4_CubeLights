using System;
using System.Collections;
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
        private const string RotateCameraActionPath = "Player/RotateCamera";
        private const string RestartActionPath = "Player/Restart";
        private const string UndoActionPath = "Player/Undo";

        [Header("Input System")]
        [SerializeField] private InputActionAsset inputActions;
        [Min(0.01f)] [SerializeField] private float scrollThreshold = 0.01f;
        [Min(0f)] [SerializeField] private float scrollCooldown = 0.08f;

        [Header("연출 연결")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Camera stageCamera;
        [SerializeField] private PrototypeGameManager gameManager;
        [Tooltip("마지막 레이저가 도착한 뒤 Stage Camera로 전환하기 전까지 유지할 시간")]
        [Min(0f)] [SerializeField] private float stageClearLaserHoldDuration = 0.5f;
        [Tooltip("Stage Camera로 전환된 뒤 Main Camera로 돌아가기 전까지 유지할 시간")]
        [Min(0f)] [SerializeField] private float stageCameraHoldDuration = 0.5f;

        [Header("시네머신 카메라들")]
        [SerializeField] public CinemachineCamera defaultCamera;
        [SerializeField] public CinemachineCamera StageCIneCamera;
        [SerializeField] public CinemachineCamera RuleCamera;
        [SerializeField] public CinemachineCamera RightCamera;
        [SerializeField] public CinemachineCamera TopCamera;
        [SerializeField] public CinemachineCamera LeftCamera;
        [SerializeField] public CinemachineCamera EasterEggCamera;


        private CinemachineCamera[] sceneCameras;
        private CinemachineCamera[] orbitCameras;
        private CinemachineCamera currentCamera;
        private float nextOrbitInputTime;
        private Coroutine stageClearTransitionCoroutine;

        private InputAction turnOnLightAction;
        private InputAction rotateCameraAction;
        private InputAction restartAction;
        private InputAction undoAction;
        private bool ownsTurnOnLightAction;
        private bool ownsRotateCameraAction;
        private bool ownsRestartAction;
        private bool ownsUndoAction;
        private bool isLightOn;

        public event Action<bool> LightStateChanged;
        public event Action LightEffectsResolved;

        /// <summary>클리어 결과 확정부터 Main Camera 복귀까지 필요한 전체 연출 시간.</summary>
        public float StageClearPresentationDuration =>
            Mathf.Max(0f, stageClearLaserHoldDuration) + Mathf.Max(0f, stageCameraHoldDuration);

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
            CancelStageClearTransition();
            ReleaseInputAction();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.tKey.wasPressedThisFrame)
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
            ownsTurnOnLightAction = turnOnLightAction == null;

            // 씬 직렬화가 아직 끝나지 않은 경우에도 동일한 Input System 바인딩으로 안전하게 작동한다.
            if (ownsTurnOnLightAction)
            {
                turnOnLightAction = new InputAction("TurnOnLight", InputActionType.Button, "<Keyboard>/space");
            }

            rotateCameraAction = inputActions != null
                ? inputActions.FindAction(RotateCameraActionPath, false)
                : null;
            ownsRotateCameraAction = rotateCameraAction == null;
            if (ownsRotateCameraAction)
            {
                rotateCameraAction = new InputAction(
                    "RotateCamera",
                    InputActionType.PassThrough,
                    "<Mouse>/scroll/y");
            }

            restartAction = inputActions != null ? inputActions.FindAction(RestartActionPath, false) : null;
            ownsRestartAction = restartAction == null;
            if (ownsRestartAction)
            {
                restartAction = new InputAction("Restart", InputActionType.Button);
                restartAction.AddCompositeBinding("OneModifier")
                    .With("Modifier", "<Keyboard>/ctrl")
                    .With("Binding", "<Keyboard>/r");
            }

            undoAction = inputActions != null ? inputActions.FindAction(UndoActionPath, false) : null;
            ownsUndoAction = undoAction == null;
            if (ownsUndoAction)
            {
                undoAction = new InputAction("Undo", InputActionType.Button, "<Keyboard>/z");
            }

            turnOnLightAction.performed += HandleTurnOnLight;
            rotateCameraAction.performed += HandleRotateCamera;
            restartAction.performed += HandleRestart;
            undoAction.performed += HandleUndo;
            turnOnLightAction.Enable();
            rotateCameraAction.Enable();
            restartAction.Enable();
            undoAction.Enable();
        }

        private void ReleaseInputAction()
        {
            ReleaseAction(ref turnOnLightAction, HandleTurnOnLight, ownsTurnOnLightAction);
            ReleaseAction(ref rotateCameraAction, HandleRotateCamera, ownsRotateCameraAction);
            ReleaseAction(ref restartAction, HandleRestart, ownsRestartAction);
            ReleaseAction(ref undoAction, HandleUndo, ownsUndoAction);
            ownsTurnOnLightAction = false;
            ownsRotateCameraAction = false;
            ownsRestartAction = false;
            ownsUndoAction = false;
        }

        private static void ReleaseAction(
            ref InputAction action,
            Action<InputAction.CallbackContext> handler,
            bool ownsAction)
        {
            if (action == null) return;

            action.performed -= handler;
            action.Disable();
            if (ownsAction) action.Dispose();
            action = null;
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

            if (isLightOn)
            {
                TurnOffLight();
                return;
            }

            isLightOn = true;
            SoundManager.Play(SoundEventId.LightOn);
            gameManager?.SetInteractionEnabled(false);
            gameManager?.CaptureLightAttemptState();

            // 성공 판정 전에는 현재 플레이어 시점을 유지한 채 광선과 연쇄 반응을 관찰한다.
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

        private void HandleRotateCamera(InputAction.CallbackContext context)
        {
            if (!context.performed || isLightOn || gameManager == null || !gameManager.CanOrbitCamera)
            {
                return;
            }

            float scroll = context.ReadValue<float>();
            if (Mathf.Abs(scroll) < scrollThreshold || Time.unscaledTime < nextOrbitInputTime)
            {
                return;
            }

            // Unity Input System에서 휠 위는 양수다. 양수는 반시계, 음수는 시계 방향이다.
            RotateOrbitCamera(scroll > 0f ? 1 : -1);
            nextOrbitInputTime = Time.unscaledTime + scrollCooldown;
        }

        private void HandleRestart(InputAction.CallbackContext context)
        {
            if (!context.performed) return;

            StageManager stageManager = FindFirstObjectByType<StageManager>();
            stageManager?.RestartStage();
        }

        private void HandleUndo(InputAction.CallbackContext context)
        {
            if (!context.performed || gameManager == null) return;

            // 점등 중에는 일반 무르기 기록을 소비하지 않고 레이저 직전의 별도 스냅숏으로 복귀한다.
            if (isLightOn)
            {
                TurnOffLight(preserveLightAttemptState: true);
                gameManager.RestoreLightAttemptState();
                return;
            }

            gameManager.UndoLastAction();
        }

        private void RotateOrbitCamera(int direction)
        {
            if (direction == 0) return;
            if (orbitCameras == null || orbitCameras.Length == 0) ResolveSceneCameras();
            if (orbitCameras == null || orbitCameras.Length == 0) return;

            int currentIndex = Array.IndexOf(orbitCameras, currentCamera);
            if (currentIndex < 0) return;

            for (int offset = 1; offset <= orbitCameras.Length; offset++)
            {
                int nextIndex = (currentIndex + direction * offset + orbitCameras.Length * 2) %
                                orbitCameras.Length;
                CinemachineCamera nextCamera = orbitCameras[nextIndex];
                if (nextCamera != null && SelectCamera(nextCamera))
                {
                    return;
                }
            }
        }

        private void TurnOffLight(bool preserveLightAttemptState = false)
        {
            CancelStageClearTransition();
            isLightOn = false;
            SoundManager.Play(SoundEventId.LightOff);
            gameManager?.CancelLightEffects();
            if (IsStageViewActive()) SetCameraState(showStage: false);
            gameManager?.ResetLightAttempt();
            gameManager?.SetInteractionEnabled(true);
            if (!preserveLightAttemptState) gameManager?.DiscardLightAttemptState();
            LightStateChanged?.Invoke(false);
        }

        /// <summary>스테이지 로드·재시작 시 점등 여부와 관계없이 조작 화면으로 복귀한다.</summary>
        public void ResetToPlayerView()
        {
            CancelStageClearTransition();
            bool wasLightOn = isLightOn;
            isLightOn = false;
            if (wasLightOn)
            {
                SoundManager.Play(SoundEventId.LightOff);
            }
            gameManager?.CancelLightEffects();
            SetCameraState(showStage: false);
            gameManager?.ResetLightAttempt();
            gameManager?.SetInteractionEnabled(true);
            gameManager?.DiscardLightAttemptState();
            LightStateChanged?.Invoke(false);
        }

        private void HandleLightEffectsResolved()
        {
            if (!isLightOn) return;

            // 얼음 용해와 낙하까지 반영된 최종 결과가 성공일 때만 전등판 카메라로 전환한다.
            if (gameManager != null && gameManager.IsCurrentStageSolved)
            {
                CancelStageClearTransition();
                stageClearTransitionCoroutine = StartCoroutine(CompleteStageClearTransition());
            }

            LightEffectsResolved?.Invoke();
        }

        private IEnumerator CompleteStageClearTransition()
        {
            float delay = Mathf.Max(0f, stageClearLaserHoldDuration);
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (!isLightOn || gameManager == null || !gameManager.IsCurrentStageSolved)
            {
                stageClearTransitionCoroutine = null;
                yield break;
            }

            // 마지막 전등 도착 장면을 잠시 유지한 뒤 레이저를 숨기고 결과 카메라로 전환한다.
            gameManager.SetLightVisualizationVisible(false);
            SetCameraState(showStage: true);

            float cameraHoldDuration = Mathf.Max(0f, stageCameraHoldDuration);
            if (cameraHoldDuration > 0f)
            {
                yield return new WaitForSeconds(cameraHoldDuration);
            }

            stageClearTransitionCoroutine = null;
            if (!isLightOn || gameManager == null || !gameManager.IsCurrentStageSolved) yield break;

            // Stage Camera 유지 시간이 끝나면 조작 화면으로 복귀한다.
            ResetToPlayerView();
        }

        private void CancelStageClearTransition()
        {
            if (stageClearTransitionCoroutine == null) return;

            StopCoroutine(stageClearTransitionCoroutine);
            stageClearTransitionCoroutine = null;
        }

        private bool IsStageViewActive()
        {
            return defaultCamera != null
                ? StageCIneCamera != null && currentCamera == StageCIneCamera
                : stageCamera != null && stageCamera.enabled;
        }

        private void SetCameraState(bool showStage)
        {
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

            // 위에서 보았을 때 반시계 순서. 휠 아래 입력은 이 배열을 역방향으로 순회한다.
            orbitCameras = new[] { defaultCamera, RightCamera, TopCamera, LeftCamera };
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
