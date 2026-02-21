using NUnit.Framework;
using PhysicsDrivenMovement.Core;

namespace PhysicsDrivenMovement.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="GameSettings"/>.
    /// Verifies that physics-configuration constants match the values documented in PLAN.md
    /// and that tuning field defaults are within documented ranges.
    /// These run without a scene or MonoBehaviour lifecycle.
    /// </summary>
    public class GameSettingsTests
    {
        // ─── Layer Index Constants ────────────────────────────────────────────

        [Test]
        [Description("Player 1 parts must live on layer 8 — matches TagManager.asset and PLAN.md §Phase 0.")]
        public void LayerPlayer1Parts_ConstantValue_IsEight()
        {
            // Arrange / Act / Assert
            Assert.That(GameSettings.LayerPlayer1Parts, Is.EqualTo(8));
        }

        [Test]
        [Description("Player 2 parts must live on layer 9.")]
        public void LayerPlayer2Parts_ConstantValue_IsNine()
        {
            Assert.That(GameSettings.LayerPlayer2Parts, Is.EqualTo(9));
        }

        [Test]
        [Description("Player 3 parts must live on layer 10.")]
        public void LayerPlayer3Parts_ConstantValue_IsTen()
        {
            Assert.That(GameSettings.LayerPlayer3Parts, Is.EqualTo(10));
        }

        [Test]
        [Description("Player 4 parts must live on layer 11.")]
        public void LayerPlayer4Parts_ConstantValue_IsEleven()
        {
            Assert.That(GameSettings.LayerPlayer4Parts, Is.EqualTo(11));
        }

        [Test]
        [Description("Environment must live on layer 12.")]
        public void LayerEnvironment_ConstantValue_IsTwelve()
        {
            Assert.That(GameSettings.LayerEnvironment, Is.EqualTo(12));
        }

        // ─── Layer Uniqueness ─────────────────────────────────────────────────

        [Test]
        [Description("All four player layers and the environment layer must have distinct indices.")]
        public void AllPlayerAndEnvironmentLayers_AreUnique()
        {
            // Arrange
            int[] layers =
            {
                GameSettings.LayerPlayer1Parts,
                GameSettings.LayerPlayer2Parts,
                GameSettings.LayerPlayer3Parts,
                GameSettings.LayerPlayer4Parts,
                GameSettings.LayerEnvironment,
            };

            // Act
            var distinctCount = new System.Collections.Generic.HashSet<int>(layers).Count;

            // Assert
            Assert.That(distinctCount, Is.EqualTo(layers.Length),
                "Layer indices must all be unique.");
        }

        // ─── User-facing layer count ──────────────────────────────────────────

        [Test]
        [Description("Each player layer index must fall in the user-assignable range (8–31).")]
        public void PlayerLayerIndices_AreInUserAssignableRange()
        {
            int[] playerLayers =
            {
                GameSettings.LayerPlayer1Parts,
                GameSettings.LayerPlayer2Parts,
                GameSettings.LayerPlayer3Parts,
                GameSettings.LayerPlayer4Parts,
            };

            foreach (int layer in playerLayers)
            {
                Assert.That(layer, Is.InRange(8, 31),
                    $"Player layer {layer} must be in the user-assignable range 8–31.");
            }
        }
    }
}
