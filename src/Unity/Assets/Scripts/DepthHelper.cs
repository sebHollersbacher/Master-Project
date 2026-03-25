using System.Runtime.InteropServices;
using Meta.XR.EnvironmentDepth;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class DepthHelper : MonoBehaviour
{
    public ComputeShader depthComputeShader;

    private RenderTexture _readableDepthTexture;
    private EnvironmentDepthManager _depthManager;
    private Tracking _trackingScript;
    private byte[] _managedDepthBuffer;
    private GCHandle _depthBufferHandle;
    private int _kernelIndex;

    private void Start()
    {
        _depthManager = GetComponent<EnvironmentDepthManager>();
        _depthManager.enabled = true;

        _readableDepthTexture = new RenderTexture(320, 320, 0, GraphicsFormat.R16_UInt);
        _readableDepthTexture.enableRandomWrite = true;
        _readableDepthTexture.Create();

        _kernelIndex = depthComputeShader.FindKernel("CSMain");
    }

    public void setTrackingScript(Tracking trackingScript)
    {
        _trackingScript = trackingScript;
    }
    
    public void CaptureAndSendDepth()
    {
        if (!_depthManager.IsDepthAvailable) return;

        var depthTex = Shader.GetGlobalTexture("_EnvironmentDepthTexture");
        if (depthTex == null) return;

        depthComputeShader.SetTexture(_kernelIndex, "InputTexture", depthTex);
        depthComputeShader.SetTexture(_kernelIndex, "OutputTexture", _readableDepthTexture);
        
        // pass Z-buffer parameters for the linear math
        Vector4 zParams = Shader.GetGlobalVector("_EnvironmentDepthZBufferParams");
        depthComputeShader.SetVector("ZBufferParams", zParams);
        depthComputeShader.Dispatch(_kernelIndex, 320 / 8, 320 / 8, 1);

        // synchronous readback to make sure latest depth image is used
        var readback = AsyncGPUReadback.Request(_readableDepthTexture, 0);
        readback.WaitForCompletion();

        if (readback.hasError)
        {
            Debug.LogError("[DepthHelper] Synchronous readback failed");
            return;
        }

        NativeArray<ushort> data = readback.GetData<ushort>();
        _trackingScript?.UpdateTrackerDepthImage(data);
    }

    private void OnDestroy()
    {
        if (_depthBufferHandle.IsAllocated) _depthBufferHandle.Free();
    }
}