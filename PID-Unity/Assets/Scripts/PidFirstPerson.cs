//PidFirstPerson.cs
//Minimal walk-and-look controller, modern character controller values like Gravity exist only to keep
//the CharacterController properly on the ground.

using UnityEngine;

namespace Pid
{
    [RequireComponent(typeof(CharacterController))]
    public class PidFirstPerson : MonoBehaviour
    {
        [Header("Movement")]
        public float walkSpeed = 3.0f; //basically one sector per second
        public float gravity = -9.81f;

        [Header("Look")]
        public Transform cameraPivot;
        public float mouseSensitivity = 2.0f;
        public float pitchLimit = 85f;

        [Header("Debug")]
        public bool logSector = true;

        CharacterController cc;
        float pitch, vy;
        Vector2Int lastSector = new Vector2Int(-1, -1);

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (cameraPivot == null && Camera.main != null)
                cameraPivot = Camera.main.transform;
        }

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            //******LOOK******
            float mx = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            float my = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
            transform.Rotate(Vector3.up, mx, Space.World);
            pitch = Mathf.Clamp(pitch - my, -pitchLimit, pitchLimit);
            if (cameraPivot != null)
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            //******MOVE******
            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f,
                                    Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 1f) input.Normalize();
            Vector3 move = transform.TransformDirection(input) * walkSpeed;

            if (cc.isGrounded && vy < 0f) vy = -2f;
            vy += gravity * Time.deltaTime;
            move.y = vy;

            cc.Move(move * Time.deltaTime);

            //Log what sector we're standing on for debug and map testing purposes
            if (logSector)
            {
                var p = transform.position;
                var s = new Vector2Int(
                    Mathf.FloorToInt(p.x / PidConst.SectorSize),
                    Mathf.FloorToInt(-p.z / PidConst.SectorSize));
                if (s != lastSector)
                {
                    lastSector = s;
                    Debug.Log($"[PID] sector ({s.x},{s.y})");
                }
            }
        }
    }
}