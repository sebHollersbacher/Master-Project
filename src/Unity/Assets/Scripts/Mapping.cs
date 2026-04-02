using UnityEngine;

public class Mapping : MonoBehaviour
{
    [SerializeField] private Transform _targetTransform;
    [SerializeField] private Transform centerEyeAnchor;
    
    public void UpdatePose(Matrix4x4 pose)
    {
        // Convert from OpenCV to Unity
        Vector3 rawPos = new Vector3(pose.m03, -pose.m13, pose.m23);
        Vector3 forward = new Vector3(pose.m02, -pose.m12, pose.m22);
        Vector3 up = new Vector3(pose.m01, -pose.m11, pose.m21);

        if (forward.sqrMagnitude < 0.0001f || float.IsNaN(forward.x))
            return;
        Quaternion rawRot = Quaternion.LookRotation(forward, up);

        // get world pose of object (relative to centerEyeAnchor)
        Vector3 finalWorldPos = centerEyeAnchor.TransformPoint(rawPos);
        Quaternion finalWorldRot = centerEyeAnchor.rotation * rawRot;

        _targetTransform.position = finalWorldPos;
        _targetTransform.rotation = finalWorldRot;
        // _targetTransform.position = Vector3.Lerp(_targetTransform.position, finalWorldPos, 0.3f);
        // _targetTransform.rotation = Quaternion.Slerp(_targetTransform.rotation, finalWorldRot, 0.3f);
    }
}