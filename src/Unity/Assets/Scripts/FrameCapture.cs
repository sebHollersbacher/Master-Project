using System;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

public class FrameCapture
{
    private readonly string sessionPath;
    private int frameIndex = 0;
    
    public string SessionPath => sessionPath;
    public int FrameCount => frameIndex;
    
    public FrameCapture()
    {
        string sessionName = $"session_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        sessionPath = Path.Combine(Application.persistentDataPath, "dataset", sessionName);
        Directory.CreateDirectory(sessionPath);
        
        var metadata = new SessionMetadata {
            rgb_detector_size = 480,
            rgb_tracker_size = 320,
            depth_size = 320,
            depth_format = "uint16_raw_little_endian",
            depth_scale_mm = 1.0f
        };
        File.WriteAllText(
            Path.Combine(sessionPath, "metadata.json"),
            JsonUtility.ToJson(metadata, true));
        
        Debug.Log($"[FrameCapture] Saving to {sessionPath}");
    }

    public void CaptureFrame(RenderTexture rgb480, NativeArray<Color32> rgbTracker, NativeArray<ushort>? depthTracker)
    {
        int index = frameIndex++;
        
        SaveRgbPng(rgbTracker, 320, 320,
            Path.Combine(sessionPath, $"frame_{index:D5}_trk.png"));
        if(depthTracker != null)
            SaveDepthRaw(depthTracker.Value,
                Path.Combine(sessionPath, $"frame_{index:D5}.depth"));
        
        if (rgb480 != null)
        {
            AsyncGPUReadback.Request(rgb480, 0, req => OnRgb480Readback(req, index));
        }
    }
    
    private void SaveRgbPng(NativeArray<Color32> image, int w, int h, string path)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.LoadRawTextureData(image);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
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
    
    private void OnRgb480Readback(AsyncGPUReadbackRequest request, int index)
    {
        if (request.hasError)
        {
            Debug.LogError($"[FrameCapture] RGB480 readback failed for frame {index}");
            return;
        }
        
        var data = request.GetData<Color32>();
        string path = Path.Combine(sessionPath, $"frame_{index:D5}_det.png");
        
        var tex = new Texture2D(480, 480, TextureFormat.RGBA32, false);
        tex.LoadRawTextureData(data);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
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