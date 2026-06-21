using UnityEngine;

public class BallPhysics : MonoBehaviour
{
    public float hitForceMultiplier = 1.3f;
    public float minHitSpeed = 0.3f;
    public float maxBallSpeed = 20f;
    [Range(0f, 1f)]
    public float racketRestitution = 0.85f;
    public float hitCooldown = 0.1f;
    public float separationOffset = 0.03f;

    public LayerMask racketLayer;

    private Rigidbody _rb;
    private float _lastHitTime = -1f;
    private Vector3 _previousPosition;
    private float _ballRadius;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _previousPosition = transform.position;

        SphereCollider sc = GetComponent<SphereCollider>();
        if (sc != null)
        {
            float maxScale = Mathf.Max(transform.lossyScale.x,
                                       transform.lossyScale.y,
                                       transform.lossyScale.z);
            _ballRadius = sc.radius * maxScale;
        }
        else
        {
            _ballRadius = 0.02f;
        }
    }

    void FixedUpdate()
    {
        Vector3 movement = transform.position - _previousPosition;
        float distance = movement.magnitude;

        if (distance > 0.001f && Time.time - _lastHitTime > hitCooldown)
        {
            RaycastHit hit;
            if (Physics.SphereCast(_previousPosition, _ballRadius, movement.normalized,
                                   out hit, distance, racketLayer))
            {
                RacketPhysics racket = hit.collider.GetComponentInParent<RacketPhysics>();
                if (racket != null)
                {
                    ApplyHit(hit.normal, hit.point, racket);
                }
            }
        }

        _previousPosition = transform.position;
    }

    void OnTriggerEnter(Collider other)
    {
        RacketPhysics racket = other.GetComponentInParent<RacketPhysics>();
        if (racket == null) return;
        if (Time.time - _lastHitTime < hitCooldown) return;
        
        Transform racketT = racket.transform;
        Vector3 faceNormal = racketT.forward;
        if (Vector3.Dot(faceNormal, transform.position - racketT.position) < 0f)
            faceNormal = -faceNormal;

        Vector3 contactPoint = transform.position - faceNormal * _ballRadius;

        ApplyHit(faceNormal, contactPoint, racket);
    }

    private void ApplyHit(Vector3 normal, Vector3 contactPoint, RacketPhysics racket)
    {
        Vector3 racketVelocity = racket.GetVelocity();
        float racketSpeed = racketVelocity.magnitude;

        if (racketSpeed < minHitSpeed) return;

        _lastHitTime = Time.time;

        Vector3 ballVel = _rb.linearVelocity;

        float ballNormalSpeed = Vector3.Dot(ballVel, normal);
        Vector3 ballTangent = ballVel - ballNormalSpeed * normal;

        float racketNormalSpeed = Vector3.Dot(racketVelocity, normal);

        float e = racketRestitution;
        float newNormalSpeed = -e * ballNormalSpeed
                             + (1f + e) * racketNormalSpeed * hitForceMultiplier;

        Vector3 newVelocity = ballTangent + newNormalSpeed * normal;

        if (newVelocity.magnitude > maxBallSpeed)
            newVelocity = newVelocity.normalized * maxBallSpeed;

        _rb.linearVelocity = newVelocity;
        transform.position = contactPoint + normal * separationOffset;
    }
}