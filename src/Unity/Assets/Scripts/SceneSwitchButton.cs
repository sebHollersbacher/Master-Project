using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider))]
public class SceneSwitchButton : MonoBehaviour
{
    public bool isNext;
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("TrackingObject"))
            return;

        if (isNext)
        {
            var next = (SceneManager.GetActiveScene().buildIndex + 1) % SceneManager.sceneCountInBuildSettings;
            SceneManager.LoadScene(next);
        }
        else
        {
            var count = SceneManager.sceneCountInBuildSettings;
            var prev = (SceneManager.GetActiveScene().buildIndex - 1 + count) % count;
            SceneManager.LoadScene(prev);
        }
    }
}