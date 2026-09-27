using System.Collections.Generic;
using UnityEngine;

namespace GameLab.Week4
{
    /// <summary>
    /// 논리 격자에서 계산한 광선과 전등 상태를 런타임 도형으로 보여준다.
    /// 기존 씬의 모델과 재질은 수정하지 않고 Week4Gameplay 아래에 표시 전용 오브젝트만 만든다.
    /// </summary>
    public sealed class LightPuzzleVisualizer : MonoBehaviour
    {
        private const int PanelColumns = 5;
        private const int PanelRows = 3;
        private const int PanelGridX = -1;

        [SerializeField] private GridBoard board;
        [Min(0.01f)] [SerializeField] private float beamWidthRatio = 0.08f;
        [Header("목표 스테이지 위치")]
        [Tooltip("보드 로컬 축 기준 오프셋입니다. X: 앞/뒤, Y: 높이, Z: 좌/우")]
        [SerializeField] private Vector3 panelPositionOffset;
        [SerializeField] private Material panelCellMaterial;
        [SerializeField] private Material lampOffMaterial;

        private readonly List<LineRenderer> beamLines = new();
        private readonly List<Renderer> lampRenderers = new();
        private Transform beamRoot;
        private Transform panelRoot;
        private Transform cellRoot;
        private Transform lampRoot;
        private PuzzleStageDefinition stage;
        private LightSimulationResult lastResult;
        private bool beamsVisible;

        public void Initialize(GridBoard targetBoard)
        {
            board = targetBoard;
            EnsureRoots();
        }

        /// <summary>에디터에서 저장한 전등판 재질을 연결해 씬 뷰에서도 같은 외형을 유지한다.</summary>
        public void ConfigureMaterials(Material cellMaterial, Material offMaterial)
        {
            panelCellMaterial = cellMaterial;
            lampOffMaterial = offMaterial;
        }

        private void OnValidate()
        {
            if (Application.isPlaying || board == null) return;
            CacheExistingPanelRoots();
            RefreshExistingPanelPositions();
        }

        /// <summary>Space 결과 화면에 표시할 5x3 배경 셀과 목표 전등을 다시 만든다.</summary>
        public void ConfigureStage(PuzzleStageDefinition targetStage)
        {
            stage = targetStage;
            lastResult = null;
            EnsureRoots();
            ClearChildren(cellRoot);
            ClearChildren(lampRoot);
            lampRenderers.Clear();

            if (board == null || stage == null) return;

            // 결과 화면의 5x3 셀은 플레이용 격자가 아니라 건너편 벽에 놓인 전등판이다.
            for (int row = 0; row < PanelRows; row++)
            {
                for (int column = 0; column < PanelColumns; column++)
                {
                    CreatePanelCell(column, row);
                }
            }

            foreach (LampTarget lamp in stage.LampTargets)
            {
                int panelColumn = GetPanelColumn(lamp.gridPosition);
                if (panelColumn < 0 || panelColumn >= PanelColumns ||
                    lamp.gridPosition.y < 0 || lamp.gridPosition.y >= PanelRows)
                {
                    Debug.LogWarning(
                        $"전등 좌표 {lamp.gridPosition}가 정면 5x3 전등판을 벗어났습니다.",
                        this);
                    continue;
                }

                // 전등은 요청대로 추가 모델 없이 Unity Primitive Sphere 하나만 사용한다.
                GameObject lampObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lampObject.name = $"Lamp_{lamp.gridPosition.x}_{lamp.gridPosition.y}_{lamp.gridPosition.z}";
                lampObject.transform.SetParent(lampRoot, false);
                lampObject.transform.position = GetPanelCellPosition(panelColumn, lamp.gridPosition.y) +
                                                board.transform.right * GetPanelThickness();
                float diameter = Mathf.Min(board.CellSize, board.CubeHeight) * 0.42f;
                lampObject.transform.localScale = Vector3.one * Mathf.Max(0.06f, diameter);
                if (lampObject.TryGetComponent(out Collider lampCollider)) Destroy(lampCollider);

                Renderer renderer = lampObject.GetComponent<Renderer>();
                renderer.sharedMaterial = lampOffMaterial != null
                    ? lampOffMaterial
                    : CreateDisplayMaterial(GetDimColor(lamp.requiredColor));
                if (Application.isPlaying)
                {
                    SetMaterialColor(renderer.material, GetDimColor(lamp.requiredColor));
                }
                lampRenderers.Add(renderer);
            }

            // 목표 스테이지는 편집 중과 플레이 중 모두 보이고, 광선만 Space로 전환한다.
            panelRoot.gameObject.SetActive(true);
        }

