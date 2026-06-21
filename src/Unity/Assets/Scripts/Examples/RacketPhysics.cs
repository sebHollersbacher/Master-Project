using UnityEngine;

public class RacketPhysics : MonoBehaviour
{
    private Rigidbody _rb;
    private Vector3 _previousPosition;
    private Vector3 _currentVelocity;
    private Vector3 _previousFrameVelocity;

    private void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _previousPosition = transform.parent.position;
    }

    private void FixedUpdate()
    {
        Vector3 targetPos = transform.parent.position;
        Quaternion targetRot = transform.parent.rotation;

        Vector3 frameVelocity = (targetPos - _previousPosition) / Time.fixedDeltaTime;

        _currentVelocity = Vector3.Lerp(_previousFrameVelocity, frameVelocity, 0.7f);
        _previousFrameVelocity = frameVelocity;

        _rb.MovePosition(targetPos);
        _rb.MoveRotation(targetRot);

        _previousPosition = targetPos;
    }

    public Vector3 GetVelocity()
    {
        return _currentVelocity;
    }
}