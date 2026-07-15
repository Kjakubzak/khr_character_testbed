using System.Collections;
using UnityEngine;

namespace Samples.Shared
{
    /// <summary>
    /// Shared base for the demo scene controllers. Centralizes the plumbing every demo used to copy-paste — creating
    /// the on-screen panel, resolving the orbit rig + framing the loaded character — and, critically, GUARANTEES a
    /// "Back to Hub" affordance so a demo can never silently ship with no way back (the A18 finding).
    ///
    /// Migration is incremental and low-risk: a controller extends this, builds its panel via <see cref="CreatePanel"/>
    /// (instead of <c>DemoUiBuilder.Create</c>) and frames via <see cref="FrameScene"/>. Once a panel exists, the base
    /// adds a Back-to-Hub button automatically at the end of the build frame; a controller that wants explicit
    /// placement can call <see cref="AddBackToHub"/> itself (idempotent, so the automatic one then no-ops). Controllers
    /// that haven't migrated keep working unchanged.
    /// </summary>
    public abstract class DemoControllerBase : MonoBehaviour
    {
        /// <summary>The demo's panel once <see cref="CreatePanel"/> has been called (null before then).</summary>
        protected DemoUiBuilder Ui { get; private set; }

        private bool _backToHubAdded;

        /// <summary>Create (and remember) the demo panel, and arm the Back-to-Hub guarantee. Use this instead of
        /// <c>DemoUiBuilder.Create</c> so the base can guarantee navigation exists.</summary>
        protected DemoUiBuilder CreatePanel(string title)
        {
            Ui = DemoUiBuilder.Create(title);
            _backToHubAdded = false;
            StartCoroutine(EnsureBackToHub());
            return Ui;
        }

        /// <summary>Add a "Back to Hub" button to the panel. Idempotent — safe to call explicitly (to control
        /// placement) even though the base also guarantees one; the second add no-ops.</summary>
        protected void AddBackToHub()
        {
            if (_backToHubAdded || Ui == null) return;
            var back = gameObject.AddComponent<BackToHubButton>();
            Ui.AddButton("Back to Hub", back.GoToHub);
            _backToHubAdded = true;
        }

        // End-of-build-frame safety net: after the controller's synchronous UI build, ensure a way back exists. A
        // controller that added its own (via AddBackToHub) suppresses this by idempotency. Auto-stopped if the
        // controller is destroyed (e.g. scene change) before it runs, so it never touches a dead object.
        private IEnumerator EnsureBackToHub()
        {
            yield return null;
            AddBackToHub();
        }

        /// <summary>Resolve the scene's orbit rig and frame + face the loaded character — the pattern every demo
        /// duplicated. No-op when there is no rig or the scene has no renderable bounds.</summary>
        protected static void FrameScene(GameObject scene)
        {
            var rig = Object.FindFirstObjectByType<OrbitCameraRig>();
            if (rig == null || scene == null) return;
            if (!SceneBoundsUtil.TryAggregate(scene, out var bounds)) return;
            rig.FrameAndFace(bounds, scene.transform);
        }

        /// <summary>As <see cref="FrameScene(GameObject)"/>, but frames <paramref name="fallback"/> bounds when the
        /// scene has no aggregable renderers (used by demos that load bone-only / mesh-light characters).</summary>
        protected static void FrameScene(GameObject scene, Bounds fallback)
        {
            var rig = Object.FindFirstObjectByType<OrbitCameraRig>();
            if (rig == null || scene == null) return;
            rig.FrameAndFace(SceneBoundsUtil.TryAggregate(scene, out var bounds) ? bounds : fallback, scene.transform);
        }
    }
}
