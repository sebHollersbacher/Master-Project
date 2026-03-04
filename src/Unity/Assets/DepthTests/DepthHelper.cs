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
 *   - DepthProviderOpenXR
 *   - EnvironmentDepthUtils
 *
 * Description of modifications:
 *   - Added Timestamp
 */

#nullable enable

using System;
using System.Collections.Generic;
using Meta.XR.EnvironmentDepth;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Assertions;
using UnityEngine.XR;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.API;
using Object = UnityEngine.Object;

public class DepthHelper
{
    private readonly XRDisplaySubsystem? _displaySubsystem;
    private readonly XROcclusionSubsystem? _occlusionSubsystem;
    private Dictionary<IntPtr, (uint textureId, RenderTexture? renderTexture)>? _depthTextures;
    private IntPtr? _prevNativeTexture;

    ComputeShader cs = Resources.Load<ComputeShader>("LinearizeDepth");
    // Material linearizeDepthMat;
    // RenderTexture _linearDepthRT;

    public DepthHelper()
    {
        // Shader s = Resources.Load<Shader>("LinearizeXRDepth");
        // linearizeDepthMat = new Material(s);
        // linearizeDepthMat.SetFloat("_DebugRaw", 1.0f);

        var loader = XRGeneralSettings.Instance.Manager.activeLoader;
        if (loader is not OpenXRLoader)
        {
            Debug.LogError("[DEBUG] XRDisplaySubsystem not found.");
            return;
        }

        _displaySubsystem = loader.GetLoadedSubsystem<XRDisplaySubsystem>();
        _occlusionSubsystem = loader.GetLoadedSubsystem<XROcclusionSubsystem>();

        if (_occlusionSubsystem == null)
        {
            Debug.LogError("[DEBUG] XROcclusionSubsystem not found. Enable Meta Quest: Occlusion in Project Settings.");
        }
    }

    public bool IsSupported => _displaySubsystem != null && _occlusionSubsystem != null;

    public void SetDepthEnabled(bool isEnabled)
    {
        Assert.IsNotNull(_occlusionSubsystem);
        if (isEnabled)
        {
            _occlusionSubsystem.Start();
        }
        else
        {
            _occlusionSubsystem.Stop();

            if (_depthTextures != null)
            {
                foreach (var depthTextureData in _depthTextures)
                {
                    RenderTexture? renderTexture = depthTextureData.Value.renderTexture;
                    if (renderTexture != null)
                    {
                        Object.Destroy(renderTexture);
                    }
                }

                _depthTextures = null;
            }
        }
    }

