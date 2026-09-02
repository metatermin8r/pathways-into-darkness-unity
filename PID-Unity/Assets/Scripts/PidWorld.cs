// PidWorld.cs
// Supersedes PidLevelLoader. Delete PidLevelLoader.cs - two components both building
// geometry into the same scene will fight.
//
// Owns: the level catalogue, the current level's geometry, its doors, and level
// transitions. Everything keys off which sector the player is standing on, computed
// from their transform each frame. No trigger volumes: a grid game already knows where
// you are, and 1,024 collider objects per level to rediscover it would be absurd.

using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Pid
{
    public class PidWorld : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("L00.json .. L24.json. Order does not matter; indexed by level_number.")]
        public TextAsset[] levelJsons;

        [Header("Rendering")]
        public Material flatMaterial;
        public Material doorMaterial;

        [Header("Player")]
        public GameObject player;
        public int startLevel = 0;
        public Vector2Int startSector = new Vector2Int(16, 30);
        public float eyeHeight = 1.0f;

        [Header("Doors")]
        [Tooltip("Open when the player stands on the door tile or either walkable neighbour.")]
        public bool openOnApproach = true;

        [Header("Debug")]
        public bool logCounts = true;
        public bool logSectorHop = true;

        readonly Dictionary<int, PidLevel> catalogue = new Dictionary<int, PidLevel>();
        readonly List<PidDoor> doors = new List<PidDoor>();

        PidLevel current;
        GameObject levelRoot;
        Vector2Int lastSector = new Vector2Int(-1, -1);

        // A ladder is two-way and symmetric: Ground Floor's (28,3) departs to level 1 AND
        // receives from level 1. So the player ARRIVES standing on a transition tile.
        // Without this latch the arrival immediately re-fires the transition and the two
        // levels ping-pong forever. Armed only once the player steps off.
        bool transitionArmed = true;

        void Start()
        {
            if (!BuildCatalogue()) return;
            LoadLevel(startLevel, startSector.x, startSector.y);
        }

        bool BuildCatalogue()
        {
            if (levelJsons == null || levelJsons.Length == 0)
            {
                Debug.LogError("[PID] No level JSON assigned.");
                return false;
            }
            foreach (var ta in levelJsons)
            {
                if (ta == null) continue;
                PidLevel lvl;
                try { lvl = PidLevelMesher.Parse(ta.text); }
                catch (JsonException e)
                {
                    Debug.LogError($"[PID] {ta.name} failed to parse: {e.Message}");
                    continue;
                }
                if (lvl?.sectors == null || lvl.sectors.Count != PidConst.Grid * PidConst.Grid)
                {
                    Debug.LogError($"[PID] {ta.name}: expected 1024 sectors.");
                    continue;
                }
                catalogue[lvl.level_number] = lvl;
            }
            Debug.Log($"[PID] catalogue: {catalogue.Count} level(s).");
            return catalogue.Count > 0;
        }

        // ---- level construction --------------------------------------------------

        public void LoadLevel(int number, int spawnX, int spawnY)
        {
            if (!catalogue.TryGetValue(number, out var lvl))
            {
                Debug.LogError($"[PID] Level {number} not in catalogue; staying put.");
                return;
            }

            if (levelRoot != null) Destroy(levelRoot);
            doors.Clear();
            current = lvl;

            levelRoot = new GameObject($"L{number:D2}_{lvl.name}");
            levelRoot.transform.SetParent(transform, false);

            var r = PidLevelMesher.Build(lvl);

            if (logCounts)
                Debug.Log($"[PID] L{lvl.level_number:D2} '{lvl.name}'  " +
                          $"nonVoid={r.nonVoid} pillars={r.pillars} walkable={r.walkable}  " +
                          $"floors={r.floorQuads} wallQuads={r.wallQuads} " +
                          $"colliderQuads={r.colliderQuads} " +
                          $"synthPillarFaces={r.synthesizedPillarFaces} " +
                          $"skippedSolidSolid={r.skippedSolidSolid}  " +
                          $"components={r.components} largest={r.largestComponent}");

            // Multiple components are expected and intended. Components with no way IN
            // are not - see the note in PidLevelMesher.Connectivity.
            if (r.unreachable.Count > 0)
                Debug.LogError($"[PID] {r.unreachable.Count} walkable region(s) with no arrival " +
                               $"coordinate: {string.Join("; ", r.unreachable)}");

            var geo = new GameObject("Geometry");
            geo.transform.SetParent(levelRoot.transform, false);
            geo.AddComponent<MeshFilter>().sharedMesh = r.render;
            geo.AddComponent<MeshRenderer>().sharedMaterial = flatMaterial;

            var phys = new GameObject("Collision");
            phys.transform.SetParent(levelRoot.transform, false);
            phys.AddComponent<MeshCollider>().sharedMesh = r.collision;

            BuildDoors(lvl);
            PlacePlayer(spawnX, spawnY);

            lastSector = new Vector2Int(spawnX, spawnY);
            transitionArmed = false;   // we may have landed on a transition tile
        }

        void BuildDoors(PidLevel lvl)
        {
            if (lvl.doors == null) return;
            var root = new GameObject("Doors");
            root.transform.SetParent(levelRoot.transform, false);

            foreach (var s in lvl.sectors)
            {
                if (s.type != PidConst.TypeDoor) continue;
                if (s.type_addl < 0 || s.type_addl >= lvl.doors.Count)
                {
                    Debug.LogWarning($"[PID] door sector ({s.x},{s.y}) addl={s.type_addl} " +
                                     "out of range of door_list.");
                    continue;
                }
                var def = lvl.doors[s.type_addl];

                // Travel axis: the pair of opposite neighbours that are both walkable.
                bool ns = !lvl.IsSolid(s.x, s.y - 1) && !lvl.IsSolid(s.x, s.y + 1);
                bool ew = !lvl.IsSolid(s.x - 1, s.y) && !lvl.IsSolid(s.x + 1, s.y);
                if (ns == ew)
                {
                    Debug.LogWarning($"[PID] door ({s.x},{s.y}) has an ambiguous travel axis " +
                                     $"(ns={ns} ew={ew}); skipping.");
                    continue;
                }

                // Slide direction must be PERPENDICULAR to travel, or the panel would
                // retract along the corridor instead of into a jamb.
                Vector3 slide;
                bool slideIsEW;
                switch (def.direction)
                {
                    case 0: slide = Vector3.left; slideIsEW = true; break;  // x_negative
                    case 2: slide = Vector3.right; slideIsEW = true; break;  // x_positive
                    case 1: slide = Vector3.forward; slideIsEW = false; break;  // y_negative = +Z
                    case 3: slide = Vector3.back; slideIsEW = false; break;  // y_positive = -Z
                    default:
                        Debug.LogWarning($"[PID] door ({s.x},{s.y}) direction={def.direction} " +
                                         "unrecognised; skipping.");
                        continue;
                }
                if (slideIsEW != ns)
                {
                    // ns travel wants an east-west slide; ew travel wants north-south.
                    Debug.LogWarning($"[PID] door ({s.x},{s.y}) slide direction " +
                                     $"'{def.direction_name}' is parallel to travel " +
                                     "(ns=" + ns + "). Panel would retract along the corridor. " +
                                     "Direction 2 (x_positive) is unattested on Ground Floor - " +
                                     "if this fires, the direction mapping needs revisiting.");
                }

                const float T = 0.15f;    // panel thickness
                float S = PidConst.SectorSize, H = PidConst.WallHeight;

                var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = $"Door_{def.index}_({s.x},{s.y})_tex{def.texture}";
                panel.transform.SetParent(root.transform, false);
                panel.transform.localScale = ns ? new Vector3(S, H, T) : new Vector3(T, H, S);
                if (doorMaterial != null)
                    panel.GetComponent<MeshRenderer>().sharedMaterial = doorMaterial;

                var d = panel.AddComponent<PidDoor>();
                d.sectorX = s.x; d.sectorY = s.y;
                d.doorIndex = def.index; d.textureId = def.texture;
                d.travelIsNorthSouth = ns;
                d.Configure(PidLevelMesher.SectorCentre(s.x, s.y, H * 0.5f), slide * S);
                doors.Add(d);
            }

            if (logCounts && doors.Count > 0)
                Debug.Log($"[PID] built {doors.Count} door(s).");
        }

        void PlacePlayer(int x, int y)
        {
            if (player == null) return;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;                 // it overwrites transform writes
            player.transform.position = PidLevelMesher.SectorCentre(x, y, eyeHeight);
            if (cc != null) cc.enabled = true;
        }

        // ---- per-frame -----------------------------------------------------------

        void Update()
        {
            if (current == null || player == null) return;

            var p = player.transform.position;
            var s = new Vector2Int(Mathf.FloorToInt(p.x / PidConst.SectorSize),
                                   Mathf.FloorToInt(-p.z / PidConst.SectorSize));

            if (s != lastSector)
            {
                lastSector = s;
                transitionArmed = true;   // stepped off whatever we were on
                if (logSectorHop)
                {
                    var sec = current.At(s.x, s.y);
                    Debug.Log($"[PID] sector ({s.x},{s.y}) {(sec == null ? "OOB" : sec.type_name)}");
                }
            }

            if (openOnApproach) UpdateDoors(s);
            CheckTransition(s);
        }

        void UpdateDoors(Vector2Int at)
        {
            foreach (var d in doors)
            {
                // Open from the door tile or either walkable neighbour along travel.
                bool near = (at.x == d.sectorX && at.y == d.sectorY) ||
                            (d.travelIsNorthSouth
                                ? at.x == d.sectorX && Mathf.Abs(at.y - d.sectorY) == 1
                                : at.y == d.sectorY && Mathf.Abs(at.x - d.sectorX) == 1);
                d.SetWanted(near);
            }
        }

        void CheckTransition(Vector2Int at)
        {
            if (!transitionArmed) return;
            var sec = current.At(at.x, at.y);
            if (sec == null || sec.type != PidConst.TypeChangeLevel) return;

            if (current.level_changes == null ||
                sec.type_addl < 0 || sec.type_addl >= current.level_changes.Count)
            {
                Debug.LogWarning($"[PID] change_level ({at.x},{at.y}) addl={sec.type_addl} " +
                                 "out of range of level_change_list.");
                transitionArmed = false;
                return;
            }

            var e = current.level_changes[sec.type_addl];
            if (!e.live)
            {
                Debug.LogWarning($"[PID] change_level ({at.x},{at.y}) points at a dead entry.");
                transitionArmed = false;
                return;
            }

            Debug.Log($"[PID] {e.type_name} from L{current.level_number:D2} ({at.x},{at.y}) " +
                      $"-> L{e.dest_level:D2} ({e.dest_x},{e.dest_y})");
            LoadLevel(e.dest_level, e.dest_x, e.dest_y);
        }
    }
}