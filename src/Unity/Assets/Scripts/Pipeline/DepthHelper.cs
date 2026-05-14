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
    
    public NativeArray<ushort>? CaptureAndSendDepth()
    {
        if (!_depthManager.IsDepthAvailable) return null;

        var depthTex = Shader.GetGlobalTexture("_EnvironmentDepthTexture");
        if (depthTex == null) return null;

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
            return null;
        }

        return readback.GetData<ushort>();
    }

    private void OnDestroy()
    {
        if (_depthBufferHandle.IsAllocated) _depthBufferHandle.Free();
    }
}