using System;
using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public class PnP
{
    [DllImport("opencv_wrapper")] // Drop Lib prefix and .so
    private static extern void SolvePnP_Ransac_Entry(
        float[] p3D, // 1D Array of x,y,z
        int count, // Number of points
        float[] p2D, // 1D Array of x,y
        float[] camMatrix, // 9 floats
        float[] output // 9 floats (tx, ty, tz, rx, ry, rz, success, inliers, reprojError)
    );

    public Matrix4x4? Solve(Constants.TargetModel target, Detection.YoloResult result)
    {
        return target switch
        {
            Constants.TargetModel.Pen => SolvePnP(result.keypoints, Constants.PenPoints),
            Constants.TargetModel.Racket => SolvePnP(result.keypoints, Constants.RacketPoints),
            Constants.TargetModel.Pikachu => SolvePnP(result.keypoints, Constants.PikachuPoints),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public Matrix4x4? SolvePnP(List<Vector2> imagePoints, List<Vector3> modelPoints)
    {
        if (imagePoints == null || imagePoints.Count != modelPoints.Count)
        {
            Debug.LogWarning(
                $"Point count mismatch! Model: {modelPoints.Count}, Detected: {(imagePoints?.Count ?? 0)}");
            return null;
        }

        int count = modelPoints.Count;

        // 2. Prepare Data Arrays
        float[] flat3D = new float[count * 3];
        float[] flat2D = new float[count * 2];

        // Fill arrays
        for (int i = 0; i < count; i++)
        {
            // Model (3D)
            flat3D[i * 3 + 0] = modelPoints[i].x;
            flat3D[i * 3 + 1] = modelPoints[i].y;
            flat3D[i * 3 + 2] = modelPoints[i].z;

            // Image (2D)
            flat2D[i * 2 + 0] = imagePoints[i].x;
            flat2D[i * 2 + 1] = imagePoints[i].y;
        }

        var cameraMatrixList = Constants.CameraMatrixList;
        float[] camMatFlat = new float[]
        {
            cameraMatrixList[0].x, cameraMatrixList[0].y, cameraMatrixList[0].z,
            cameraMatrixList[1].x, cameraMatrixList[1].y, cameraMatrixList[1].z,
            cameraMatrixList[2].x, cameraMatrixList[2].y, cameraMatrixList[2].z
        };

        float[] result = new float[9];
        SolvePnP_Ransac_Entry(flat3D, count, flat2D, camMatFlat, result);

        if (result[6] > 0.5f)
        {
            int inlierCount = (int)result[7];
            float meanReprojError = result[8];
    
            // Quality gate — only return a pose if it's trustworthy
            int minInliers = modelPoints.Count >= 12 ? 8 : (modelPoints.Count >= 9 ? 6 : 4);
            minInliers = 5;
            float maxReproj = modelPoints.Count <= 9 ? 6.0f : 4.0f;
            maxReproj = 4.0f;
    
            if (inlierCount >= minInliers && meanReprojError < maxReproj)
                return ParseResult(result);
    
            Debug.Log($"[PnP] Rejected: {inlierCount} inliers, {meanReprojError:F1}px reproj error");
        }
        return null;
    }

    private Matrix4x4 ParseResult(float[] result)
    {
        Vector3 tvec = new Vector3(result[0], result[1], result[2]);
        Vector3 rvec = new Vector3(result[3], result[4], result[5]);

        float angleRad = rvec.magnitude;
        Quaternion rotation = Quaternion.identity;

        if (angleRad > 0.0001f)
        {
            Vector3 axis = rvec.normalized;
            rotation = Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, axis);
        }

        return Matrix4x4.TRS(tvec, rotation, Vector3.one);
    }
}