    public bool TryGetUpdatedDepthTexture(out RenderTexture? depthTexture,
        DepthManager.DepthFrameDesc[] frameDescriptors)
    {
        depthTexture = null;
        if (_depthTextures == null)
        {
            if (!_occlusionSubsystem.TryGetSwapchainTextureDescriptors(out var swapchainDescriptors))
            {
                Debug.LogError("TryGetSwapchainTextureDescriptors() failed.");
                return false;
            }

            var depthTextures = new Dictionary<IntPtr, (uint, RenderTexture)>(swapchainDescriptors.Length);
            foreach (var descriptors in swapchainDescriptors)
            {
                Assert.AreEqual(1, descriptors.Length, nameof(descriptors));
                var descriptor = descriptors[0];
                Assert.AreNotEqual(IntPtr.Zero, descriptor.nativeTexture);
                if (!UnityXRDisplay.CreateTexture(ToUnityXRRenderTextureDesc(descriptor), out var textureId))
                {
                    Debug.LogError("UnityXRDisplay.CreateTexture() failed.");
                    return false;
                }

                depthTextures.Add(descriptor.nativeTexture, (textureId, null));
            }

            _depthTextures = depthTextures;
        }

        if (!_occlusionSubsystem.running ||
            !_displaySubsystem.running ||
            !_occlusionSubsystem.TryGetFrame(Allocator.Temp, out var frame) ||
            !frame.TryGetTimestamp(out var timeStamp) ||
            !frame.TryGetFovs(out var fovs) ||
            !frame.TryGetPoses(out var poses) ||
            !frame.TryGetNearFarPlanes(out var nearFarPlanes))
        {
            return false;
        }

        var textureDescriptors = _occlusionSubsystem.GetTextureDescriptors(Allocator.Temp);

        Debug.Log($"[DEBUG] format {textureDescriptors[0].format}");
        Debug.Log($"[DEBUG] {textureDescriptors[0].dimension}");
        Debug.Log($"[DEBUG] {textureDescriptors[0].nativeTexture}");
        Debug.Log($"[DEBUG] {textureDescriptors[0].textureType}");

        Assert.AreEqual(1, textureDescriptors.Length, nameof(textureDescriptors));
        var nativeTexture = textureDescriptors[0].nativeTexture;
        Assert.AreNotEqual(IntPtr.Zero, nativeTexture);
        if (_prevNativeTexture == nativeTexture)
        {
            return false;
        }

        _prevNativeTexture = nativeTexture;

        // if (!_depthTextures.TryGetValue(nativeTexture, out var depthTextureData))
        // {
        //     Debug.LogError(
        //         $"Unknown native texture received from MetaOpenXROcclusionSubsystem.GetTextureDescriptors(): {nativeTexture}.");
        //     return false;
        // }
        //
        // var rawTexture = depthTextureData.renderTexture;
        // if (rawTexture == null)
        // {
        //     Debug.Log("[DEBUG] rawTexture is null.");
        //     Assert.IsNotNull(_displaySubsystem, nameof(_displaySubsystem));
        //     depthTexture = _displaySubsystem.GetRenderTexture(depthTextureData.textureId);
        //     if (depthTexture == null)
        //     {
        //         // Can fail if MetaOpenXROcclusionSubsystem is started/stopped quickly.
        //         Debug.Log("XRDisplaySubsystem.GetRenderTexture() failed.");
        //         return false;
        //     }
        //     
        //     _depthTextures[nativeTexture] = (depthTextureData.textureId, depthTexture);
        // }
        // else
        // {
        var rawTexture = Texture2D.CreateExternalTexture(textureDescriptors[0].width, textureDescriptors[0].height,
            TextureFormat.R16,
            false, false,
            textureDescriptors[0].nativeTexture);

        int kernel = cs.FindKernel("LinearizeDepth");

        RenderTexture linearRT = new RenderTexture(rawTexture.width, rawTexture.height, 0, RenderTextureFormat.RFloat);
        linearRT.enableRandomWrite = true;
        linearRT.Create();

        cs.SetTexture(kernel, "_DepthTex", rawTexture);
        cs.SetTexture(kernel, "_LinearDepth", linearRT);
        // EnvironmentDepthManager

        cs.SetFloat("_MinDepth", 0.0f); // in meters
        cs.SetFloat("_MaxDepth", 8.0f); // example max range

        // Debug.Log($"[DEBUG] {rawTexture.format}");
        uint x, y, z;
        cs.GetKernelThreadGroupSizes(kernel, out x, out y, out z);
        cs.Dispatch(kernel, rawTexture.width / (int)x, rawTexture.height / (int)y, 1);

        depthTexture = linearRT;
        // }

        for (int i = 0; i < frameDescriptors.Length; i++)
        {
            frameDescriptors[i] = new DepthManager.DepthFrameDesc()
            {
                timestampNs = timeStamp,
                createPoseLocation = poses[i].position,
                createPoseRotation = poses[i].rotation,
                fovLeftAngleTangent = Mathf.Tan(Mathf.Abs(fovs[i].angleLeft)),
                fovRightAngleTangent = Mathf.Tan(Mathf.Abs(fovs[i].angleRight)),
                fovTopAngleTangent = Mathf.Tan(Mathf.Abs(fovs[i].angleUp)),
                fovDownAngleTangent = Mathf.Tan(Mathf.Abs(fovs[i].angleDown)),
                nearZ = nearFarPlanes.nearZ,
                farZ = nearFarPlanes.farZ
            };
        }

        return true;
    }

