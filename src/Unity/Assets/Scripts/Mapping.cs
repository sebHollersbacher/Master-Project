using System;
using UnityEngine;

public class Mapping : MonoBehaviour
{
    [SerializeField] private Transform pikachuTransform;
    [SerializeField] private Transform racketTransform;
    [SerializeField] private Transform penTransform;
    
    [SerializeField] private Transform refTransform;

    public Pose lensOffset { set; get; }

    // public void UpdateTrackingPose(Transform transform, Matrix4x4 pose)
    // {
    //     // 1. POSITION
    //     // Flip Y because OpenCV Y is Down, Unity Y is Up
    //     Vector3 position = new Vector3(pose.m03, -pose.m13, pose.m23);
    //
    //     // 2. ROTATION (The Vector Method)
    //     // We extract the Forward (Z) and Up (Y) vectors from the matrix columns.
    //     // Col 1 is Up, Col 2 is Forward.
    //
    //     // In M3T, +Y is Down. In Unity, we want that vector to map to Down (-Y).
    //     // So we flip the Y component of the vector itself to convert the space.
    //     Vector3 forward = new Vector3(pose.m02, -pose.m12, pose.m22);
    //     Vector3 up = new Vector3(pose.m01, -pose.m11, pose.m21);
    //
    //     // Now simply look in that direction. 
    //     // This handles all the quaternion math for you.
    //     Quaternion rotation = Quaternion.LookRotation(forward, up);
    //
    //     // 3. APPLY
    //     transform.position = _ref.TransformPoint(position);
    //     transform.rotation = _ref.rotation * rotation;
    // }
    // public void UpdateTrackingPose(Transform transform, Matrix4x4 pose)
    // {
    //     // 1. RAW POSITION (OpenCV to Unity Space)
    //     Vector3 rawPosition = new Vector3(pose.m03, -pose.m13, pose.m23);
    //
    //     // 2. RAW ROTATION (OpenCV to Unity Space)
    //     Vector3 forward = new Vector3(pose.m02, -pose.m12, pose.m22);
    //     Vector3 up = new Vector3(pose.m01, -pose.m11, pose.m21);
    //     Quaternion rawRotation = Quaternion.LookRotation(forward, up);
    //
    //     // 3. APPLY LENS OFFSET
    //     // We treat 'rawPosition' and 'rawRotation' as the origin.
    //     // We move/rotate by _lensOffset relative to that origin.
    //     Vector3 offsetPosition = rawPosition + (rawRotation * _lensOffset.position);
    //     Quaternion offsetRotation = rawRotation * _lensOffset.rotation;
    //
    //     // 4. APPLY GLOBAL REFERENCE
    //     // Finally, bring the offset pose into the world relative to _ref
    //     transform.position = _ref.TransformPoint(offsetPosition);
    //     transform.rotation = _ref.rotation * offsetRotation;
    // }
    
    public void UpdateTrackingPose(Detection.TargetModel target, Matrix4x4 pose)
    {
        // 1. Convert Raw OpenCV Matrix to Unity Local Space
        Vector3 rawPos = new Vector3(pose.m03, -pose.m13, pose.m23);
        Vector3 forward = new Vector3(pose.m02, -pose.m12, pose.m22);
        Vector3 up = new Vector3(pose.m01, -pose.m11, pose.m21);
        Quaternion rawRot = Quaternion.LookRotation(forward, up);

        // 2. Prepare the Inverse Offset
        // If the offset is "how far the lens is from the tracker", 
        // we invert it to move from the tracker back to the lens.
        Quaternion invOffsetRot = Quaternion.Inverse(lensOffset.rotation);
        // We rotate the negative position by the inverse rotation
        Vector3 invOffsetPos = invOffsetRot * -lensOffset.position;

        // 3. Apply Offset in Local Space
        // New Position = TrackerPos + (TrackerRot * InvertedOffsetPos)
        Vector3 correctedPos = rawPos + (rawRot * invOffsetPos);
        Quaternion correctedRot = rawRot * invOffsetRot;

        // 4. Final World Application
        var targetTransform = target switch
        {
            Detection.TargetModel.Racket => racketTransform,
            Detection.TargetModel.Pikachu => pikachuTransform,
            Detection.TargetModel.Pen => penTransform
        };
        
        targetTransform.position = refTransform.TransformPoint(correctedPos);
        targetTransform.rotation = refTransform.rotation * correctedRot;
    }
}