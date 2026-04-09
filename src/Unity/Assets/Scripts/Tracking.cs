using UnityEngine;
using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine.Networking;

public class Tracking
{
    private const string pluginName = "m3t";

    [DllImport(pluginName)]
    private static extern void InitTracker();

    [DllImport(pluginName)]
    private static extern void PassRGBCameraFrame(IntPtr data, int w, int h);

    [DllImport(pluginName)]
    private static extern void PassDepthCameraFrame(IntPtr data, int w, int h);

    [DllImport(pluginName)]
    private static extern void AddObjectToTracker(Constants.TargetModel target, string bodyMetaPath,
        string regionModelPath, string depthModelPath);

    [DllImport(pluginName)]
    private static extern void PassNewPose(Constants.TargetModel target, ref Matrix4x4 newPose);

    [DllImport(pluginName)]
    private static extern void GetBodyPose(Constants.TargetModel target, float[] outMatrix);

    // New Direct Headless Call
    [DllImport(pluginName)]
    private static extern void UpdateTrackerHeadless();

    [DllImport(pluginName)]
    private static extern bool SetupTrackerHeadless();

    private GCHandle _bufferHandleRGB;
    private byte[] _managedBufferRGB;
    private GCHandle _bufferHandleDepth;
    private byte[] _managedBufferDepth;
    private bool isInitialized;

    public async Task Init() // Changed to Task so Core can await it
    {
        string path = Application.persistentDataPath;
        await CopyFilesAsync(path);

        InitTracker();
        AddObjectToTracker(Constants.TargetModel.Pikachu,
            Path.Combine(path, "pikachu_yaml.yaml"), 
            Path.Combine(path, "pikachu_region_model.bin"),
            Path.Combine(path, "pikachu_depth_model.bin"));
        AddObjectToTracker(Constants.TargetModel.Racket, 
            Path.Combine(path, "racket_yaml.yaml"), 
            Path.Combine(path, "racket_region_model.bin"),
            Path.Combine(path, "racket_depth_model.bin"));
        AddObjectToTracker(Constants.TargetModel.Pen,
            Path.Combine(path, "pen_yaml.yaml"), 
            Path.Combine(path, "pen_region_model.bin"),
            Path.Combine(path, "pen_depth_model.bin"));

        if (SetupTrackerHeadless())
        {
            isInitialized = true;
            Debug.Log("M3T Headless Context Ready.");
        }
        else
        {
            Debug.LogError("M3T Setup Failed. Check EGL initialization.");
        }
    }

    private async Task CopyFilesAsync(string path)
    {
        string[] files =
        {
            "pen_yaml.yaml", "pen.obj", "pen_region_model.bin", "pen_depth_model.bin",
            "pikachu_yaml.yaml", "pikachu.obj", "pikachu_region_model.bin", "pikachu_depth_model.bin",
            "racket_yaml.yaml", "racket.obj", "racket_region_model.bin", "racket_depth_model.bin"
        };

        foreach (var f in files)
        {
            string destPath = Path.Combine(path, f);
            string sourcePath = Path.Combine(Application.streamingAssetsPath, "M3T_Files", f);
            using (UnityWebRequest www = UnityWebRequest.Get(sourcePath))
            {
                var operation = www.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (www.result == UnityWebRequest.Result.Success)
                    await File.WriteAllBytesAsync(destPath, www.downloadHandler.data);
            }
        }
    }

    public void UpdateTrackerRGBImage(NativeArray<Color32> image)
    {
        if (!isInitialized || !image.IsCreated || image.Length == 0) return;
        int byteCount = image.Length * 4;

        // Check buffer resizing
        if (_managedBufferRGB == null || _managedBufferRGB.Length != byteCount)
        {
            if (_bufferHandleRGB.IsAllocated) _bufferHandleRGB.Free();
            _managedBufferRGB = new byte[byteCount];
            _bufferHandleRGB = GCHandle.Alloc(_managedBufferRGB, GCHandleType.Pinned);
        }

        // Copy data
        NativeArray<byte> byteView = image.Reinterpret<byte>(4);
        byteView.CopyTo(_managedBufferRGB);

        // Send to C++
        PassRGBCameraFrame(_bufferHandleRGB.AddrOfPinnedObject(), 320, 320);
    }

    public void UpdateTrackerDepthImage(NativeArray<ushort> image)
    {
        if (!isInitialized || !image.IsCreated || image.Length == 0) return;
        int byteCount = image.Length * sizeof(ushort);

        // Check buffer resizing
        if (_managedBufferDepth == null || _managedBufferDepth.Length != byteCount)
        {
            if (_bufferHandleDepth.IsAllocated) _bufferHandleDepth.Free();
            _managedBufferDepth = new byte[byteCount];
            _bufferHandleDepth = GCHandle.Alloc(_managedBufferDepth, GCHandleType.Pinned);
        }

        // Copy data
        NativeArray<byte> byteView = image.Reinterpret<byte>(sizeof(ushort));
        byteView.CopyTo(_managedBufferDepth);
        
        PassDepthCameraFrame(_bufferHandleDepth.AddrOfPinnedObject(), 320, 320);
    }

    public void UpdateTrackerDetection(Constants.TargetModel target, Matrix4x4 newDetection)
    {
        PassNewPose(target, ref newDetection);
    }

    public void UpdateTracker()
    {
        if (!isInitialized) return;
        UpdateTrackerHeadless();
    }

    public Matrix4x4 GetPose(Constants.TargetModel target)
    {
        float[] poseArray = new float[16];
        GetBodyPose(target, poseArray);

        Matrix4x4 m3tMatrix = Matrix4x4.identity;
        for (int i = 0; i < 16; i++)
        {
            m3tMatrix[i] = poseArray[i];
        }

        return m3tMatrix;
    }

    public void OnDestroy()
    {
        if (_bufferHandleRGB.IsAllocated) _bufferHandleRGB.Free();
    }
}