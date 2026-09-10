using UnityEngine;

namespace Pid
{
    //Builds the floor/ceiling gradient as a camera-locked quad in the Background render queue
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class PidBackdrop : MonoBehaviour
    {
        [Tooltip("Material using PID/Backdrop (created at runtime using backdropShader if left empty)")]
        public Material backdropMaterial;

        [Tooltip("Used only when backdropMaterial is empty")]
        public Shader backdropShader;

        [Tooltip("Reads the lit flag, falls back to the first PidShade found")]
        public PidShade shade;

        static readonly int LitId = Shader.PropertyToID("_Lit");

        Camera cam;
        Transform quad;
        Material mat;

        void OnEnable()
        {
            cam = GetComponent<Camera>();

            mat = backdropMaterial;
            if (mat == null && backdropShader != null) mat = new Material(backdropShader);
            if (mat == null)
            {
                Debug.LogWarning("[PID] PidBackdrop has no material or shader; floor and " +
                                 "ceiling will show whatever the camera clears to.");
                enabled = false;
                return;
            }

            if (shade == null)
#if UNITY_2023_1_OR_NEWER
                shade = Object.FindFirstObjectByType<PidShade>();
#else
                shade = Object.FindObjectOfType<PidShade>();
#endif

            Build();
        }

        void OnDisable()
        {
            if (quad != null) DestroyImmediate(quad.gameObject);
            quad = null;
        }

        void Build()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "PidBackdrop";
            go.hideFlags = HideFlags.DontSave;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            quad = go.transform;
            quad.SetParent(transform, false);
            Place();
        }

        void Place()
        {
            if (quad == null || cam == null) return;

            float d = cam.nearClipPlane * 1.05f;
            float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = h * cam.aspect;

            quad.localPosition = new Vector3(0f, 0f, d);
            quad.localRotation = Quaternion.identity;
            quad.localScale = new Vector3(w, h, 1f);
        }

        void LateUpdate()
        {
            Place();
            if (mat != null)
                mat.SetFloat(LitId, shade != null && shade.View14 > PidShade.Unlit ? 1f : 0f);
        }
    }
}