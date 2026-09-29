using UnityEngine;

[ExecuteAlways]
public class KaleidoscopeControl : MonoBehaviour
{
    public Material screenMaterial;
    [Range(0f, 1f)] public float strength;
    [Min(2f)] public float segments = 6f;
    public float rotation = 0f;
    [Min(0.01f)] public float zoom = 1f;

    void LateUpdate()
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

    public void Apply()
    {
        if (screenMaterial == null)
        {
            return;
        }
            
            screenMaterial.SetFloat("_Strength", strength);
            screenMaterial.SetFloat("_Segments", Mathf.Max(2f, rotation));
            screenMaterial.SetFloat("_Zoom", Mathf.Max(0.01f, zoom));
            screenMaterial.SetFloat("_Rotation", rotation);
    }

    void OnDisable()
    {
        if (screenMaterial != null)
            screenMaterial.SetFloat("_Strength", 0f);
    }
}
