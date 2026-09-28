using UnityEngine;
using Unity.Cinemachine;
using GameLab.Week4;

public class RulebookClick : MonoBehaviour
{
    [SerializeField] LightingSequenceController controller;
    public void OnMouseDown()
    {
        if(controller.RuleCamera.Priority < 1)
        {
            controller.SelectCamera(controller.RuleCamera);
        }
        else if (controller.RuleCamera.Priority > 1)
        {
            controller.SelectCamera(controller.defaultCamera);
        }
    }
}
