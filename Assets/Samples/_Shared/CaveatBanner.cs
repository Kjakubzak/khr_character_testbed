using UnityEngine;

namespace Samples.Shared
{
    /// <summary>
    /// Displays a short, static caveat/notice string (e.g. "tracks glTF PR #2512, non-ratified") in a demo panel.
    /// Attach to any GameObject, set <see cref="Message"/>, and call <see cref="Apply"/> with the scene's panel.
    /// </summary>
    public class CaveatBanner : MonoBehaviour
    {
        [TextArea]
        public string Message = string.Empty;

        /// <summary>Append the message as a label row to the given panel (no-op when empty).</summary>
        public void Apply(DemoUiBuilder ui)
        {
            if (ui == null || string.IsNullOrEmpty(Message)) return;
            ui.AddLabel(Message);
        }
    }
}
