using UnityEngine;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine.Networking;

public class Tracking
{
    private GCHandle _bufferHandleRGB;
    private byte[] _managedBufferRGB;
    private GCHandle _bufferHandleDepth;
    private byte[] _managedBufferDepth;
    private bool isInitialized;

    public bool IsInitialized => isInitialized;
    
    // Reusable buffer for pose transfer
    private readonly float[] _poseBuffer = new float[16];
    
    private int trackingLostFramesRequired = 45;
    private int consecutiveLostFrames = 0;

    public async Task Init(int nCorrIterations = 4, int nUpdateIterations = 2, 
        Constants.TargetModel target = Constants.TargetModel.Pikachu)
    {
        string path = Application.persistentDataPath;
        await CopyFilesAsync(path);

        M3TNative.InitTracker(nCorrIterations, nUpdateIterations);

        switch (target)
        {
            case Constants.TargetModel.Pikachu:
                var pikachuCfg = M3TNative.PikachuConfig();
                M3TNative.AddObjectToTracker(
                    (int)Constants.TargetModel.Pikachu,
                    Path.Combine(path, "pikachu_yaml.yaml"),
                    Path.Combine(path, "pikachu_region_model.bin"),
                    Path.Combine(path, "pikachu_depth_model.bin"),
                    Path.Combine(path, "pikachu_texture.png"),
                    ref pikachuCfg);
                break;

            case Constants.TargetModel.Racket:
                var racketCfg = M3TNative.RacketConfig();
                M3TNative.AddObjectToTracker(
                    (int)Constants.TargetModel.Racket,
                    Path.Combine(path, "racket_yaml.yaml"),
                    Path.Combine(path, "racket_region_model.bin"),
                    Path.Combine(path, "racket_depth_model.bin"),
                    Path.Combine(path, "racket_texture.png"),
                    ref racketCfg);
                break;

            case Constants.TargetModel.Pen:
                var penCfg = M3TNative.PenConfig();
                M3TNative.AddObjectToTracker(
                    (int)Constants.TargetModel.Pen,
                    Path.Combine(path, "pen_yaml.yaml"),
                    Path.Combine(path, "pen_region_model.bin"),
                    Path.Combine(path, "pen_depth_model.bin"),
                    Path.Combine(path, "pen_texture.png"),
                    ref penCfg);
                break;
        }

        Debug.Log($"M3T Init complete for {target} — waiting for camera params before Setup.");
    }

    public void SetCameraParams(
        float rgbFx, float rgbFy, float rgbCx, float rgbCy, int rgbW, int rgbH,
        Matrix4x4 rgbCamera2World,
        float depthFx, float depthFy, float depthCx, float depthCy, int depthW, int depthH,
        Matrix4x4 depthCamera2World)
    {
        float[] rgbExt = Matrix4x4ToColumnMajor(rgbCamera2World);
        float[] depthExt = Matrix4x4ToColumnMajor(depthCamera2World);

        M3TNative.SetRGBCameraParams(rgbFx, rgbFy, rgbCx, rgbCy, rgbW, rgbH, rgbExt);
        M3TNative.SetDepthCameraParams(depthFx, depthFy, depthCx, depthCy, depthW, depthH, depthExt);

        Debug.Log($"[Tracking] RGB: fx={rgbFx:F4} fy={rgbFy:F4} cx={rgbCx:F4} cy={rgbCy:F4}");
        Debug.Log($"[Tracking] Depth: fx={depthFx:F4} fy={depthFy:F4} cx={depthCx:F4} cy={depthCy:F4}");
    }

    public bool Setup()
    {
        if (M3TNative.SetupTrackerHeadless())
        {
            isInitialized = true;
            Debug.Log("M3T Headless Context Ready.");
            return true;
        }

        Debug.LogError("M3T Setup Failed. Check EGL initialization.");
        return false;
    }

    private static float[] Matrix4x4ToColumnMajor(Matrix4x4 m)
    {
        float[] arr = new float[16];
        for (int i = 0; i < 16; i++)
            arr[i] = m[i];
        return arr;
    }

