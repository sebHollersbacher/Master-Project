using UnityEngine;
using UnityEngine.UI;
using System;
using System.Text;
using System.Collections;
using Meta.Net.NativeWebSocket;
using PassthroughCameraSamples;

public class WebcamSender : MonoBehaviour
{
    [SerializeField] private RawImage m_image;
    [SerializeField] private WebCamTextureManager m_webCamTextureManager; // Your existing manager

    private WebSocket websocket;

    private void Start()
    {
        StartCoroutine(InitWebcamAndSocket());
    }

    private IEnumerator InitWebcamAndSocket()
    {
        // Wait for webcam texture to be ready
        while (m_webCamTextureManager.WebCamTexture == null || !m_webCamTextureManager.WebCamTexture.isPlaying)
        {
            yield return null;
        }

        m_image.texture = m_webCamTextureManager.WebCamTexture;

        // Connect to WebSocket server
        websocket = new WebSocket("ws://192.168.0.89:8765"); // Replace <PC-IP>

        websocket.OnMessage += (bytes,a, b) =>
        {
            string msg = Encoding.UTF8.GetString(bytes);
            Debug.Log("Server says: " + msg);
        };

        websocket.Connect();

        // Start sending frames
        InvokeRepeating("SendFrame", 1f, 0.1f); // every 100ms (10 FPS)
    }

    private async void SendFrame()
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
            return;

        WebCamTexture camTexture = m_webCamTextureManager.WebCamTexture;

        // Create Texture2D from WebCamTexture
        Texture2D tex = new Texture2D(camTexture.width, camTexture.height, TextureFormat.RGB24, false);
        tex.SetPixels(camTexture.GetPixels());
        tex.Apply();

        byte[] jpg = tex.EncodeToJPG(50); // Adjust compression quality here (0–100)
        UnityEngine.Object.Destroy(tex); // Clean up

        string base64 = Convert.ToBase64String(jpg);
        await websocket.SendText("RGB:" + base64);
    }

    private async void OnApplicationQuit()
    {
        if (websocket != null)
        {
            await websocket.Close();
        }
    }
}
