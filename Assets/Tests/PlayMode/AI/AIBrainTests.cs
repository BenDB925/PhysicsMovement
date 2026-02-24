using System.Collections;
using NUnit.Framework;
using PhysicsDrivenMovement.AI;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for <see cref="AIBrain"/> state transitions,
    /// target selection, and reactive flee behavior.
    /// </summary>
    public class AIBrainTests
    {
        private GameObject _root;
        private Rigidbody _rb;
        private BalanceController _balance;
        private AILocomotion _locomotion;
        private CharacterState _characterState;
        private AIBrain _brain;

        // Interest point test objects.
        private GameObject _interestPointGO;
        private MuseumInterestPoint _interestPoint;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("AIBrain_TestRoot");
            _rb = _root.AddComponent<Rigidbody>();
            _rb.useGravity = false;
            _balance = _root.AddComponent<BalanceController>();
            _locomotion = _root.AddComponent<AILocomotion>();
            _characterState = _root.AddComponent<CharacterState>();
            _brain = _root.AddComponent<AIBrain>();

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _balance.enabled = false;

            // Create a test interest point.
            _interestPointGO = new GameObject("TestInterestPoint");
            _interestPointGO.transform.position = new Vector3(5f, 0f, 5f);
            _interestPoint = _interestPointGO.AddComponent<MuseumInterestPoint>();
            _interestPoint.Initialise(1.5f, Vector3.forward);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }
            if (_interestPointGO != null)
            {
                Object.Destroy(_interestPointGO);
            }
        }

        [UnityTest]
        public IEnumerator StartsInIdleState()
        {
            yield return null; // Allow Awake + Start.

            Assert.That(_brain.CurrentState, Is.EqualTo(AIBrain.AIState.Idle),
                "AIBrain should start in Idle state.");
        }

        [UnityTest]
        public IEnumerator Idle_AfterPause_TransitionsToWalking()
        {
            yield return null;

            // Inject interest points so the brain can pick a target.
            _brain.SetInterestPointsForTest(new[] { _interestPoint });

            // Wait for idle pause to expire and brain to pick a target.
            yield return new WaitForSeconds(3f);

            Assert.That(_brain.CurrentState, Is.Not.EqualTo(AIBrain.AIState.Idle),
                "AIBrain should transition out of Idle after the pause duration.");
        }

        [UnityTest]
        public IEnumerator KnockedOut_WhenHitReceiverFires_EntersKnockedOutState()
        {
            yield return null;

            // Create a Head child with HitReceiver.
            GameObject head = new GameObject("Head");
            head.transform.SetParent(_root.transform);
            head.AddComponent<Rigidbody>();
            HitReceiver hitReceiver = head.AddComponent<HitReceiver>();

            // Need to let Start() run so HitReceiver caches joints.
            yield return null;

            // Trigger knockout via test seam.
            hitReceiver.TriggerKnockoutForTest();

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(_brain.CurrentState, Is.EqualTo(AIBrain.AIState.KnockedOut),
                "AIBrain should enter KnockedOut state when HitReceiver reports knockout.");

            Object.Destroy(head);
        }

        [UnityTest]
        public IEnumerator SetStateForTest_SetsCurrentState()
        {
            yield return null;

            _brain.SetStateForTest(AIBrain.AIState.Observing);
            Assert.That(_brain.CurrentState, Is.EqualTo(AIBrain.AIState.Observing),
                "SetStateForTest should set the current state.");

            _brain.SetStateForTest(AIBrain.AIState.Fleeing);
            Assert.That(_brain.CurrentState, Is.EqualTo(AIBrain.AIState.Fleeing),
                "SetStateForTest should set the current state.");
        }
    }
}
