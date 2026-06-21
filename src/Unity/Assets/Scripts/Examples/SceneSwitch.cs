using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneSwitch : MonoBehaviour
{
    void Update()
    {
        // next
        if (OVRInput.GetDown(OVRInput.Button.Two))
        {
            var next = (SceneManager.GetActiveScene().buildIndex + 1) % SceneManager.sceneCountInBuildSettings;
            Debug.Log("[Scene] Switch to " + next);
            SceneManager.LoadScene(next);
        }
        
        // reset
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            Debug.Log("[Scene] Reload");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
