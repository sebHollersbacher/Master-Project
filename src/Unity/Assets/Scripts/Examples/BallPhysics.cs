using UnityEngine;

public class BallPhysics : MonoBehaviour
{
    public float hitForceMultiplier = 1.5f;
    public float minHitSpeed = 0.5f;
    public float maxBallSpeed = 20f;

    private Rigidbody _rb;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    void OnCollisionEnter(Collision collision)
    {
        RacketPhysics racket = collision.gameObject.GetComponent<RacketPhysics>();
        if (racket == null) return;

        Vector3 racketVelocity = racket.GetVelocity();
        float racketSpeed = racketVelocity.magnitude;

        // Only add force when the racket is actively moving
        if (racketSpeed < minHitSpeed) return;

        Vector3 contactNormal = collision.contacts[0].normal;
        Vector3 hitDir = (contactNormal + racketVelocity.normalized).normalized;
        _rb.AddForce(hitDir * racketSpeed * hitForceMultiplier, ForceMode.VelocityChange);

        if (_rb.linearVelocity.magnitude > maxBallSpeed)
        {
            _rb.linearVelocity = _rb.linearVelocity.normalized * maxBallSpeed;
        }
    }
}