    private static UnityXRRenderTextureDesc ToUnityXRRenderTextureDesc(XRTextureDescriptor descriptor)
    {
        Assert.AreEqual(XRTextureType.DepthRenderTexture, descriptor.textureType);
        return new UnityXRRenderTextureDesc
        {
            shadingRateFormat = UnityXRShadingRateFormat.kUnityXRShadingRateFormatNone,
            shadingRate = new UnityXRTextureData(),
            width = (uint)descriptor.width,
            height = (uint)descriptor.height,
            textureArrayLength = (uint)descriptor.depth,
            flags = 0,
            colorFormat = UnityXRRenderTextureFormat.kUnityXRRenderTextureFormatNone,
            depthFormat = ToUnityXRDepthTextureFormat(descriptor.format),
            depth = new UnityXRTextureData { nativePtr = descriptor.nativeTexture }
        };
    }

    private static UnityXRDepthTextureFormat ToUnityXRDepthTextureFormat(TextureFormat textureFormat)
    {
        switch (textureFormat)
        {
            case TextureFormat.RFloat:
                return UnityXRDepthTextureFormat.kUnityXRDepthTextureFormat24bitOrGreater;
            case TextureFormat.R16:
            case TextureFormat.RHalf:
                return UnityXRDepthTextureFormat.kUnityXRDepthTextureFormat16bit;
            default:
                throw new NotSupportedException(
                    $"Attempted to convert unsupported TextureFormat {textureFormat} to UnityXRDepthTextureFormat");
        }
    }

    public class DepthUtils
    {
        private static readonly Vector3 _scalingVector3 = new(1, 1, -1);

        public static Vector4 ComputeNdcToLinearDepthParameters(float near, float far)
        {
            float invDepthFactor;
            float depthOffset;
            if (far < near || float.IsInfinity(far))
            {
                // Inf far plane:
                invDepthFactor = -2.0f * near;
                depthOffset = -1.0f;
            }
            else
            {
                // Finite far plane:
                invDepthFactor = -2.0f * far * near / (far - near);
                depthOffset = -(far + near) / (far - near);
            }

            return new Vector4(invDepthFactor, depthOffset, 0, 0);
        }

        public static Matrix4x4 CalculateReprojection(DepthManager.DepthFrameDesc frameDesc)
        {
            CalculateDepthCameraMatrices(frameDesc, out var proj, out var view);
            return proj * view;
        }

        private static void CalculateDepthCameraMatrices(DepthManager.DepthFrameDesc frameDesc,
            out Matrix4x4 projMatrix, out Matrix4x4 viewMatrix)
        {
            float left = frameDesc.fovLeftAngleTangent;
            float right = frameDesc.fovRightAngleTangent;
            float bottom = frameDesc.fovDownAngleTangent;
            float top = frameDesc.fovTopAngleTangent;
            float near = frameDesc.nearZ;
            float far = frameDesc.farZ;

            float x = 2.0F / (right + left);
            float y = 2.0F / (top + bottom);
            float a = (right - left) / (right + left);
            float b = (top - bottom) / (top + bottom);
            float c;
            float d;
            if (float.IsInfinity(far))
            {
                c = -1.0F;
                d = -2.0f * near;
            }
            else
            {
                c = -(far + near) / (far - near);
                d = -(2.0F * far * near) / (far - near);
            }

            float e = -1.0F;
            projMatrix = new Matrix4x4
            {
                m00 = x,
                m01 = 0,
                m02 = a,
                m03 = 0,
                m10 = 0,
                m11 = y,
                m12 = b,
                m13 = 0,
                m20 = 0,
                m21 = 0,
                m22 = c,
                m23 = d,
                m30 = 0,
                m31 = 0,
                m32 = e,
                m33 = 0
            };

            viewMatrix = Matrix4x4.TRS(frameDesc.createPoseLocation, frameDesc.createPoseRotation, _scalingVector3)
                .inverse;
        }
    }
}