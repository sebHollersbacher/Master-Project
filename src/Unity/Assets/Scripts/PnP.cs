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
        float[] output // 7 floats (tx, ty, tz, rx, ry, rz, success)
    );

    [DllImport("opencv_wrapper")]
    private static extern void FitLine2D_Entry(
        float[] p2D, // Array of x,y (size = count * 2)
        int count, // Number of points
        float[] outputArr //  4 floats (vx, vy, x0, y0)
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

        float[] result = new float[7];
        SolvePnP_Ransac_Entry(flat3D, count, flat2D, camMatFlat, result);

        if (result[6] > 0.5f) // Success flag
        {
            return ParseResult(result);
        }

        Debug.Log("[PnP] PnP Unsuccessful");
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

    public Matrix4x4? GetPenPoseMatrix(Rect boundingBox, List<Vector2> keypoints)
    {
        if (keypoints == null || keypoints.Count < 2) return null;
 
        var modelPoints = Constants.PenPoints;
 
        // 1. Fit a robust line through all keypoints
        float[] p2D = new float[keypoints.Count * 2];
        for (int i = 0; i < keypoints.Count; i++)
        {
            p2D[i * 2] = keypoints[i].x;
            p2D[i * 2 + 1] = keypoints[i].y;
        }
 
        float[] lineParams = new float[4];
        FitLine2D_Entry(p2D, keypoints.Count, lineParams);
 
        Vector2 V = new Vector2(lineParams[0], lineParams[1]).normalized;
        Vector2 P0 = new Vector2(lineParams[2], lineParams[3]);
 
        // 2. Project each keypoint onto the fitted line to get clean 1D positions
        //    This removes noise perpendicular to the pen axis.
        float[] projT = new float[keypoints.Count]; // signed distance along the line
        for (int i = 0; i < keypoints.Count; i++)
        {
            projT[i] = Vector2.Dot(keypoints[i] - P0, V);
        }
 
        // 3. Estimate Z from all usable keypoint pairs
        //    For each pair (i, j): pixelDist = |projT[i] - projT[j]|
        //    realDist along pen axis = |modelY[i] - modelY[j]|
        //    Z_estimate = fx * realDist / pixelDist
        //    (This gives Z / cos(tilt), but since tilt is the same for all pairs,
        //     the median is a robust estimate of Z / cos(tilt))
        float fx = Constants.CameraMatrixList[0].x;
        float cx = Constants.CameraMatrixList[0].z;
        float fy = Constants.CameraMatrixList[1].y;
        float cy = Constants.CameraMatrixList[1].z;
 
        var depthEstimates = new List<float>();
 
        // Minimum real-world distance to consider a pair (skip pairs too close together,
        // e.g., points 1↔2 are only 2.4mm apart — pixel noise dominates)
        const float minRealDist = 0.01f; // 1cm
 
        int count = Mathf.Min(keypoints.Count, modelPoints.Count);
        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                // Real 3D distance along the pen axis (Y component dominates since X,Z ≈ 0)
                float realDist = Vector3.Distance(modelPoints[i], modelPoints[j]);
                if (realDist < minRealDist) continue;
 
                float pixelDist = Mathf.Abs(projT[i] - projT[j]);
                if (pixelDist < 2.0f) continue; // too small in pixels to be reliable
 
                float zEstimate = (fx * realDist) / pixelDist;
                depthEstimates.Add(zEstimate);
            }
        }
 
        if (depthEstimates.Count == 0) return null;
 
        // 4. Take the median — robust against noisy/occluded keypoints
        depthEstimates.Sort();
        float Z = depthEstimates[depthEstimates.Count / 2];
 
        if (Z < 0.05f || Z > 5.0f) return null; // sanity check
 
        // 5. Find pen extremes using bounding box (as before)
        Vector2[] corners =
        {
            new(boundingBox.xMin, boundingBox.yMin),
            new(boundingBox.xMax, boundingBox.yMin),
            new(boundingBox.xMin, boundingBox.yMax),
            new(boundingBox.xMax, boundingBox.yMax)
        };
 
        float minT = float.MaxValue;
        float maxT = float.MinValue;
 
        foreach (Vector2 corner in corners)
        {
            float t = Vector2.Dot(corner - P0, V);
            if (t < minT) minT = t;
            if (t > maxT) maxT = t;
        }
 
        Vector2 extremeA = P0 + (V * minT);
        Vector2 extremeB = P0 + (V * maxT);
 
        // 6. Determine tip vs base
        Vector2 noisyTip = keypoints[0];
        Vector2 trueTip, trueBase;
 
        if (Vector2.Distance(noisyTip, extremeA) < Vector2.Distance(noisyTip, extremeB))
        {
            trueTip = extremeA;
            trueBase = extremeB;
        }
        else
        {
            trueTip = extremeB;
            trueBase = extremeA;
        }
 
        // 7. Unproject 2D center to 3D position (OpenCV space)
        Vector2 center = (trueTip + trueBase) / 2f;
        float X = (center.x - cx) * Z / fx;
        float Y = (center.y - cy) * Z / fy;
 
        // 8. Construct rotation from 2D line direction
        Vector2 dir = (trueTip - trueBase).normalized;
 
        Vector4 colX = new Vector4(dir.y, -dir.x, 0, 0);
        Vector4 colY = new Vector4(dir.x, dir.y, 0, 0);
        Vector4 colZ = new Vector4(0, 0, 1, 0);
        Vector4 colW = new Vector4(X, Y, Z, 1);
 
        return new Matrix4x4(colX, colY, colZ, colW);
    }
}