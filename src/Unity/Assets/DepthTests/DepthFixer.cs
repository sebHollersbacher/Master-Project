using System;
using Meta.XR.EnvironmentDepth;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using Unity.XR.CoreUtils;
using UnityEngine.Experimental.Rendering; // for XROrigin

public class EnvironmentDepthPipeline : MonoBehaviour
{
    private RenderTexture _readableDepthTexture;
    private Material _blitMaterial;
    
    public EnvironmentDepthManager _DepthManager;
    public RawImage rawDepthDebug;
    [SerializeField] public Transform CustomTrackingSpace;

    private void Start()
    {
        _DepthManager.enabled = true;
        _readableDepthTexture = new RenderTexture(320, 320, 0, GraphicsFormat.R16_UNorm);
        _readableDepthTexture.Create();
    }

    void Update()
    {
        if (CustomTrackingSpace != null)
        {
            Debug.Log($"[DEBUG] trackingspace: {CustomTrackingSpace.worldToLocalMatrix}");
        }
        
        if (_DepthManager.IsDepthAvailable)
        {
            var rawTexture = Shader.GetGlobalTexture("_EnvironmentDepthTexture");
        }
    }
}