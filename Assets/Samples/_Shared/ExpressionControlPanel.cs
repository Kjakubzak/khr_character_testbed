using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;

namespace Samples.Shared
{
    /// <summary>
    /// Binds a uGUI control panel to a character's <see cref="ExpressionController"/>. On
    /// <see cref="KhrCharacter.WhenReady"/> it auto-builds one row per expression (a 0/1-snapping slider for binary
    /// (all-STEP) expressions, a free 0..1 slider otherwise) plus a Reset All button. Expression names and counts vary
    /// generated from <see cref="ExpressionController.Expressions"/>; rows are iterated by index and each name is
    /// captured locally so writes are name-stable. Every controller access is null-checked because a capability
    /// (and its sub-controller) may be absent on a given character.
    /// </summary>
    public class ExpressionControlPanel : MonoBehaviour
    {
        [Tooltip("Optional panel to populate. If unset, one is found on this GameObject or created at runtime.")]
        public DemoUiBuilder Ui;

        private KhrCharacter _character;
        private ExpressionController _controller;
        private readonly List<RowBinding> _rows = new List<RowBinding>();

        private struct RowBinding
        {
            public string Name;
            public Slider Slider;
        }

        /// <summary>Bind to a character; rows (re)build when it becomes ready (live import or rehydrated prefab).</summary>
        public void Bind(KhrCharacter character)
        {
            _character = character;
            if (_character == null) return;
            _character.WhenReady(OnReady);
        }

        private void OnReady(KhrCharacter character)
        {
            _controller = character != null ? character.Expressions : null;
            Rebuild();
        }

        /// <summary>Clear and regenerate all rows from the current controller.</summary>
        public void Rebuild()
        {
            EnsureUi();
            Ui.ClearRows();
            _rows.Clear();

            if (_controller == null)
            {
                Ui.AddLabel("No expression data on this character.");
                return;
            }

            var handles = _controller.Expressions;
            if (handles == null || handles.Count == 0)
            {
                Ui.AddLabel("This character has no expressions.");
                return;
            }

            for (int i = 0; i < handles.Count; i++)
            {
                var handle = handles[i];
                string name = handle.Name;
                if (string.IsNullOrEmpty(name)) continue;

                var slider = Ui.AddSlider(name, v => Write(name, v), 0f, 1f, _controller.GetWeight(name));
                // Binary (all-STEP) expressions use a 0/1-snapping slider instead of a free 0..1 slider.
                if (handle.IsBinary) slider.wholeNumbers = true;
                _rows.Add(new RowBinding { Name = name, Slider = slider });
            }

            Ui.AddButton("Reset All", ResetAll);
        }

        /// <summary>Reset every expression weight and reflect it in the controls without re-triggering writes.</summary>
        public void ResetAll()
        {
            if (_controller != null) _controller.ResetAll();

            foreach (var row in _rows)
            {
                float w = _controller != null ? _controller.GetWeight(row.Name) : 0f;
                if (row.Slider != null) row.Slider.SetValueWithoutNotify(w);
            }
        }

        private void Write(string name, float value)
        {
            if (_controller != null) _controller.SetWeight(name, value);
        }

        private void EnsureUi()
        {
            if (Ui == null) Ui = GetComponent<DemoUiBuilder>();
            if (Ui == null) Ui = DemoUiBuilder.Create("Expressions");
        }
    }
}
