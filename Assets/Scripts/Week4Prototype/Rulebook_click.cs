using UnityEngine;
using Unity.Cinemachine;
using GameLab.Week4;

public class RulebookClick : MonoBehaviour
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
        if(controller.RuleCamera.Priority < 1)
        {
            controller.SelectCamera(controller.RuleCamera);
        }
        else if (controller.RuleCamera.Priority > 1)
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

internal static class MagnifyingGlassCursor
{
    private const int CursorSize = 32;
    private const int LensCenter = 10;
    private const float LensRadius = 7.5f;

    private static Texture2D cursorTexture;
    private static int hoverOwnerCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        cursorTexture = null;
        hoverOwnerCount = 0;
    }

    public static void Show()
    {
        hoverOwnerCount++;
        Cursor.SetCursor(GetOrCreateTexture(), new Vector2(LensCenter, LensCenter), CursorMode.Auto);
    }

    public static void Hide()
    {
        hoverOwnerCount = Mathf.Max(0, hoverOwnerCount - 1);
        if (hoverOwnerCount == 0)
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }

    private static Texture2D GetOrCreateTexture()
    {
        if (cursorTexture != null) return cursorTexture;

        cursorTexture = new Texture2D(CursorSize, CursorSize, TextureFormat.RGBA32, false)
        {
            name = "Runtime Magnifying Glass Cursor",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };

        Color32[] pixels = new Color32[CursorSize * CursorSize];
        Color32 outline = new(255, 255, 255, 255);
        Color32 body = new(24, 24, 24, 255);

        DrawHandle(pixels, outline, 3);
        DrawHandle(pixels, body, 1);
        DrawLens(pixels, outline, 2.5f);
        DrawLens(pixels, body, 1.25f);

        cursorTexture.SetPixels32(pixels);
        cursorTexture.Apply(false, false);
        return cursorTexture;
    }

    private static void DrawLens(Color32[] pixels, Color32 color, float halfThickness)
    {
        for (int y = 0; y < CursorSize; y++)
        {
            for (int x = 0; x < CursorSize; x++)
            {
                float deltaX = x - LensCenter;
                float deltaY = y - LensCenter;
                float distance = Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);
                if (Mathf.Abs(distance - LensRadius) <= halfThickness)
                {
                    SetPixel(pixels, x, y, color);
                }
            }
        }
    }

    private static void DrawHandle(Color32[] pixels, Color32 color, int radius)
    {
        for (int step = 0; step <= 12; step++)
        {
            int center = 16 + step;
            for (int y = center - radius; y <= center + radius; y++)
            {
                for (int x = center - radius; x <= center + radius; x++)
                {
                    SetPixel(pixels, x, y, color);
                }
            }
        }
    }

    private static void SetPixel(Color32[] pixels, int x, int y, Color32 color)
    {
        if (x < 0 || x >= CursorSize || y < 0 || y >= CursorSize) return;
        pixels[y * CursorSize + x] = color;
    }
}
