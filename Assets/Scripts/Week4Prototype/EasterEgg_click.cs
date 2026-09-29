using UnityEngine;
using Unity.Cinemachine;
using GameLab.Week4;

public class EasterEgg_click : MonoBehaviour
{
    [SerializeField] LightingSequenceController controller;
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

    public void OnMouseDown()
    {
        // Debug.Log("OnMouseDown");
        if(controller.EasterEggCamera.Priority < 1)
        {
            controller.SelectCamera(controller.EasterEggCamera);
        }
        else if (controller.EasterEggCamera.Priority > 1)
        {
            controller.SelectCamera(controller.defaultCamera);
        }
    }

    private void HideMagnifyingCursor()
    {
        if (!isShowingMagnifyingCursor) return;

        isShowingMagnifyingCursor = false;
        MagnifyingGlassCursor.Hide();
    }
}
