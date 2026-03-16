using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.EditMode.Character
{
    /// <summary>
    /// Focused EditMode seam tests for GroundSensor terrain sensing so Chapter 7 can extend
    /// the per-foot contact payload without needing a full prefab-backed locomotion harness.
    /// </summary>
    public class GroundSensorTests
    {
        private const float StepHeight = 0.2f;

        private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly int EnvironmentMask = 1 << GameSettings.LayerEnvironment;

        private GameObject _ground;
        private GameObject _step;
        private GameObject _foot;
        private GroundSensor _sensor;

        [TearDown]
        public void TearDown()
        {
            if (_sensor != null)
            {
                Object.DestroyImmediate(_sensor);
            }

            if (_foot != null)
            {
                Object.DestroyImmediate(_foot);
            }

            if (_step != null)
            {
                Object.DestroyImmediate(_step);
            }

            if (_ground != null)
            {
                Object.DestroyImmediate(_ground);
            }
        }

        [Test]
        public void FixedUpdate_WhenStepUpAhead_ReportsForwardObstructionAndEstimatedHeight()
        {
            // Arrange
            CreateTerrain();
            CreateFootSensor();

            // Act
            InvokePrivateMethod(_sensor, "Awake");
            InvokePrivateMethod(_sensor, "FixedUpdate");

            // Assert
            Assert.That(_sensor.IsGrounded, Is.True,
                "The seam setup should keep the test foot grounded on the flat approach surface.");
            Assert.That(_sensor.HasForwardObstruction, Is.True,
                "GroundSensor should report the authored step-up face as a forward obstruction.");
            Assert.That(_sensor.EstimatedStepHeight, Is.EqualTo(StepHeight).Within(0.08f),
                "GroundSensor should estimate the top-surface rise of the step-up ahead of the foot.");
            Assert.That(_sensor.ForwardObstructionConfidence, Is.GreaterThan(0.25f),
                "A clean face-plus-top detection should produce a usable non-zero confidence.");
            Assert.That(_sensor.ForwardObstructionTopSurfacePoint.y, Is.EqualTo(StepHeight).Within(0.08f),
                "GroundSensor should preserve the sampled top-surface point so the planner can place touchdown onto the raised landing.");
            Assert.That(_sensor.ForwardObstructionTopSurfacePoint.z, Is.GreaterThan(0.01f),
                "The preserved top-surface point should lie ahead of the current support point.");
        }

        [Test]
        public void FixedUpdate_WhenGroundAheadIsFlat_DoesNotReportForwardObstruction()
        {
            // Arrange
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "Ground";
            _ground.layer = GameSettings.LayerEnvironment;
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(4f, 1f, 4f);
            CreateFootSensor();

            // Act
            InvokePrivateMethod(_sensor, "Awake");
            InvokePrivateMethod(_sensor, "FixedUpdate");

            // Assert
            Assert.That(_sensor.IsGrounded, Is.True,
                "The seam setup should keep the test foot grounded on the flat surface.");
            Assert.That(_sensor.HasForwardObstruction, Is.False,
                "GroundSensor should not synthesize a step-up obstruction when the terrain ahead stays flat.");
            Assert.That(_sensor.EstimatedStepHeight, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(_sensor.ForwardObstructionConfidence, Is.EqualTo(0f).Within(0.0001f));
        }

        private void CreateTerrain()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "Ground";
            _ground.layer = GameSettings.LayerEnvironment;
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(4f, 1f, 4f);

            _step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _step.name = "Step";
            _step.layer = GameSettings.LayerEnvironment;
            _step.transform.position = new Vector3(0f, StepHeight * 0.5f, 0.20f);
            _step.transform.localScale = new Vector3(1.4f, StepHeight, 0.3f);
        }

        private void CreateFootSensor()
        {
            _foot = new GameObject("Foot");
            _foot.transform.position = new Vector3(0f, 0.08f, 0.0f);

            SphereCollider collider = _foot.AddComponent<SphereCollider>();
            collider.radius = 0.035f;
            collider.center = Vector3.zero;

            _sensor = _foot.AddComponent<GroundSensor>();
            SetPrivateField(_sensor, "_groundLayers", (LayerMask)EnvironmentMask);
            SetPrivateField(_sensor, "_castRadius", 0.04f);
            SetPrivateField(_sensor, "_castDistance", 0.12f);

            Physics.SyncTransforms();
        }

        private static void InvokePrivateMethod(MonoBehaviour component, string methodName)
        {
            MethodInfo method = component.GetType().GetMethod(methodName, PrivateInstance);
            Assert.That(method, Is.Not.Null, $"Expected private method '{methodName}' on {component.GetType().Name}.");
            method.Invoke(component, null);
        }

        private static void SetPrivateField<TValue>(object instance, string fieldName, TValue value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, PrivateInstance);
            Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}' on {instance.GetType().Name}.");
            field.SetValue(instance, value);
        }
    }
}