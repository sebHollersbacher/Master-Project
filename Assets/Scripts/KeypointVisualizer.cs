using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class KeypointVisualizer : MonoBehaviour
{
    public GameObject prefab;
    public RectTransform container;
    private List<GameObject> dotPool = new();

    public void UpdateVisuals(List<Vector2> imagePoints)
    {
        while (dotPool.Count < imagePoints.Count)
        {
            GameObject dot = Object.Instantiate(prefab, container);
            Image image = dot.GetComponent<Image>();
            switch (dotPool.Count)
            {
                case 0:
                    image.color = Color.limeGreen;
                    break;
                case 1:
                    image.color = Color.yellow;
                    break;
                case 2:
                    image.color = Color.blue;
                    break;
                case 3:
                    image.color = Color.red;
                    break;
                case 4:
                    image.color = Color.purple;
                    break;
                case 5:
                    image.color = Color.brown;
                    break;
                case 6:
                    image.color = Color.black;
                    break;
                case 7:
                    image.color = Color.white;
                    break;
            }

            // Reset scale in case parent scale is weird
            dot.transform.localScale = Vector3.one;
            // Ensure dot ignores raycasts so you can still click things behind it
            if (dot.GetComponent<Image>() != null)
                dot.GetComponent<Image>().raycastTarget = false;

            dotPool.Add(dot);
        }

        // 2. Hide all initially
        foreach (var d in dotPool) d.SetActive(false);

        // 3. Get the actual size of the floating screen in local coordinates
        float screenWidth = container.rect.width;
        float screenHeight = container.rect.height;

        // 4. Map points
        for (int i = 0; i < imagePoints.Count; i++)
        {
            Vector2 rawPoint = imagePoints[i];

            // Normalize (0 to 1)
            float normX = rawPoint.x / Constants.ModelResolution.x;

            // FLIP Y: YOLO is Top-Left (0,0), Unity is Bottom-Left (0,0)
            float normY = 1.0f - (rawPoint.y / Constants.ModelResolution.y);

            // Convert to Local Position relative to Center Pivot (0.5, 0.5)
            // Result range: -Width/2 to +Width/2
            float localX = (normX - 0.5f) * screenWidth;
            float localY = (normY - 0.5f) * screenHeight;

            GameObject dot = dotPool[i];
            dot.SetActive(true);

            // Using anchoredPosition3D ensures it sticks to the canvas plane perfectly
            RectTransform rt = dot.GetComponent<RectTransform>();
            rt.anchoredPosition3D = new Vector3(localX, localY, 0);
        }
    }
}