        /// <summary>현재 광선 경로와 각 전등의 성공·오입사 상태를 갱신한다.</summary>
        public void ShowResult(LightSimulationResult result)
        {
            lastResult = result;
            EnsureRoots();
            ClearChildren(beamRoot);
            beamLines.Clear();

            if (result == null || board == null) return;
            foreach (LightBeamSegment segment in result.Segments)
            {
                GameObject beamObject = new("Beam");
                beamObject.transform.SetParent(beamRoot, false);
                LineRenderer line = beamObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.SetPosition(0, board.GridToWorld(segment.From));
                line.SetPosition(1, board.GridToWorld(segment.To));
                line.startWidth = line.endWidth = Mathf.Max(0.01f, board.CellSize * beamWidthRatio);
                line.numCapVertices = 4;
                Color beamColor = LightDirectionUtility.ToDisplayColor(segment.Color);
                line.startColor = line.endColor = beamColor;
                line.material = CreateDisplayMaterial(beamColor);
                beamLines.Add(line);
            }

            for (int index = 0; index < lampRenderers.Count; index++)
            {
                Renderer renderer = lampRenderers[index];
                if (renderer == null || index >= result.Lamps.Count) continue;

                LampLightResult lamp = result.Lamps[index];
                Color color = lamp.HasInvalidHit
                    ? new Color(1f, 0.04f, 0.03f)
                    : lamp.IsSatisfied
                        ? LightDirectionUtility.ToDisplayColor(lamp.Target.requiredColor) * 1.5f
                        : GetDimColor(lamp.Target.requiredColor);
                SetMaterialColor(renderer.material, color);
            }

            beamRoot.gameObject.SetActive(beamsVisible);
        }

        /// <summary>스페이스바 결과 화면에서는 광선만 켜고 끈다. 목표 전등판은 항상 보인다.</summary>
        public void SetBeamsVisible(bool visible)
        {
            beamsVisible = visible;
            EnsureRoots();
            beamRoot.gameObject.SetActive(visible);
            panelRoot.gameObject.SetActive(true);

            if (!visible && stage != null && Application.isPlaying)
            {
                for (int index = 0; index < lampRenderers.Count && index < stage.LampTargets.Count; index++)
                {
                    bool staysOn = lastResult != null &&
                                   index < lastResult.Lamps.Count &&
                                   lastResult.Lamps[index].IsLatchedOn;
                    Color color = staysOn
                        ? LightDirectionUtility.ToDisplayColor(stage.LampTargets[index].requiredColor) * 1.5f
                        : GetDimColor(stage.LampTargets[index].requiredColor);
                    SetMaterialColor(lampRenderers[index].material, color);
                }
            }
        }

        /// <summary>Stage Camera가 기존 그림자 벽 대신 새 5x3 전등판과 광선을 바라보게 한다.</summary>
        public void FocusStageCamera(Camera stageCamera)
        {
            if (stageCamera == null || board == null) return;

            Vector3 center = GetPanelCellPosition((PanelColumns - 1) * 0.5f, (PanelRows - 1) * 0.5f);
            float panelWidth = PanelColumns * board.CellSize;
            float panelHeight = PanelRows * board.CubeHeight;
            float distance = Mathf.Max(panelWidth, panelHeight) * 2.15f;

            // 전등판의 +X 정면에서 바라보되 광선의 길이도 보이도록 옆·위에 아주 약한 각도를 준다.
            Vector3 cameraPosition = center + board.transform.right * distance +
                                     board.transform.forward * (panelWidth * 0.08f) +
                                     board.transform.up * (panelHeight * 0.08f);
            stageCamera.transform.SetPositionAndRotation(
                cameraPosition,
                Quaternion.LookRotation(center - cameraPosition, board.transform.up));
            stageCamera.orthographic = false;
            stageCamera.fieldOfView = 40f;
            stageCamera.clearFlags = CameraClearFlags.SolidColor;
            stageCamera.backgroundColor = new Color(0.012f, 0.016f, 0.022f, 1f);
        }

        private void EnsureRoots()
        {
            if (beamRoot == null)
            {
                GameObject root = new("RuntimeLightBeams");
                root.transform.SetParent(transform, false);
                beamRoot = root.transform;
                beamRoot.gameObject.SetActive(beamsVisible);
            }

            if (panelRoot == null)
            {
                panelRoot = transform.Find("LightTargetStage_5x3") ??
                            transform.Find("RuntimeLampPanel5x3");
                if (panelRoot == null)
                {
                    GameObject root = new("LightTargetStage_5x3");
                    root.transform.SetParent(transform, false);
                    panelRoot = root.transform;
                }

                panelRoot.name = "LightTargetStage_5x3";
                panelRoot.gameObject.SetActive(true);
            }

            if (cellRoot == null)
            {
                cellRoot = panelRoot.Find("Cells");
                if (cellRoot == null)
                {
                    GameObject root = new("Cells");
                    root.transform.SetParent(panelRoot, false);
                    cellRoot = root.transform;
                }
            }

            if (lampRoot == null)
            {
                lampRoot = panelRoot.Find("Lamps");
                if (lampRoot == null)
                {
                    GameObject root = new("Lamps");
                    root.transform.SetParent(panelRoot, false);
                    lampRoot = root.transform;
                }
            }
        }

