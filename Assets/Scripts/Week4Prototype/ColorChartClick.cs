using GameLab.Week4;
using UnityEngine;

/// <summary>
/// 색 조합표를 클릭하면 전용 시네머신 카메라와 기본 카메라 사이를 전환한다.
/// </summary>
public sealed class ColorChartClick : MonoBehaviour
{
    [SerializeField] private LightingSequenceController controller;

    private bool isShowingMagnifyingCursor;

    private void OnMouseEnter()
    {
        if (isShowingMagnifyingCursor) return;

        isShowingMagnifyingCursor = true;
        MagnifyingGlassCursor.Show();
    }

    private void OnMouseExit()
    {
        HideMagnifyingCursor();
    }

    private void OnDisable()
    {
        HideMagnifyingCursor();
    }

    private void OnMouseDown()
    {
        if (controller == null || controller.ColorChartCamera == null) return;

        controller.SelectCamera(
            controller.IsCameraSelected(controller.ColorChartCamera)
                ? controller.defaultCamera
                : controller.ColorChartCamera);
    }

    private void HideMagnifyingCursor()
    {
        if (!isShowingMagnifyingCursor) return;

        isShowingMagnifyingCursor = false;
        MagnifyingGlassCursor.Hide();
    }
}
