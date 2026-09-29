using UnityEngine;

namespace GameLab.Week4
{
    /// <summary>
    /// 퍼즐 규칙에서 구분하는 큐브 종류다.
    /// 기존 프리팹의 직렬화 번호가 바뀌지 않도록 새 종류는 항상 열거형 뒤에 추가한다.
    /// </summary>
    public enum PuzzleCubeType
    {
        Normal,
        Adhesive,
        Glass,
        ColoredGlass,
        Brittle,
        Cracked,
        Styrofoam,
        Ice,
        LightEmitter,
        Refractor,
        LightSplitter
    }

    /// <summary>큐브의 재질별 퍼즐 성질을 보관하고 파괴·용해 상태를 관리한다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DraggableCube))]
    public sealed class PuzzleCubeProperties : MonoBehaviour
    {
        private const string MarkerRootName = "OpticDirectionMarkers";
        private const string LegacyMarkerRootName = "RuntimeOpticMarkers";

        [SerializeField] private PuzzleCubeType cubeType = PuzzleCubeType.Normal;
        [SerializeField] private Color shadowColor = Color.white;
        [Min(0f)] [SerializeField] private float iceMeltDelay = 1f;

        [Header("빛 규칙")]
        [SerializeField] private PuzzleLightColor lightColor = PuzzleLightColor.White;
        [Tooltip("발광 큐브가 빛을 내보내는 로컬 방향")]
        [SerializeField] private Vector3Int emitterLocalDirection = new(-1, 0, 0);
        [Tooltip("굴절 큐브의 첫 번째 구멍이 바라보는 로컬 방향")]
        [SerializeField] private Vector3Int refractorPortALocal = new(0, 0, -1);
        [Tooltip("굴절 큐브의 두 번째 구멍이 바라보는 로컬 방향")]
        [SerializeField] private Vector3Int refractorPortBLocal = new(1, 0, 0);
        [Tooltip("빛 분기 큐브의 첫 번째 로컬 포트 방향")]
        [SerializeField] private Vector3Int splitterInputLocal = new(1, 0, 0);
        [Tooltip("빛 분기 큐브의 두 번째 로컬 포트 방향")]
        [SerializeField] private Vector3Int splitterOutputALocal = new(0, 0, -1);
        [Tooltip("빛 분기 큐브의 세 번째 로컬 포트 방향")]
        [SerializeField] private Vector3Int splitterOutputBLocal = new(0, 0, 1);

        private DraggableCube draggableCube;
        private bool removedByRule;

        public PuzzleCubeType CubeType => cubeType;
        public Color ShadowColor => shadowColor;
        public bool CastsShadow => !removedByRule &&
                                   cubeType != PuzzleCubeType.Glass &&
                                   cubeType != PuzzleCubeType.Ice;
        public bool IsStyrofoam => cubeType == PuzzleCubeType.Styrofoam;
        public bool BreaksUnderWeight => cubeType == PuzzleCubeType.Brittle;
        public bool MeltsInLight => cubeType == PuzzleCubeType.Ice && !removedByRule;
        public float IceMeltDelay => iceMeltDelay;
        public bool EmitsLight => cubeType == PuzzleCubeType.LightEmitter && !removedByRule;
        public bool RefractsLight => cubeType == PuzzleCubeType.Refractor && !removedByRule;
        public bool SplitsLight => cubeType == PuzzleCubeType.LightSplitter && !removedByRule;
        public bool PassesLightStraight => !removedByRule &&
                                           (cubeType == PuzzleCubeType.Glass ||
                                            cubeType == PuzzleCubeType.ColoredGlass ||
                                            cubeType == PuzzleCubeType.Ice);
        public PuzzleLightColor LightColor => lightColor;
        public bool RemovedByRule => removedByRule;

        private void Awake()
        {
            draggableCube = GetComponent<DraggableCube>();
        }

        /// <summary>씬 통합기가 이름으로 추론한 초기 타입을 적용한다.</summary>
        public void Configure(PuzzleCubeType type)
        {
            cubeType = type;
            if (type != PuzzleCubeType.ColoredGlass)
            {
                lightColor = PuzzleLightColor.White;
            }
        }

        /// <summary>프리팹 생성기가 큐브 종류와 빛 색을 함께 설정할 때 사용한다.</summary>
        public void ConfigureOptics(PuzzleCubeType type, PuzzleLightColor color)
        {
            cubeType = type;
            lightColor = color;

            // SampleScene_Test에서 플레이어 카메라는 보드의 +X 쪽에 있으므로
            // 맞은편 전등판을 향하는 기본 발광 면은 -X이다.
            if (type == PuzzleCubeType.LightEmitter)
            {
                emitterLocalDirection = Vector3Int.left;
            }
        }

