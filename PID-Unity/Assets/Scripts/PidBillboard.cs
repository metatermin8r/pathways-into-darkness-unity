using UnityEngine;

namespace Pid
{
    //Custom billboarding implementation to mimic the weird ecentricites of how PID handles objects in the world
    public class PidBillboard : MonoBehaviour
    {
        public int objectIndex;
        public int descriptor;
        public int resource, s1Index;
        public int flags;
        public int nextLink;
        public Vector2 rawPosition;

        [Tooltip("Set by PidWorld (falls back to Camera.main)")]
        public Camera camOverride;

        [Tooltip("Yaw-only billboarding (holdover from attempted vertical look implementation)")]
        public bool yawOnly = true; //Probably don't need this anymore since proper implmentation means PID lacks a floor/ceiling and vertical
                                    //look isn't going to be something we can support with that

        Transform cam;

        void LateUpdate()
        {
            //Resolved every frame rather than cached in Start
            if (cam == null)
            {
                var c = camOverride != null ? camOverride : Camera.main;
                if (c == null) return;
                cam = c.transform;
            }

            Vector3 to = cam.position - transform.position;
            if (yawOnly) to.y = 0f;
            if (to.sqrMagnitude < 1e-6f) return;

            //A Unity Quad faces +Z, so the quad's forward must point at the camera at all times
            transform.rotation = Quaternion.LookRotation(-to.normalized, Vector3.up);
        }
    }
}