        private void CacheExistingPanelRoots()
        {
            if (panelRoot == null)
            {
                panelRoot = transform.Find("LightTargetStage_5x3") ??
                            transform.Find("RuntimeLampPanel5x3");
            }

            if (panelRoot == null) return;
            if (cellRoot == null) cellRoot = panelRoot.Find("Cells");
            if (lampRoot == null) lampRoot = panelRoot.Find("Lamps");
        }

        /// <summary>Inspector의 위치 오프셋이 바뀌면 저장된 프리뷰 도형만 즉시 재배치한다.</summary>
        private void RefreshExistingPanelPositions()
        {
            if (cellRoot != null)
            {
                foreach (Transform cell in cellRoot)
                {
                    string[] parts = cell.name.Split('_');
                    if (parts.Length != 3 ||
                        !int.TryParse(parts[1], out int column) ||
                        !int.TryParse(parts[2], out int row))
                    {
                        continue;
                    }

                    cell.position = GetPanelCellPosition(column, row);
                }
            }

            if (lampRoot != null)
            {
                foreach (Transform lamp in lampRoot)
                {
                    string[] parts = lamp.name.Split('_');
                    if (parts.Length != 4 ||
                        !int.TryParse(parts[1], out int gridX) ||
                        !int.TryParse(parts[2], out int gridY) ||
                        !int.TryParse(parts[3], out int gridZ))
                    {
                        continue;
                    }

                    var gridPosition = new Vector3Int(gridX, gridY, gridZ);
                    int panelColumn = GetPanelColumn(gridPosition);
                    if (panelColumn < 0 || panelColumn >= PanelColumns) continue;
                    lamp.position = GetPanelCellPosition(panelColumn, gridY) +
                                    board.transform.right * GetPanelThickness();
                }
            }
        }

        private void CreatePanelCell(int column, int row)
        {
            GameObject cell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cell.name = $"Cell_{column}_{row}";
            cell.transform.SetParent(cellRoot, false);
            cell.transform.position = GetPanelCellPosition(column, row);
            // 큐브의 얇은 로컬 Z축을 보드 X축에 맞춰 전등판이 실제 보드 정면을 향하게 한다.
            cell.transform.rotation = Quaternion.LookRotation(board.transform.right, board.transform.up);
            cell.transform.localScale = new Vector3(
                board.CellSize * 0.86f,
                board.CubeHeight * 0.86f,
                GetPanelThickness());
            if (cell.TryGetComponent(out Collider cellCollider)) Destroy(cellCollider);
            cell.GetComponent<Renderer>().sharedMaterial = panelCellMaterial != null
                ? panelCellMaterial
                : CreateDisplayMaterial(new Color(0.045f, 0.055f, 0.07f));
        }

        private Vector3 GetPanelCellPosition(float column, float row)
        {
            Vector3 basePosition = board.GridToWorld(new Vector3Int(PanelGridX, 0, GetPanelDepthStart()));
            Vector3 worldOffset = board.transform.right * panelPositionOffset.x +
                                  board.transform.up * panelPositionOffset.y +
                                  board.transform.forward * panelPositionOffset.z;
            return basePosition + worldOffset +
                   board.transform.forward * (column * board.CellSize) +
                   board.transform.up * (row * board.CubeHeight);
        }

        private int GetPanelColumn(Vector3Int gridPosition)
        {
            if (gridPosition.x != PanelGridX) return -1;
            return gridPosition.z - GetPanelDepthStart();
        }

        private int GetPanelDepthStart()
        {
            return Mathf.Max(0, (board.Depth - PanelColumns) / 2);
        }

        private float GetPanelThickness()
        {
            return Mathf.Max(0.015f, Mathf.Min(board.CellSize, board.CubeHeight) * 0.08f);
        }

        private static Material CreateDisplayMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Sprites/Default") ??
                            Shader.Find("Unlit/Color");
            Material material = new(shader);
            SetMaterialColor(material, color);
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null) return;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", color * 1.2f);
                material.EnableKeyword("_EMISSION");
            }
        }

        private static Color GetDimColor(PuzzleLightColor color)
        {
            Color baseColor = LightDirectionUtility.ToDisplayColor(color);
            return new Color(baseColor.r * 0.16f, baseColor.g * 0.16f, baseColor.b * 0.16f, 1f);
        }

        private static void ClearChildren(Transform root)
        {
            if (root == null) return;
            for (int index = root.childCount - 1; index >= 0; index--)
            {
                GameObject child = root.GetChild(index).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }
    }
}