        /// <summary>발광 면을 현재 큐브 회전과 보드 회전에 맞춘 격자 방향으로 반환한다.</summary>
        public Vector3Int GetEmitterDirection(Transform boardTransform)
        {
            return TransformLocalDirection(emitterLocalDirection, boardTransform);
        }

        /// <summary>
        /// 빛이 굴절 큐브의 두 구멍 중 하나로 들어왔을 때 반대 구멍의 진행 방향을 반환한다.
        /// 구멍이 아닌 면으로 들어온 빛은 차단된다.
        /// </summary>
        public bool TryGetRefractedDirection(
            Vector3Int incomingTravelDirection,
            Transform boardTransform,
            out Vector3Int outgoingDirection)
        {
            Vector3Int portA = TransformLocalDirection(refractorPortALocal, boardTransform);
            Vector3Int portB = TransformLocalDirection(refractorPortBLocal, boardTransform);
            Vector3Int entryFace = -incomingTravelDirection;

            if (entryFace == portA)
            {
                outgoingDirection = portB;
                return true;
            }

            if (entryFace == portB)
            {
                outgoingDirection = portA;
                return true;
            }

            outgoingDirection = Vector3Int.zero;
            return false;
        }

        /// <summary>
        /// 세 포트 중 하나로 빛이 들어오면 같은 색의 빛을 나머지 두 포트로 복제한다.
        /// 포트가 아닌 면에 닿은 빛은 분기되지 않고 차단된다.
        /// </summary>
        public bool TryGetSplitDirections(
            Vector3Int incomingTravelDirection,
            Transform boardTransform,
            out Vector3Int outputA,
            out Vector3Int outputB)
        {
            TryGetSplitterPorts(boardTransform, out Vector3Int portA, out Vector3Int portB, out Vector3Int portC);
            Vector3Int entryFace = -incomingTravelDirection;
            outputA = Vector3Int.zero;
            outputB = Vector3Int.zero;

            if (portA == Vector3Int.zero || portB == Vector3Int.zero || portC == Vector3Int.zero ||
                portA == portB || portA == portC || portB == portC)
            {
                return false;
            }

            if (entryFace == portA)
            {
                outputA = portB;
                outputB = portC;
                return true;
            }

            if (entryFace == portB)
            {
                outputA = portA;
                outputB = portC;
                return true;
            }

            if (entryFace == portC)
            {
                outputA = portA;
                outputB = portB;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 분배기의 세 포트를 보드 기준 방향으로 반환한다.
        /// 세 포트는 입력/출력으로 고정되지 않으며 빛이 들어오는 순간의 입사 포트만 입력이 된다.
        /// </summary>
        public bool TryGetSplitterPorts(
            Transform boardTransform,
            out Vector3Int portA,
            out Vector3Int portB,
            out Vector3Int portC)
        {
            // 기존 프리팹과 스테이지의 직렬화 데이터를 보존하기 위해 필드 이름은 유지한다.
            portA = TransformLocalDirection(splitterInputLocal, boardTransform);
            portB = TransformLocalDirection(splitterOutputALocal, boardTransform);
            portC = TransformLocalDirection(splitterOutputBLocal, boardTransform);
            return portA != Vector3Int.zero && portB != Vector3Int.zero && portC != Vector3Int.zero &&
                   portA != portB && portA != portC && portB != portC;
        }

        /// <summary>바사삭 큐브를 격자에서 제거하고 시각 오브젝트도 숨긴다.</summary>
        public void Break()
        {
            SoundManager.Play(SoundEventId.BrittleBreak, transform.position);
            RemoveFromPuzzle();
        }

        /// <summary>GridBoard가 낙하 충격을 계산한 뒤, 중복 제거 없이 시각 오브젝트만 정리한다.</summary>
        public void BreakAfterBoardDetach()
        {
            if (removedByRule) return;
            SoundManager.Play(SoundEventId.BrittleBreak, transform.position);
            removedByRule = true;
            gameObject.SetActive(false);
        }

        /// <summary>GridBoard가 용해 대기 시간을 처리한 뒤 얼음 큐브를 제거한다.</summary>
        public void ApplyLightEffectNow()
        {
            if (cubeType == PuzzleCubeType.Ice)
            {
                SoundManager.Play(SoundEventId.IceMelt, transform.position);
                RemoveFromPuzzle(animateCollapse: true);
            }
        }

        private void RemoveFromPuzzle(bool animateCollapse = false)
        {
            if (removedByRule) return;

            removedByRule = true;
            if (draggableCube == null)
            {
                draggableCube = GetComponent<DraggableCube>();
            }

            draggableCube.RemoveFromBoardForRule(animateCollapse);
            gameObject.SetActive(false);
        }

        /// <summary>무르기 스냅숏에 저장된 규칙 제거 상태와 활성 상태를 복원한다.</summary>
        public void RestoreUndoState(bool wasRemovedByRule, bool wasActive)
        {
            removedByRule = wasRemovedByRule;
            gameObject.SetActive(wasActive);
        }

        private Vector3Int TransformLocalDirection(Vector3Int localDirection, Transform boardTransform)
        {
            Vector3 worldDirection = transform.TransformDirection((Vector3)localDirection);
            Vector3 boardDirection = boardTransform != null
                ? boardTransform.InverseTransformDirection(worldDirection)
                : worldDirection;
            return LightDirectionUtility.Quantize(boardDirection);
        }

        /// <summary>프리팹에 표식이 없는 이전 애셋도 Play 중에는 방향 표식을 자동으로 보완한다.</summary>
        private void EnsureOpticMarker()
        {
            // Refractor는 프리팹에 직접 제작된 화살표와 굴절 경로 모델을 그대로 사용한다.
            // 런타임 도형을 덧붙이면 저작된 외형이 달라지므로 Emitter만 자동 보완한다.
            if (cubeType != PuzzleCubeType.LightEmitter) return;
            if (transform.Find(MarkerRootName) != null || transform.Find(LegacyMarkerRootName) != null) return;

            RebuildEmitterMarkers(null);
        }

        /// <summary>
        /// 발광 큐브의 금색 방향 표식만 다시 만든다.
        /// Refractor의 외형은 수동 제작된 프리팹을 그대로 사용하므로 이 경로에서 변경하지 않는다.
        /// </summary>
        public void RebuildEmitterMarkers(Material emitterMarkerMaterial)
        {
            RemoveMarkerRoot(MarkerRootName);
            RemoveMarkerRoot(LegacyMarkerRootName);
            if (cubeType != PuzzleCubeType.LightEmitter) return;

            GameObject root = new(MarkerRootName);
            root.transform.SetParent(transform, false);

            Material material = emitterMarkerMaterial != null
                ? emitterMarkerMaterial
                : CreateRuntimeMarkerMaterial(new Color(1f, 0.72f, 0.08f));
            CreateFaceArrow(root.transform, emitterLocalDirection, material, false, "Emitter_Output");

            // 기본 발광 방향이 수평일 때 위·아래 면에도 같은 방향 화살표를 넣어
            // 발광 면이 카메라 반대편에 있어도 방향을 읽을 수 있게 한다.
            if (emitterLocalDirection.y == 0)
            {
                CreateSurfaceDirectionArrow(root.transform, Vector3Int.up, emitterLocalDirection, material, "Emitter_TopGuide");
                CreateSurfaceDirectionArrow(root.transform, Vector3Int.down, emitterLocalDirection, material, "Emitter_BottomGuide");
            }
        }

        /// <summary>새 빛 분기 프리팹의 T자형 입력·출력 방향을 최초 한 번 설정한다.</summary>
        public void ConfigureSplitterDefaults()
        {
            cubeType = PuzzleCubeType.LightSplitter;
            lightColor = PuzzleLightColor.White;
            splitterInputLocal = Vector3Int.right;
            splitterOutputALocal = new Vector3Int(0, 0, -1);
            splitterOutputBLocal = new Vector3Int(0, 0, 1);
        }

        private void RemoveMarkerRoot(string rootName)
        {
            Transform markerRoot = transform.Find(rootName);
            if (markerRoot == null) return;

            if (Application.isPlaying)
            {
                Destroy(markerRoot.gameObject);
            }
            else
            {
                DestroyImmediate(markerRoot.gameObject);
            }
        }

        private static void CreateFaceArrow(
            Transform parent,
            Vector3Int faceDirection,
            Material material,
            bool doubleHeaded,
            string markerName)
        {
            Transform faceRoot = CreateFaceRoot(parent, faceDirection, markerName);
            CreateArrowGlyph(faceRoot, material, doubleHeaded, 1f);
        }

        private static void CreateSurfaceDirectionArrow(
            Transform parent,
            Vector3Int faceNormal,
            Vector3Int arrowDirection,
            Material material,
            string markerName)
        {
            Transform faceRoot = CreateFaceRoot(parent, faceNormal, markerName);
            Vector3 tangent = Quaternion.Inverse(faceRoot.localRotation) * (Vector3)arrowDirection;
            tangent.z = 0f;
            if (tangent.sqrMagnitude < 0.001f) return;

            GameObject directionRoot = new("Direction");
            directionRoot.transform.SetParent(faceRoot, false);
            float angle = -Mathf.Atan2(tangent.x, tangent.y) * Mathf.Rad2Deg;
            directionRoot.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            CreateArrowGlyph(directionRoot.transform, material, false, 0.82f);
        }

        private void CreateCornerDiagram(
            Transform parent,
            Vector3Int faceNormal,
            Material material,
            string markerName)
        {
            Transform faceRoot = CreateFaceRoot(parent, faceNormal, markerName);
            CreateCornerArm(faceRoot, refractorPortALocal, material, "Port_A_Path");
            CreateCornerArm(faceRoot, refractorPortBLocal, material, "Port_B_Path");
            CreateMarkerPart(
                faceRoot,
                "Corner",
                Vector3.zero,
                new Vector3(0.13f, 0.13f, 0.032f),
                Quaternion.Euler(0f, 0f, 45f),
                material);
        }

        private static void CreateCornerArm(
            Transform faceRoot,
            Vector3Int portDirection,
            Material material,
            string armName)
        {
            Vector3 tangent = Quaternion.Inverse(faceRoot.localRotation) * (Vector3)portDirection;
            tangent.z = 0f;
            if (tangent.sqrMagnitude < 0.001f) return;
            tangent.Normalize();

            float angle = -Mathf.Atan2(tangent.x, tangent.y) * Mathf.Rad2Deg;
            GameObject armRoot = new(armName);
            armRoot.transform.SetParent(faceRoot, false);
            armRoot.transform.localRotation = Quaternion.Euler(0f, 0f, angle);

            CreateMarkerPart(
                armRoot.transform,
                "Arm",
                new Vector3(0f, 0.19f, 0f),
                new Vector3(0.065f, 0.38f, 0.028f),
                Quaternion.identity,
                material);
            GameObject endPoint = new("End_Arrow");
            endPoint.transform.SetParent(armRoot.transform, false);
            endPoint.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            CreateArrowHead(endPoint.transform, material, true, 0.82f);
        }

        private static Transform CreateFaceRoot(Transform parent, Vector3Int faceDirection, string markerName)
        {
            GameObject marker = new(markerName);
            marker.transform.SetParent(parent, false);
            marker.transform.localPosition = (Vector3)faceDirection * 0.515f;
            Vector3 normal = ((Vector3)faceDirection).normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f
                ? Vector3.forward
                : Vector3.up;
            marker.transform.localRotation = Quaternion.LookRotation(normal, up);
            return marker.transform;
        }

        private static void CreateArrowGlyph(
            Transform parent,
            Material material,
            bool doubleHeaded,
            float scale)
        {
            CreateMarkerPart(
                parent,
                "Shaft",
                Vector3.zero,
                new Vector3(0.07f, 0.34f, 0.03f) * scale,
                Quaternion.identity,
                material);
            CreateArrowHead(parent, material, true, scale);
            if (doubleHeaded)
            {
                CreateArrowHead(parent, material, false, scale);
            }
        }

        private static void CreateArrowHead(Transform parent, Material material, bool positive, float scale)
        {
            float sign = positive ? 1f : -1f;
            float y = sign * 0.15f * scale;
            float rotation = positive ? 45f : -45f;
            CreateMarkerPart(
                parent,
                positive ? "Head_Positive_L" : "Head_Negative_L",
                new Vector3(-0.055f * scale, y, 0f),
                new Vector3(0.06f, 0.19f, 0.034f) * scale,
                Quaternion.Euler(0f, 0f, rotation),
                material);
            CreateMarkerPart(
                parent,
                positive ? "Head_Positive_R" : "Head_Negative_R",
                new Vector3(0.055f * scale, y, 0f),
                new Vector3(0.06f, 0.19f, 0.034f) * scale,
                Quaternion.Euler(0f, 0f, -rotation),
                material);
        }

        private static void CreateMarkerPart(
            Transform parent,
            string partName,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;

            if (part.TryGetComponent(out Collider markerCollider))
            {
                if (Application.isPlaying) Destroy(markerCollider);
                else DestroyImmediate(markerCollider);
            }

            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Material CreateRuntimeMarkerMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null) return null;

            Material material = new(shader) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", color * 1.6f);
                material.EnableKeyword("_EMISSION");
            }

            return material;
        }
    }
}
