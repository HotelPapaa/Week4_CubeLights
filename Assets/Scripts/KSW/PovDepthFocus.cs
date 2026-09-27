using UnityEngine;
using Unity.Cinemachine;

namespace KSW
{
    // 시선 자동 초점
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("KSW/Camera/POV Depth Focus")]
    public sealed class PovDepthFocus : CinemachineVolumeSettings
    {
        [Tooltip("초점 검사 제외 대상")]
        public Transform IgnoreRoot;

        [Tooltip("초점 대상 레이어")] public LayerMask FocusLayers = ~0;
        [Min(0.1f)] public float MinimumDistance = 8f;
        [Min(1f)] public float MaximumDistance = 1600f;

        [Tooltip("초점 전환 시간 (초)")]
        [Min(0f)] public float FocusResponse = 0.25f;
        [Min(0.1f)] public float EmptyViewDistance = 700f;

        readonly RaycastHit[] hits = new RaycastHit[64];
        float currentDistance;

        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
            CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage == CinemachineCore.Stage.Finalize)
            {
                // 거리 검사
                Vector3 forward = state.GetFinalOrientation() * Vector3.forward;
                int count = Physics.RaycastNonAlloc(state.GetFinalPosition(), forward, hits,
                    MaximumDistance, FocusLayers, QueryTriggerInteraction.Ignore);
                float distance = Mathf.Clamp(EmptyViewDistance, MinimumDistance, MaximumDistance);
                float nearest = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    var hit = hits[i];
                    if (IgnoreRoot != null && (hit.transform == IgnoreRoot || hit.transform.IsChildOf(IgnoreRoot)))
                    {
                        continue;
                    }

                    if (hit.distance < nearest)
                    {
                        nearest = hit.distance;
                    }
                }
                if (!float.IsPositiveInfinity(nearest))
                {
                    distance = Mathf.Clamp(nearest, MinimumDistance, MaximumDistance);
                }

                // 초점 이동
                if (deltaTime < 0 || !vcam.PreviousStateIsValid || currentDistance <= 0 || FocusResponse <= 0)
                {
                    currentDistance = distance;
                }

                else if (deltaTime > 0)
                {
                    currentDistance = Mathf.Lerp(currentDistance, distance, 1f - Mathf.Exp(-deltaTime / FocusResponse));
                }

                FocusTracking = FocusTrackingMode.Camera;
                FocusOffset = currentDistance;
            }
            // Volume 반영
            base.PostPipelineStageCallback(vcam, stage, ref state, deltaTime);
        }
    }
}
