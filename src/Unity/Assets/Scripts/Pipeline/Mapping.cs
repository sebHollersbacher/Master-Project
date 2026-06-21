using UnityEngine;

public class Mapping : MonoBehaviour
{
    [SerializeField] private Transform _targetTransform;
    [SerializeField] private Transform centerEyeAnchor;
    
    [SerializeField] private Vector3 smoothing = new(0.4f, 0.4f, 0.15f);
    
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

        _targetTransform.rotation = Quaternion.Slerp(_targetTransform.rotation, finalWorldRot, 0.15f);
        Vector3 smoothedPos = new Vector3();
        smoothedPos.x = Mathf.Lerp(_targetTransform.position.x, finalWorldPos.x, 0.5f);   // responsive
        smoothedPos.y = Mathf.Lerp(_targetTransform.position.y, finalWorldPos.y, 0.5f);   // responsive
        smoothedPos.z = Mathf.Lerp(_targetTransform.position.z, finalWorldPos.z, 0.15f);  // heavily dampened
        _targetTransform.position = smoothedPos;
    }
}