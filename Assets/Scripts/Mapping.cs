using UnityEngine;

public class Mapping
{
    private Transform _ref;

    public Mapping(Transform _ref)
    {
        this._ref = _ref;
    }

    public void UpdateTrackingPose(Transform transform, Matrix4x4 pose)
    {
        // 1. POSITION
        // Flip Y because OpenCV Y is Down, Unity Y is Up
        Vector3 position = new Vector3(pose.m03, -pose.m13, pose.m23);

        // 2. ROTATION (The Vector Method)
        // We extract the Forward (Z) and Up (Y) vectors from the matrix columns.
        // Col 1 is Up, Col 2 is Forward.

        // In M3T, +Y is Down. In Unity, we want that vector to map to Down (-Y).
        // So we flip the Y component of the vector itself to convert the space.
        Vector3 forward = new Vector3(pose.m02, -pose.m12, pose.m22);
        Vector3 up = new Vector3(pose.m01, -pose.m11, pose.m21);

        // Now simply look in that direction. 
        // This handles all the quaternion math for you.
        Quaternion rotation = Quaternion.LookRotation(forward, up);

        // 3. APPLY
        transform.position = _ref.TransformPoint(position);
        transform.rotation = _ref.rotation * rotation;
    }
}