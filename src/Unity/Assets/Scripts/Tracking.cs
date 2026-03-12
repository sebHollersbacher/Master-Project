using UnityEngine;
using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Meta.XR;
using Unity.Collections;
using UnityEngine.Networking;

public class Tracking
{
    private const string pluginName = "m3t";

    [DllImport(pluginName)]
    private static extern void InitTracker();
    
    [DllImport(pluginName)]
    private static extern void PassCameraFrame(IntPtr data, int w, int h);
    
    [DllImport(pluginName)]
    private static extern void AddObjectToTracker(Constants.TargetModel target, string bodyMetaPath, string modelBinPath);

    [DllImport(pluginName)]
    private static extern void PassNewPose(Constants.TargetModel target, ref Matrix4x4 newPose);

    [DllImport(pluginName)]
    private static extern void GetBodyPose(Constants.TargetModel target, float[] outMatrix);

    [DllImport(pluginName)]
    private static extern IntPtr GetRenderEventFunc();

    

    private RenderTexture _resizeRT;
    private GCHandle _bufferHandle;
    private byte[] _managedBuffer;
    private bool isInitialized;

    public async void Init()
    {
        _resizeRT = new RenderTexture(Constants.Width, Constants.Height, 0, RenderTextureFormat.ARGB32);
        _resizeRT.Create();

        string path = Application.persistentDataPath;
        await CopyFilesAsync(path);
        InitTracker();
        AddObjectToTracker(Constants.TargetModel.Pikachu, Path.Combine(path, "pikachu_yaml.yaml"), Path.Combine(path, "pikachu_model.bin"));
        AddObjectToTracker(Constants.TargetModel.Racket, Path.Combine(path, "racket_yaml.yaml"), Path.Combine(path, "racket_model.bin"));
        AddObjectToTracker(Constants.TargetModel.Pen, Path.Combine(path, "pen_yaml.yaml"), Path.Combine(path, "pen_model.bin"));
        GL.IssuePluginEvent(GetRenderEventFunc(), 1);
        Debug.Log("M3T Initialized via Script");

        isInitialized = true;
    }

    private async Task CopyFilesAsync(string path)
    {
        string[] files = { "pen_yaml.yaml", "pen.obj", "pen_model.bin", "pikachu_yaml.yaml", "pikachu.obj", "pikachu_model.bin", "racket_yaml.yaml", "racket.obj", "racket_model.bin" };

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
                {
                    await File.WriteAllBytesAsync(destPath, www.downloadHandler.data);
                    Debug.Log($"[DEBUG] Copied: {f}");
                }
                else
                {
                    Debug.LogError($"[DEBUG] Failed to copy {f}: {www.error}");
                }
            }
        }
    }

    public void UpdateTrackerImage(NativeArray<Color32> image)
    {
        if (!isInitialized || !image.IsCreated || image.Length == 0) return;
        int byteCount = image.Length * 4;
        
        // Check buffer resizing
        if (_managedBuffer == null || _managedBuffer.Length != byteCount)
        {
            if (_bufferHandle.IsAllocated) _bufferHandle.Free();
            _managedBuffer = new byte[byteCount];
            _bufferHandle = GCHandle.Alloc(_managedBuffer, GCHandleType.Pinned);
        }

        // Copy data
        NativeArray<byte> byteView = image.Reinterpret<byte>(16);
        byteView.CopyTo(_managedBuffer);

        // Send to C++
        PassCameraFrame(_bufferHandle.AddrOfPinnedObject(), 320, 320);
    }
    
    public void UpdateTrackerDetection(Constants.TargetModel target, Matrix4x4 newDetection)
    {
        PassNewPose(target, ref newDetection);
    }

    public void UpdateTracker()
    {
        GL.IssuePluginEvent(GetRenderEventFunc(), 2);
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
        if (_resizeRT != null) _resizeRT.Release();
    }
}