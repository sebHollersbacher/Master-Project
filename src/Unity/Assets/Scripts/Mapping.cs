using System;
using UnityEngine;

public class Mapping : MonoBehaviour
{
    [SerializeField] private Transform pikachuTransform;
    [SerializeField] private Transform racketTransform;
    [SerializeField] private Transform penTransform;
    
    [SerializeField] private Transform refTransform;

    public Pose lensOffset { set; get; }
    
    public void UpdateTrackingPose(Constants.TargetModel target, Matrix4x4 pose)
    {        
        // Convert from OpenCV to Unity
        Vector3 rawPos = new Vector3(pose.m03, -pose.m13, pose.m23);
        Vector3 forward = new Vector3(pose.m02, -pose.m12, pose.m22);
        Vector3 up = new Vector3(pose.m01, -pose.m11, pose.m21);
        Quaternion rawRot = Quaternion.LookRotation(forward, up);

        // get true lensPose (add lensOffset to refTransform)
        Vector3 lensWorldPos = refTransform.TransformPoint(lensOffset.position);
        Quaternion lensWorldRot = refTransform.rotation * lensOffset.rotation;

        // get world pose of object
        Vector3 finalWorldPos = lensWorldPos + (lensWorldRot * rawPos);
        Quaternion finalWorldRot = lensWorldRot * rawRot;

        var targetTransform = target switch
        {
            Constants.TargetModel.Racket => racketTransform,
            Constants.TargetModel.Pikachu => pikachuTransform,
            Constants.TargetModel.Pen => penTransform
        };
        
        targetTransform.position = Vector3.Lerp(targetTransform.position,  finalWorldPos, 0.2f);
        targetTransform.rotation = Quaternion.Slerp(targetTransform.rotation,  finalWorldRot, 0.2f);
    }
}