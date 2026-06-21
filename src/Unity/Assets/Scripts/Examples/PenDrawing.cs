using UnityEngine;

public class PenDrawing : MonoBehaviour
{
    public Transform penTip;
    
    public float drawDistance = 0.03f;
    public int brushRadius = 3;
    public Color penColor = Color.black;
    public bool interpolateStrokes = true;

    private Texture2D _canvasTexture;
    private Vector2Int _lastPixel = new Vector2Int(-1, -1);
    private bool _wasDrawing = false;

    void Update()
    {
        if (penTip == null) return;

        bool hitForward = Physics.Raycast(penTip.position, penTip.forward, out RaycastHit hitFwd, drawDistance);
        bool hitBackward = Physics.Raycast(penTip.position, -penTip.forward, out RaycastHit hitBack, drawDistance);

        RaycastHit hit = hitForward ? hitFwd : hitBack;
        
        if (hitForward || hitBackward)
        {
            if (hit.collider.CompareTag("Canvas"))
            {
                if (_canvasTexture == null)
                {
                    InitializeCanvasTexture(hit.collider);
                }
        
                // Convert UV (0-1) to pixel coordinates
                Vector2 uv = hit.textureCoord;
                int x = (int)(uv.x * _canvasTexture.width);
                int y = (int)(uv.y * _canvasTexture.height);
                Vector2Int currentPixel = new Vector2Int(x, y);
        
                if (interpolateStrokes && _wasDrawing && _lastPixel.x >= 0)
                {
                    DrawLine(_lastPixel, currentPixel);
                }
                else
                {
                    DrawCircle(x, y);
                }
        
                _canvasTexture.Apply();
                _lastPixel = currentPixel;
                _wasDrawing = true;
                return;
            }
        }

        _wasDrawing = false;
        _lastPixel = new Vector2Int(-1, -1);
    }
    
    private void InitializeCanvasTexture(Collider canvasCollider)
    {
        Renderer renderer = canvasCollider.GetComponent<Renderer>();
        Texture2D originalTex = renderer.material.GetTexture("_BaseMap") as Texture2D;

        if (originalTex != null)
        {
            _canvasTexture = new Texture2D(originalTex.width, originalTex.height, TextureFormat.RGBA32, false);
            _canvasTexture.SetPixels(originalTex.GetPixels());
            _canvasTexture.Apply();
        }
        else
        {
            _canvasTexture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
            Color[] blank = new Color[1024 * 1024];
            for (int i = 0; i < blank.Length; i++) blank[i] = Color.white;
            _canvasTexture.SetPixels(blank);
            _canvasTexture.Apply();
        }

        renderer.material.SetTexture("_BaseMap", _canvasTexture);
    }

    private void DrawCircle(int cx, int cy)
    {
        int w = _canvasTexture.width;
        int h = _canvasTexture.height;

        for (int dx = -brushRadius; dx <= brushRadius; dx++)
        {
            for (int dy = -brushRadius; dy <= brushRadius; dy++)
            {
                if (dx * dx + dy * dy <= brushRadius * brushRadius)
                {
                    int px = cx + dx;
                    int py = cy + dy;
                    if (px >= 0 && px < w && py >= 0 && py < h)
                    {
                        _canvasTexture.SetPixel(px, py, penColor);
                    }
                }
            }
        }
    }

    private void DrawLine(Vector2Int from, Vector2Int to)
    {
        int dx = Mathf.Abs(to.x - from.x);
        int dy = Mathf.Abs(to.y - from.y);
        int sx = from.x < to.x ? 1 : -1;
        int sy = from.y < to.y ? 1 : -1;
        int err = dx - dy;

        int x = from.x;
        int y = from.y;

        while (true)
        {
            DrawCircle(x, y);

            if (x == to.x && y == to.y) break;

            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
    }
}
