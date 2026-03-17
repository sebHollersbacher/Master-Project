/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

/*
 * Modifications and additional code Copyright (c) 2025 Sebastian Hollersbacher
 *
 * This file incorporates and extends functionality from the following
 * Meta.XR.EnvironmentDepth classes:
 *   - EnvironmentDepthManager
 *   - DepthFrameDesc
 *
 * Description of modifications:
 *   - Added Timestamp to Descriptor
 */

using Meta.XR.EnvironmentDepth;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Assertions;
using UnityEngine.UI;

public class DepthManager : MonoBehaviour
{
    private const int numViews = 2;
    private static DepthHelper2 _helper;
    private readonly DepthFrameDesc[] frameDescriptors = new DepthFrameDesc[numViews];
    private bool _hasPermission;
    private readonly Matrix4x4[] _reprojectionMatrices = new Matrix4x4[numViews];

    public RenderTexture depthTexture { get; private set; }
    public static bool IsSupported => _helper.IsSupported;
    public bool IsDepthAvailable { get; private set; }


    private void OnEnable()
    {
        _helper = new();
        Application.onBeforeRender += OnBeforeRender;

        if (!IsSupported)
        {
            Debug.LogError(
                $"Environment Depth is not supported. Please check {nameof(DepthManager)}.{nameof(IsSupported)} before enabling {nameof(DepthManager)}.\n" +
                "Open 'Meta > Tools > Project Setup Tool' to see requirements.\n");
            enabled = false;
            return;
        }

        _hasPermission = Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission);
        if (_hasPermission)
            _helper.SetDepthEnabled(true);
        else
            Debug.unityLogger.Log(LogType.Warning,
                $"Environment Depth requires {OVRPermissionsRequester.ScenePermission} permission. Waiting for permission...");
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= OnBeforeRender;
        ResetDepthTextureIfAvailable();
        if (IsSupported && _hasPermission)
            _helper.SetDepthEnabled(false);
    }

    private void OnBeforeRender()
    {
        if (!_hasPermission)
        {
            if (!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
                return;
            _hasPermission = true;
            _helper.SetDepthEnabled(true);
        }

        TryFetchDepthTexture();
        if (!IsDepthAvailable)
        {
            return;
        }

        // Calculate Environment Depth Camera parameters
        // Assume NearZ and FarZ are the same for left and right eyes
        var leftEyeData = frameDescriptors[0];
        var depthZBufferParams =
            DepthHelper2.DepthUtils.ComputeNdcToLinearDepthParameters(leftEyeData.nearZ, leftEyeData.farZ);

        for (int i = 0; i < numViews; i++)
        {
            _reprojectionMatrices[i] =
                DepthHelper2.DepthUtils.CalculateReprojection(frameDescriptors[i]); // * trackingSpaceWorldToLocal;
        }
    }

    private void TryFetchDepthTexture()
    {
        if (!_helper.TryGetUpdatedDepthTexture(out var outDepthTexture, frameDescriptors))
        {
            return;
        }

        if (outDepthTexture == null) // can be null when the headset is awaking from sleep
        {
            Debug.LogError("[DEBUG] texture null");
            ResetDepthTextureIfAvailable();
            return;
        }

        Assert.IsTrue(outDepthTexture.IsCreated(), "depthTexture.IsCreated()");

        depthTexture = outDepthTexture;
        if (!IsDepthAvailable)
        {
            IsDepthAvailable = true;
        }
    }

    private void ResetDepthTextureIfAvailable()
    {
        if (IsDepthAvailable)
        {
            IsDepthAvailable = false;
        }
    }

    public struct DepthFrameDesc
    {
        public long timestampNs;
        public Vector3 createPoseLocation;
        public Quaternion createPoseRotation;
        public float fovLeftAngleTangent;
        public float fovRightAngleTangent;
        public float fovTopAngleTangent;
        public float fovDownAngleTangent;
        public float nearZ;
        public float farZ;
    }
}