    private async Task CopyFilesAsync(string path)
    {
        string[] files =
        {
            "pen_yaml.yaml", "pen.obj", "pen_region_model.bin", "pen_depth_model.bin", "pen_texture.png",
            "pikachu_yaml.yaml", "pikachu.obj", "pikachu_region_model.bin", "pikachu_depth_model.bin", "pikachu_texture.png",
            "racket_yaml.yaml", "racket.obj", "racket_region_model.bin", "racket_depth_model.bin", "racket_texture.png"
        };

        foreach (var f in files)
        {
            string destPath = Path.Combine(path, f);
            string sourcePath = Path.Combine(Application.streamingAssetsPath, "M3T_Files", f);
            using (UnityWebRequest www = UnityWebRequest.Get(sourcePath))
            {
                var operation = www.SendWebRequest();
                while (!operation.isDone)
                    await Task.Yield();

                if (www.result == UnityWebRequest.Result.Success)
                    await File.WriteAllBytesAsync(destPath, www.downloadHandler.data);
            }
        }
    }

    public void UpdateTrackerRGBImage(NativeArray<Color32> image)
    {
        if (!isInitialized || !image.IsCreated || image.Length == 0) return;
        int byteCount = image.Length * 4;

        if (_managedBufferRGB == null || _managedBufferRGB.Length != byteCount)
        {
            if (_bufferHandleRGB.IsAllocated) _bufferHandleRGB.Free();
            _managedBufferRGB = new byte[byteCount];
            _bufferHandleRGB = GCHandle.Alloc(_managedBufferRGB, GCHandleType.Pinned);
        }

        NativeArray<byte> byteView = image.Reinterpret<byte>(4);
        byteView.CopyTo(_managedBufferRGB);

        M3TNative.PassRGBCameraFrame(_bufferHandleRGB.AddrOfPinnedObject(), Constants.TrackingRGBResolution, Constants.TrackingRGBResolution);
    }

    public void UpdateTrackerDepthImage(NativeArray<ushort> image)
    {
        if (!isInitialized || !image.IsCreated || image.Length == 0) return;
        int byteCount = image.Length * sizeof(ushort);

        if (_managedBufferDepth == null || _managedBufferDepth.Length != byteCount)
        {
            if (_bufferHandleDepth.IsAllocated) _bufferHandleDepth.Free();
            _managedBufferDepth = new byte[byteCount];
            _bufferHandleDepth = GCHandle.Alloc(_managedBufferDepth, GCHandleType.Pinned);
        }

        NativeArray<byte> byteView = image.Reinterpret<byte>(sizeof(ushort));
        byteView.CopyTo(_managedBufferDepth);

        M3TNative.PassDepthCameraFrame(_bufferHandleDepth.AddrOfPinnedObject(), 320, 320);
    }

    public void UpdateTrackerDetection(Constants.TargetModel target, Matrix4x4 newDetection)
    {
        for (int i = 0; i < 16; i++)
            _poseBuffer[i] = newDetection[i];

        M3TNative.PassNewPose((int)target, _poseBuffer);
    }

    public void UpdateTracker()
    {
        if (!isInitialized) return;
        M3TNative.UpdateTrackerHeadless();
    }
    
    public bool IsTracking(Constants.TargetModel target, int minLines = 60)
    {
        bool currentlyTracking = M3TNative.GetTrackingValidLines((int)target) >= minLines;
    
        if (currentlyTracking)
        {
            consecutiveLostFrames = 0;
            return true;
        }
    
        consecutiveLostFrames++;
        return consecutiveLostFrames < trackingLostFramesRequired;
    }

    public Matrix4x4 GetPose(Constants.TargetModel target)
    {
        M3TNative.GetBodyPose((int)target, _poseBuffer);

        Matrix4x4 m3tMatrix = Matrix4x4.identity;
        for (int i = 0; i < 16; i++)
            m3tMatrix[i] = _poseBuffer[i];

        return m3tMatrix;
    }

    public void OnDestroy()
    {
        if (_bufferHandleRGB.IsAllocated) _bufferHandleRGB.Free();
        if (_bufferHandleDepth.IsAllocated) _bufferHandleDepth.Free();
    }
}