using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Suppression composition at avatar_ext_normative 314c95c5: the attenuations of every suppression that targets
    /// an expression are summed, the sum is clamped to [0, 1], and <c>v[t] = u[t] * (1 - clamp(sum, 0, 1))</c>.
    /// Earlier drafts multiplied one factor per suppression. The cases use the hero's restored mouth suppressions,
    /// where <c>happy</c> (blend, amount 1) and <c>surprised</c> (blend, amount 0.5) both target aa, ou and oh, so
    /// the two rules give different results whenever both are active.
    /// </summary>
    public class SandboxSuppressionSumTests
    {
        // Hero expression indices (khr-character-example.glb).
        private const int Happy = 0, Surprised = 4, Aa = 5, Ih = 6, Ou = 7, Ee = 8, Oh = 9, Count = 18;
        private const double HappyAmount = 1.0, SurprisedAmount = 0.5;

        private static IReadOnlyList<SuppressionEntry>[] HeroSuppressions()
        {
            var lists = new IReadOnlyList<SuppressionEntry>[Count];
            lists[Happy] = new[] { Blend(Aa, HappyAmount), Blend(Ih, HappyAmount), Blend(Ou, HappyAmount),
                                   Blend(Ee, HappyAmount), Blend(Oh, HappyAmount) };
            lists[Surprised] = new[] { Blend(Aa, SurprisedAmount), Blend(Ou, SurprisedAmount), Blend(Oh, SurprisedAmount) };
            return lists;
        }

        private static SuppressionEntry Blend(int target, double amount) => new SuppressionEntry
        {
            TargetIndex = target, Type = SuppressionType.Blend, Amount = amount, HasAmount = true,
        };

        private static double[] Drivers(double happy, double surprised)
        {
            var u = new double[Count];
            u[Happy] = happy;
            u[Surprised] = surprised;
            u[Aa] = u[Ih] = u[Ou] = u[Ee] = u[Oh] = 1.0;
            return u;
        }

        [Test]
        public void SharedTargets_SumAttenuationsInsteadOfMultiplyingFactors()
        {
            // happy 0.3 and surprised 0.6: aa receives 0.3 * 1 + 0.6 * 0.5 = 0.6, so v = 0.4. Multiplying the two
            // factors would give (1 - 0.3) * (1 - 0.3) = 0.49.
            var v = ExpressionDriverPipeline.ApplySuppressions(Drivers(0.3, 0.6), HeroSuppressions());
            foreach (int t in new[] { Aa, Ou, Oh })
                Assert.AreEqual(0.4, v[t], 1e-12, $"expression {t}: 1 - (0.3 + 0.3).");
            // ih and ee have one suppression (happy), where both rules agree.
            Assert.AreEqual(0.7, v[Ih], 1e-12);
            Assert.AreEqual(0.7, v[Ee], 1e-12);
            // Suppressions never change their sources.
            Assert.AreEqual(0.3, v[Happy], 1e-12);
            Assert.AreEqual(0.6, v[Surprised], 1e-12);
        }

        [Test]
        public void SharedTargets_ClampTheSumSoTheTargetReachesZero()
        {
            // happy 0.5 and surprised 1: aa receives 0.5 + 0.5 = 1, so it is fully suppressed. Multiplying would have
            // left 0.25 of it.
            var v = ExpressionDriverPipeline.ApplySuppressions(Drivers(0.5, 1.0), HeroSuppressions());
            foreach (int t in new[] { Aa, Ou, Oh })
                Assert.AreEqual(0.0, v[t], 1e-12, $"expression {t} is fully suppressed.");
            Assert.AreEqual(0.5, v[Ih], 1e-12);

            // Both fully active: the sum 1.5 clamps to 1, and the result never goes negative.
            v = ExpressionDriverPipeline.ApplySuppressions(Drivers(1.0, 1.0), HeroSuppressions());
            foreach (int t in new[] { Aa, Ih, Ou, Ee, Oh })
                Assert.AreEqual(0.0, v[t], 1e-12, $"expression {t}: the clamped sum is 1.");
        }

        [Test]
        public void SharedTargets_DoNotDependOnSuppressionOrder()
        {
            var forward = HeroSuppressions();
            var reversed = HeroSuppressions();
            var happy = new List<SuppressionEntry>(reversed[Happy]);
            happy.Reverse();
            reversed[Happy] = happy;
            var surprised = new List<SuppressionEntry>(reversed[Surprised]);
            surprised.Reverse();
            reversed[Surprised] = surprised;
            var u = Drivers(0.35, 0.85);
            CollectionAssert.AreEqual(
                ExpressionDriverPipeline.ApplySuppressions(u, forward),
                ExpressionDriverPipeline.ApplySuppressions(u, reversed));
        }

        [Test]
        public void HeroWire_CarriesTheSuppressionsTheseCasesUse()
        {
            // The hero is an optional Git LFS object; without it the cases above still run on the copied values.
            if (!CharacterLoader.HeroIsRealGlb)
                Assert.Pass("the hero is not a smudged GLB in this checkout; the cases above use its copied values.");
            JObject json = null;
            SandboxTestUtil.PatchGlbJson(File.ReadAllBytes(CharacterLoader.HeroAbsolutePath), j => json = j);
            var entries = (JArray)json["extensions"]["KHR_character_expression"]["expressions"];
            Assert.AreEqual(Count, entries.Count);
            Assert.AreEqual("happy", (string)entries[Happy]["name"]);
            Assert.AreEqual("surprised", (string)entries[Surprised]["name"]);
            var expected = HeroSuppressions();
            foreach (int source in new[] { Happy, Surprised })
            {
                var wire = (JArray)entries[source]["extensions"]["KHR_character_expression_suppression"]["suppressions"];
                Assert.AreEqual(expected[source].Count, wire.Count, $"{entries[source]["name"]}: suppression count.");
                for (int i = 0; i < wire.Count; i++)
                {
                    Assert.AreEqual(expected[source][i].TargetIndex, (int)wire[i]["target"]);
                    Assert.AreEqual("blend", (string)wire[i]["type"]);
                    Assert.AreEqual(expected[source][i].Amount, (double)wire[i]["amount"], 1e-12);
                }
            }
        }
    }
}
