using System;
using System.IO;
using System.Threading.Tasks;
using Meta.XR;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class FrameCapture : MonoBehaviour
{
    [SerializeField] private RawImage rgbImage;
    private string sessionPath;
    private int frameIndex = 0;
    private int warmup = 10;
    private int currentwarmup = 0;
    
    private DepthHelper _depthHelper;
    private PassthroughCameraAccess _cameraAccess;
    private RenderTexture downscaledTextureTracking;
    private RenderTexture downscaledTextureDetection;
    
    public string SessionPath => sessionPath;
    public int FrameCount => frameIndex;
    
    
    private void Awake()
    {
        _cameraAccess = GetComponent<PassthroughCameraAccess>();
        _depthHelper = GetComponent<DepthHelper>();

        downscaledTextureTracking = new RenderTexture(320, 320, 0, RenderTextureFormat.ARGB32);
        downscaledTextureTracking.Create();
        downscaledTextureDetection = new RenderTexture(640, 640, 0, RenderTextureFormat.ARGB32);
        downscaledTextureDetection.Create();
    }
    
    private async void Start()
    {
        string sessionName = $"session_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        sessionPath = Path.Combine(Application.persistentDataPath, "dataset", sessionName);
        Directory.CreateDirectory(sessionPath);
        
        var metadata = new SessionMetadata {
            rgb_detector_size = 640,
            rgb_tracker_size = 320,
            depth_size = 320,
            depth_format = "uint16_raw_little_endian",
            depth_scale_mm = 1.0f
        };
        File.WriteAllText(
            Path.Combine(sessionPath, "metadata.json"),
            JsonUtility.ToJson(metadata, true));
        
        Debug.Log($"[FrameCapture] Saving to {sessionPath}");
        
        while (!_cameraAccess.IsPlaying)
        {
            await Task.Yield();
        }
        rgbImage.texture = _cameraAccess.GetTexture();
    }

    private void Update()
    {
        var cameraTexture = _cameraAccess.GetTexture();
        if (cameraTexture == null) return;
        Graphics.Blit(cameraTexture, downscaledTextureDetection);
        Graphics.Blit(cameraTexture, downscaledTextureTracking);

        NativeArray<Color32>
            colorsBuffer = new NativeArray<Color32>(320 * 320, Allocator.Persistent);
        AsyncGPUReadback.RequestIntoNativeArray(ref colorsBuffer, downscaledTextureTracking).WaitForCompletion();
        
        var depthData = _depthHelper.CaptureAndSendDepth();
        CaptureFrame(downscaledTextureDetection, colorsBuffer, depthData);
        
        colorsBuffer.Dispose();
    }

    public void CaptureFrame(RenderTexture rgb640, NativeArray<Color32> rgbTracker, NativeArray<ushort>? depthTracker)
    {
        if (currentwarmup < warmup)
        {
            currentwarmup++;
            return;
        }
        
        int index = frameIndex++;
        
        SaveRgbPng(rgbTracker, 320, 320,
            Path.Combine(sessionPath, $"frame_{index:D5}_trk.png"));
        if(depthTracker != null)
            SaveDepthRaw(depthTracker.Value,
                Path.Combine(sessionPath, $"frame_{index:D5}.depth"));
        
        if (rgb640 != null)
        {
            AsyncGPUReadback.Request(rgb640, 0, req => OnRgb640Readback(req, index));
        }
    }
    
    private void SaveRgbPng(NativeArray<Color32> image, int w, int h, string path)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.LoadRawTextureData(image);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex);
    }
    
    private unsafe void SaveDepthRaw(NativeArray<ushort> depth, string path)
    {
        byte[] bytes = new byte[depth.Length * sizeof(ushort)];
        fixed (byte* dst = bytes)
        {
            UnsafeUtility.MemCpy(
                dst,
                NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(depth),
                bytes.Length);
        }
        File.WriteAllBytes(path, bytes);
    }
    
    private void OnRgb640Readback(AsyncGPUReadbackRequest request, int index)
    {
        if (request.hasError)
        {
            Debug.LogError($"[FrameCapture] RGB640 readback failed for frame {index}");
            return;
        }
        
        var data = request.GetData<Color32>();
        string path = Path.Combine(sessionPath, $"frame_{index:D5}_det.png");
        
        var tex = new Texture2D(640, 640, TextureFormat.RGBA32, false);
        tex.LoadRawTextureData(data);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex);
    }
    
    [Serializable]
    private class SessionMetadata
    {
        public int rgb_detector_size;
        public int rgb_tracker_size;
        public int depth_size;
        public string depth_format;
        public float depth_scale_mm;
    }
}