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
    private static extern void InitTracker(string a, string b);

    [DllImport(pluginName)]
    private static extern IntPtr GetRenderEventFunc();

    [DllImport(pluginName)]
    private static extern void PassCameraFrame(IntPtr data, int w, int h);
    
    [DllImport(pluginName)]
    private static extern void PassNewPose(ref Matrix4x4 newPose);

    [DllImport(pluginName)]
    private static extern void GetBodyPose(float[] outMatrix);

    private RenderTexture _resizeRT;
    private GCHandle _bufferHandle;
    private byte[] _managedBuffer;
    private float[] poseArray = new float[16];
    private bool isInitialized;

    public async void Init()
    {
        _resizeRT = new RenderTexture(Constants.Width, Constants.Height, 0, RenderTextureFormat.ARGB32);
        _resizeRT.Create();

        string path = Application.persistentDataPath;
        await CopyFilesAsync(path);
        InitTracker(Path.Combine(path, "pikachu_yaml.yaml"), Path.Combine(path, "pikachu_model.bin"));
        GL.IssuePluginEvent(GetRenderEventFunc(), 1);
        Debug.Log("M3T Initialized via Script");

        isInitialized = true;
    }

    private async Task CopyFilesAsync(string path)
    {
        string[] files = { "pikachu_yaml.yaml", "pikachu_obj.obj", "pikachu_model.bin" };

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
        PassCameraFrame(_bufferHandle.AddrOfPinnedObject(), Constants.Width, Constants.Height);
    }
    
    public void UpdateTrackerDetection(Matrix4x4 newDetection)
    {
        PassNewPose(ref newDetection);
    }

    public void UpdateTracker()
    {
        GL.IssuePluginEvent(GetRenderEventFunc(), 2);
    }

    public Matrix4x4 GetPose()
    {
        GetBodyPose(poseArray);

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