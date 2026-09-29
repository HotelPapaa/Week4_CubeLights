using UnityEngine;

public class OpeningFlameLight : MonoBehaviour
{
    public Light candle;
    public Gradient gradient;
    [SerializeField] float basicIntensity = 5f;

    Vector3 originPosition;
    float seed;

    void Start()
    {
        originPosition = transform.localPosition;
        seed = Random.value * 100f;
    }

    void Update()
    {
        float time = Time.time * 0.8f;
        float noise = Mathf.PerlinNoise(seed, time) * 0.7f
                + Mathf.PerlinNoise(seed + 10f, time * 2.7f) * 0.3f;

        candle.intensity = basicIntensity  - (Mathf.PerlinNoise(seed, time) * 3f);
        candle.color = gradient.Evaluate(noise);

        transform.localPosition = originPosition + (new Vector3(
            Mathf.PerlinNoise(seed + 70f, time) - 0.5f,
            Mathf.PerlinNoise(seed + 20f, time) - 0.5f,
            Mathf.PerlinNoise(seed + 50f, time) - 0.5f) / 50);
    }
}
