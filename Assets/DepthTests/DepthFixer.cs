using System;
using Meta.XR.EnvironmentDepth;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using Unity.XR.CoreUtils; // for XROrigin

public class EnvironmentDepthPipeline : MonoBehaviour
{
    public EnvironmentDepthManager _DepthManager;
    public RawImage rawDepthDebug;
    [SerializeField] public Transform CustomTrackingSpace;

    private void Start()
    {
        _DepthManager.enabled = true;
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
            rawDepthDebug.texture = rawTexture;
        }
    }
}