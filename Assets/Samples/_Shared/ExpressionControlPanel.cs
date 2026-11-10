using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;

namespace Samples.Shared
{
    /// <summary>
    /// Binds a uGUI control panel to a character's <see cref="ExpressionController"/>. On
    /// <see cref="KhrCharacter.WhenReady"/> it auto-builds one row per expression (a 0/1-snapping slider when the
    /// host-authored <c>IsBinary</c> metadata is set, a free 0..1 slider otherwise) plus a Reset All button. Expression
    /// names and counts vary by asset and are generated from <see cref="ExpressionController.Expressions"/>. Each row
    /// binds its expression's array index, because names need not be unique. Every controller access is null-checked
    /// because a capability (and its sub-controller) may be absent on a given character.
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
            public int Index;
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
                int index = i;
                string label = string.IsNullOrEmpty(handle.Name) ? $"Expression {index}" : handle.Name;

                var slider = Ui.AddSlider(label, v => Write(index, v), 0f, 1f, _controller.GetWeight(index));
                // Host-authored binary metadata, not STEP interpolation, selects a 0/1-snapping slider.
                if (handle.IsBinary) slider.wholeNumbers = true;
                _rows.Add(new RowBinding { Index = index, Slider = slider });
            }

            Ui.AddButton("Reset All", ResetAll);
        }

        /// <summary>Reset every expression weight and reflect it in the controls without re-triggering writes.</summary>
        public void ResetAll()
        {
            if (_controller != null)
            {
                _controller.UseDirectNativeInputs();
                _controller.ResetAll();
            }

            foreach (var row in _rows)
            {
                float w = _controller != null ? _controller.GetWeight(row.Index) : 0f;
                if (row.Slider != null) row.Slider.SetValueWithoutNotify(w);
            }
        }

        private void Write(int index, float value)
        {
            if (_controller == null) return;
            _controller.UseDirectNativeInputs();
            _controller.SetWeight(index, value);
        }

        private void EnsureUi()
        {
            if (Ui == null) Ui = GetComponent<DemoUiBuilder>();
            if (Ui == null) Ui = DemoUiBuilder.Create("Expressions");
        }
    }
}
