using UnityEngine;
using UnityEngine.SceneManagement;

namespace Samples.Shared
{
    /// <summary>
    /// Navigation helper that loads the hub scene. Wire <see cref="GoToHub"/> to a uGUI button's onClick, or call
    /// it directly. Warns (rather than throwing) when the hub scene is not in Build Settings.
    /// </summary>
    public class BackToHubButton : MonoBehaviour
    {
        public string HubSceneName = "SampleHub";

        public void GoToHub()
        {
            if (string.IsNullOrEmpty(HubSceneName))
                return;

            if (Application.CanStreamedLevelBeLoaded(HubSceneName))
                SceneManager.LoadScene(HubSceneName);
            else
                Debug.LogWarning($"[Samples] Hub scene '{HubSceneName}' is not in Build Settings; cannot navigate.");
        }
    }
}
