using UnityEngine;

public class BallSpawner : MonoBehaviour
{
    public GameObject ballPrefab;
    public Transform racketTransform;
    public float spawnInterval = 5f;
    public float spawnHeight = 1f;

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

    private void SpawnBall()
    {
        if (ballPrefab == null || racketTransform == null) return;

        GameObject old = GameObject.FindWithTag("Ball");
        if (old != null) Destroy(old);

        Vector3 spawnPos = racketTransform.position + Vector3.up * spawnHeight;
        GameObject ball = Instantiate(ballPrefab, spawnPos, Quaternion.identity);
        ball.tag = "Ball";
    }
}