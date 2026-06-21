using UnityEngine;

public class BallSpawner : MonoBehaviour
{
    public GameObject ballPrefab;

    public Transform tableCenter;
    public float tableLength = 2.5f;
    public float tableWidth = 1.5f;
    public float spawnInterval = 5f;
    public float spawnHeight = 0.4f;
    public float forwardSpeed = 2.1f;
    public float downwardSpeed = 0.3f;

    [Range(0f, 1f)]
    public float lateralSpawnSpread = 0.6f;

    [Range(0f, 1f)]
    public float lateralAngleSpread = 0.5f;

    private float _timer;

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer >= spawnInterval)
        {
            _timer = 0f;
            SpawnBall();
        }
    }

    public void SpawnBall()
    {
        if (ballPrefab == null || tableCenter == null) return;

        GameObject old = GameObject.FindWithTag("Ball");
        if (old != null) Destroy(old);

        float side   = Random.value < 0.5f ? -1f : 1f;
        float spread = Random.Range(0.15f, 0.5f);
        float lateralOffset = side * spread * tableWidth * lateralSpawnSpread;

        Vector3 lateral = Vector3.Cross(tableCenter.forward, Vector3.up).normalized;

        Vector3 farSide = tableCenter.position
                          - tableCenter.forward * (tableLength * 0.35f)
                          + lateral             * lateralOffset;

        Vector3 spawnPos = farSide + Vector3.up * spawnHeight;

        GameObject ball = Instantiate(ballPrefab, spawnPos, Quaternion.identity);
        ball.tag = "Ball";

        Rigidbody rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
        {
            float normalizedOffset = lateralOffset / (tableWidth * 0.5f);

            float rawAngle    = Random.Range(-0.5f, 0.5f);
            float corrected   = rawAngle - normalizedOffset * 0.6f;
            float lateralSpeed = corrected * forwardSpeed * lateralAngleSpread;

            rb.linearVelocity = tableCenter.forward * forwardSpeed
                                + lateral              * lateralSpeed
                                + Vector3.down         * downwardSpeed;
        }
    }
}