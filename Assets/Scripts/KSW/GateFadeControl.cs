using UnityEngine;

[ExecuteAlways]
public class GateFadeControl : MonoBehaviour
{
    public Material gateMaterial;

    [Range(0f, 1f)]
    public float fade;

    static readonly int fadeId = Shader.PropertyToID("_Fade");

    void Update()
    {
        Apply();    
    }
    void OnEnable()
    {
        Apply();
    }
    void OnValidate()
    {
        Apply(); 
    }
    void OnDidApplyAnimationProperties()
    {
        Apply();
    }

    void Apply()
    {
        if (gateMaterial == null)
        {
            return;
        }

        gateMaterial.SetFloat(fadeId, fade);
    }
}
