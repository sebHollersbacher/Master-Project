using System;
using System.Collections;
using System.Threading.Tasks;
using Meta.Net.NativeWebSocket;
using PassthroughCameraSamples;
using UnityEngine;
using UnityEngine.UI;

public class ImageSender : MonoBehaviour
{
    [SerializeField] private WebCamTextureManager m_webCamTextureManager;
    [SerializeField] private string wsUrl = "ws://192.168.0.208:9002";
    [SerializeField] private float fps = 30f;
    [SerializeField] private Transform _transform;
    [SerializeField] private Transform _ref;
    

    private WebSocket websocket;
    private Texture2D frameTex;
    private float sendTimer;

    private void Start()
    {
        Connect();
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    private void Update()
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
            return;

        sendTimer += Time.deltaTime;
        if (sendTimer >= 1f / fps)
        {
            sendTimer = 0f;
            SendCurrentFrame();
        }
    }

    private void OnMessageReceived(byte[] data, int offset, int length)
    {
        try
        {
            string msg = System.Text.Encoding.UTF8.GetString(data, offset, length);
            ApplyTransformMessage(msg);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DEBUG] Error decoding message: {ex.Message}");
        }
    }

    private async void SendCurrentFrame()
    {
        try
        {
            var cam = m_webCamTextureManager.WebCamTexture;
            if (cam == null || cam.width == 0 || cam.height == 0)
                return;

            if (frameTex == null || frameTex.width != cam.width || frameTex.height != cam.height)
                frameTex = new Texture2D(cam.width, cam.height, TextureFormat.RGB24, false);

            frameTex.SetPixels(cam.GetPixels());
            frameTex.Apply(false);

            byte[] jpgBytes = frameTex.EncodeToJPG(80);

            if (websocket == null || websocket.State != WebSocketState.Open)
                return;
            await websocket.Send(jpgBytes);
        }
        catch (Exception e)
        {
            Debug.LogError($"[DEBUG] Send error: {e.Message}");
        }
    }

    private void ApplyTransformMessage(string msg)
    {
        string[] lines = msg.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;

        foreach (string line in lines)
        {
            string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            Debug.Log(parts);

            if (parts[0] == "T" && parts.Length >= 4)
            {
                float.TryParse(parts[1], out position.x);
                float.TryParse(parts[2], out position.y);
                float.TryParse(parts[3], out position.z);
            }
            else if (parts[0] == "R" && parts.Length >= 5)
            {
                float.TryParse(parts[1], out float rx);
                float.TryParse(parts[2], out float ry);
                float.TryParse(parts[3], out float rz);
                float.TryParse(parts[4], out float rw);
                
                rotation = new Quaternion(rx, ry, rz, rw);
            }
        }
        Quaternion Bq = new Quaternion(1f, 0f, 0f, 0f);      // 180° about X
        Quaternion rotLocal = Bq * rotation * Bq; 
        
        Quaternion rotZ = Quaternion.AngleAxis(180f, Vector3.forward);
        rotLocal = rotZ * rotLocal;

        position.y = -position.y;
        position.z = 0.5f * position.z;
        
        Debug.Log("[DEBUG] Rotation: " + rotation.eulerAngles);
        _transform.position = _ref.TransformPoint(position);
        _transform.rotation = _ref.rotation * rotLocal;
    }

    private async void OnApplicationQuit()
    {
        if (websocket != null)
            await websocket.Close();
    }

    public async void Connect()
    {
        Debug.Log("[DEBUG] Connection");

        while (m_webCamTextureManager.WebCamTexture == null ||
               !m_webCamTextureManager.WebCamTexture.isPlaying)
        {
            await Task.Yield();
        }

        Debug.Log($"[DEBUG] {PassthroughCameraUtils.GetCameraIntrinsics(m_webCamTextureManager.Eye).ToString()}");

        websocket = new WebSocket(wsUrl);
        websocket.OnOpen += () => Debug.Log("[DEBUG] Connected");
        websocket.OnError += e => Debug.LogError($"[DEBUG] WebSocket error: {e}");
        websocket.OnMessage += OnMessageReceived;

        await websocket.Connect();
    }

    public async void Disconnect()
    {
        if (websocket == null) return;

        websocket.OnMessage -= OnMessageReceived;

        if (websocket.State == WebSocketState.Open)
        {
            try
            {
                await websocket.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DEBUG] WebSocket close warning: {e.Message}");
            }
        }

        websocket = null;
        Debug.Log("[DEBUG] Disconnected");
    }
}