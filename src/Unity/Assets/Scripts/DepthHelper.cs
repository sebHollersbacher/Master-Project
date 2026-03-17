using System.Runtime.InteropServices;
using Meta.XR.EnvironmentDepth;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class DepthHelper : MonoBehaviour
{
    public ComputeShader depthComputeShader;

    private RenderTexture _readableDepthTexture;
    private EnvironmentDepthManager _depthManager;
    private Tracking _trackingScript;
    private bool _isProcessing;
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

    private void Update()
    {
        if (_depthManager.IsDepthAvailable && !_isProcessing)
        {
            var depthTex = Shader.GetGlobalTexture("_EnvironmentDepthTexture");
            if (depthTex != null)
            {
                _isProcessing = true;
                
                depthComputeShader.SetTexture(_kernelIndex, "InputTexture", depthTex);
                depthComputeShader.SetTexture(_kernelIndex, "OutputTexture", _readableDepthTexture);

                // pass Z-buffer parameters for the linear math
                Vector4 zParams = Shader.GetGlobalVector("_EnvironmentDepthZBufferParams");
                depthComputeShader.SetVector("ZBufferParams", zParams);
                depthComputeShader.Dispatch(_kernelIndex, 320 / 8, 320 / 8, 1);

                RequestDepthReadback(_readableDepthTexture);
            }
        }
    }

    private void RequestDepthReadback(Texture tex)
    {
        AsyncGPUReadback.Request(tex, 0, request =>
        {
            if (request.hasError)
            {
                Debug.LogError(
                    "[DepthHelper] AsyncGPUReadback failed! GPU might be overwhelmed or format is incompatible.");
                _isProcessing = false;
                return;
            }

            // read 16-bit data from texture
            var data = request.GetData<ushort>();
            int byteCount = data.Length * sizeof(ushort);
            
            // allocate buffer
            if (_managedDepthBuffer == null || _managedDepthBuffer.Length != byteCount)
            {
                if (_depthBufferHandle.IsAllocated) _depthBufferHandle.Free();
                _managedDepthBuffer = new byte[byteCount];
                _depthBufferHandle = GCHandle.Alloc(_managedDepthBuffer, GCHandleType.Pinned);
            }
            
            // reinterpret to pass the data as a byte array
            var byteView = data.Reinterpret<byte>(sizeof(ushort));
            byteView.CopyTo(_managedDepthBuffer);
            
            _trackingScript?.UpdateTrackerDepthImage(_depthBufferHandle.AddrOfPinnedObject());
            _isProcessing = false;
        });
    }

    private void OnDestroy()
    {
        if (_depthBufferHandle.IsAllocated) _depthBufferHandle.Free();
    }
}