using UnityEngine;

namespace PhysicsDrivenMovement.Environment
{
    public class TwirlingHammerHazard : MonoBehaviour
    {
        [SerializeField] private float _rotationSpeed = 180f;
        [SerializeField] private Vector3 _rotationAxis = Vector3.right;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
        
        }

        // Update is called once per frame
        void Update()
        {
            transform.Rotate(_rotationAxis, _rotationSpeed * Time.deltaTime);        
        }
    }
}
