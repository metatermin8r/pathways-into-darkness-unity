// PidLevelLoader.cs
// The only MonoBehaviour in the geometry stack. Unity requires the file name to
// match the component class name, so this lives on its own. Everything it depends
// on is in PidLevelMesher.cs; none of those are MonoBehaviours, so they can share
// a file.

using Newtonsoft.Json;
using UnityEngine;

namespace Pid
{
    public class PidLevelLoader : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("JSON from pid-re/reference/export/, e.g. L00.json")]
        public TextAsset levelJson;

        [Header("Rendering")]
        public Material flatMaterial;

        [Header("Player")]
        public GameObject player;
        public Vector2Int spawnSector = new Vector2Int(16, 30);
        [Tooltip("CharacterController capsule CENTRE, not the camera. 0.9 for a 1.8 m capsule.")]
        public float eyeHeight = 0.9f;

        [Header("Verification")]
        public bool logCounts = true;

        void Start()
        {
            if (levelJson == null) { Debug.LogError("[PID] No level JSON assigned."); return; }

            PidLevel lvl;
            try { lvl = PidLevelMesher.Parse(levelJson.text); }
            catch (JsonException e) { Debug.LogError($"[PID] JSON parse failed: {e.Message}"); return; }

            if (!Validate(lvl)) return;

            var r = PidLevelMesher.Build(lvl);

            if (logCounts)
                Debug.Log($"[PID] L{lvl.level_number:D2} '{lvl.name}'  " +
                          $"nonVoid={r.nonVoid} pillars={r.pillars} walkable={r.walkable}  " +
                          $"floors={r.floorQuads} wallQuads={r.wallQuads} " +
                          $"colliderQuads={r.colliderQuads} " +
                          $"synthPillarFaces={r.synthesizedPillarFaces} " +
                          $"skippedSolidSolid={r.skippedSolidSolid}  " +
                          $"components={r.components} largest={r.largestComponent}");

            if (r.unreachable.Count > 0)
                Debug.LogError($"[PID] {r.unreachable.Count} save/ladder tiles outside the main " +
                               $"component: {string.Join(", ", r.unreachable)}");

            var geo = new GameObject("Level");
            geo.transform.SetParent(transform, false);
            geo.AddComponent<MeshFilter>().sharedMesh = r.render;
            geo.AddComponent<MeshRenderer>().sharedMaterial = flatMaterial;

            var phys = new GameObject("LevelCollision");
            phys.transform.SetParent(transform, false);
            phys.AddComponent<MeshCollider>().sharedMesh = r.collision;

            if (player != null)
            {
                // CharacterController overwrites direct transform writes while enabled,
                // so it has to be off for the teleport to stick.
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.transform.position =
                    PidLevelMesher.SectorCentre(spawnSector.x, spawnSector.y, eyeHeight);
                player.transform.rotation = Quaternion.identity;   // +Z = game north
                if (cc != null) cc.enabled = true;
            }
        }

        /// The export states its own format, grid size and slot ordering. Check the
        /// engine's assumptions against those declarations rather than trusting them,
        /// so a change on the pid-re side surfaces here as a named error instead of as
        /// mysterious geometry.
        bool Validate(PidLevel lvl)
        {
            if (lvl == null || lvl.sectors == null)
            {
                Debug.LogError("[PID] Deserialized to null. Is this a pid_level_v1 export?");
                return false;
            }
            if (lvl.format != "pid_level_v1")
                Debug.LogWarning($"[PID] Unexpected format '{lvl.format}'; expected pid_level_v1.");

            if (lvl.grid != PidConst.Grid)
            {
                Debug.LogError($"[PID] Export declares grid={lvl.grid}, engine assumes {PidConst.Grid}.");
                return false;
            }
            int expected = PidConst.Grid * PidConst.Grid;
            if (lvl.sectors.Count != expected)
            {
                Debug.LogError($"[PID] Expected {expected} sectors, got {lvl.sectors.Count}.");
                return false;
            }

            // Slot order is the single most load-bearing assumption in the mesher, and the
            // one the fan documentation disagrees about. Petrich has walls[0] as the north
            // (-Y) edge; Semmler's Torch docs say the opposite, and that reading breaks
            // Ground Floor. The export labels the slots, so confirm rather than assume.
            var w = lvl.sectors[0].walls;
            if (w == null || w.Count != 6)
            {
                Debug.LogError($"[PID] Expected 6 wall slots, got {(w == null ? 0 : w.Count)}.");
                return false;
            }
            if (w[0].slot != "wall_y" || w[1].slot != "wall_x")
            {
                Debug.LogError($"[PID] Slot order changed: walls[0]='{w[0].slot}' walls[1]='{w[1].slot}'. " +
                               "The mesher requires walls[0]=wall_y (north), walls[1]=wall_x (west).");
                return false;
            }

            // Blocking comes from wall.blocks_movement. If the parser ever emits a rule the
            // engine has not been checked against, say so out loud.
            if (lvl.movement_rule != "{32}")
                Debug.LogWarning($"[PID] Export movement_rule is '{lvl.movement_rule}', not '{{32}}'. " +
                                 "Colliders follow blocks_movement, so this is informational.");
            return true;
        }
    }
}