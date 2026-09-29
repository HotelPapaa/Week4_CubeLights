using UnityEngine;
using Unity.Cinemachine;
using GameLab.Week4;

public class EasterEgg_click : MonoBehaviour
{
    [SerializeField] LightingSequenceController controller;
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
}
