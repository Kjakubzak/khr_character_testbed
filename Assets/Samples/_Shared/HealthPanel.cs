using UnityEngine;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;

namespace Samples.Shared
{
    /// <summary>
    /// Renders a character's live <see cref="KhrCharacter.GetHealth"/> snapshot into a uGUI <see cref="Text"/>:
    /// expression count and each capability's Active / Degraded / Inert status. Bind a character + a text element;
    /// it refreshes every frame so rig switches and similar changes show immediately.
    /// </summary>
    public class HealthPanel : MonoBehaviour
    {
        private KhrCharacter _hub;
        private Text _text;

        public void Bind(KhrCharacter hub, Text text)
        {
            _hub = hub;
            _text = text;
        }

        private void Update()
        {
            if (_hub == null || _text == null) return;

            var report = _hub.GetHealth();
            if (report == null) { _text.text = "(no health data)"; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Expressions: {report.ExpressionCount}");
            foreach (var capability in report.Capabilities)
                sb.AppendLine($"  {capability.Capability}: {capability.Status}");
            _text.text = sb.ToString();
        }
    }
}
