using System;

namespace Samples.Editor
{
    /// <summary>
    /// Marks a public static method as a sample-character generator that <see cref="SandboxCI"/> should
    /// discover reflectively. Signature contract: the method must be
    /// <c>public static string Generate(string outputDirectory)</c> — takes the output directory,
    /// writes a ``.glb`` file, returns the absolute path.
    ///
    /// Adding a new SC-*/VH-* fixture becomes ONE code touch: define the generator method in
    /// <see cref="SampleCharacterFactory"/> and decorate it with <c>[SampleCharacter("NAME")]</c>.
    /// SandboxCI's build/snapshot pipeline picks it up automatically.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class SampleCharacterAttribute : Attribute
    {
        /// <summary>Fixture short name — matches the ``.glb`` basename produced by the generator
        /// (e.g. <c>"SC-Face"</c> → produces <c>SC-Face.glb</c>).</summary>
        public string Name { get; }

        public SampleCharacterAttribute(string name)
        {
            Name = name;
        }
    }
}
