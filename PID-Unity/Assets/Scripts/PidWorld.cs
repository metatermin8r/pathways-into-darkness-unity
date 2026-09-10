//Handles the level list, the current level's geometry, doors, materials, and moves you between levels at transition sectors
//Which sector you're standing on gets worked out from your transform each frame. "Triggers" like level transitions are hardcoded sectors

using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Pid
{
    //World objects like corpses and pillars, from the save file rather than the map JSON, because that's where PID keeps them
    public class PidObject
    {
        public int index;
        public int x_raw, y_raw;      //10-bit fixed point, 1024 to a sector
        public int descriptor;        //tag 6, selecting resources 128-191
        public int flags;
        public int link;              //$FFFF end of chain

        public bool IsFree => link == 0xFFFE;
        public int SectorX => x_raw >> 10;
        public int SectorY => y_raw >> 10;
    }

    public class PidObjectSet
    {
        public int level;
        public List<PidObject> objects;
    }

    public class PidWorld : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("L00.json .. L24.json, order does not matter since its indexed by level_number")]
        public TextAsset[] levelJsons;
        [Tooltip("manifest.json goes here, without it walls fall back to flatMaterial")]
        public TextAsset textureManifest;
        [Tooltip("objects_L00.json .. objects_L24.json, exported from a save file's world blocks")]
        public TextAsset[] objectJsons;

        [Header("Rendering")]
        [Tooltip("Fallback for any wall whose tile could not be resolved")]
        public Material flatMaterial;
        [Tooltip("Floor mat, goes unused since the floor isn't real in PID proper")]
        public Material floorMaterial;
        [Tooltip("Ceiling mat, same as the floor it goes unused since its not rendered in PID proper")]
        public Material ceilingMaterial;
        [Tooltip("Template for wall materials generated at runtime")]
        public Material wallMaterialTemplate;
        public Material doorMaterial;
        [Tooltip("Resources subfolder holding the tile PNGs")]
        public string tileResourcePath = "PidTiles";
        [Tooltip("Resources subfolder holding the LUT PNGs")]
        public string lutResourcePath = "PidLuts";

        [Header("Sector markers (debug stuff)")]
        public Material changeLevelMaterial;
        public Material saveMaterial;
        public Material triggerMaterial;
        public Material doorTileMaterial;
        public Material corpseMaterial;

        [Header("Player")]
        public GameObject player;
        public int startLevel = 0;
        //Ground Floor's real start, hard coded from seeing it in-game in the emulator because I CAN'T FIND WHERE THE GAME SETS THIS
        public Vector2Int startSector = new Vector2Int(16, 16);
        [Tooltip("Camera height, PID sets this at 614/1023 of a wall sector, use this as a manual override otherwise leave at 0 and it'll sort itself")]
        public float eyeHeight = 0f;
        [Tooltip("Use PID's fixed 61.93 degree vertical FOV")]
        public bool applyOriginalFov = true;

        [Header("Objects")]
        [Tooltip("Template for billboard materials")]
        public Material billboardMaterialTemplate;
        public bool spawnObjects = true;
        [Tooltip("Camera the billboards turn to face, should be Camera.main")]
        public Camera playerCamera;
        [Tooltip("(DEBUG) Log the first few objects resolved world sizes")]
        public bool logObjectSizes = true;
        [Tooltip("Draw a placeholder where a wall descriptor has no tile (probably should depreciate)")]
        public bool drawMissingTiles = false;
        [Tooltip("Pixels per sector, used to size a sprite from its tile when the manifest has no world size (it now does, should probably depreciate)")]
        public float pixelsPerSector = 113f;
        [Tooltip("Last-resort size in sectors when neither world size nor tile dimensions are known (DEPRECIATE)")]
        public float defaultObjectSize = 0.8f;

        [Header("Doors")]
        public bool openOnApproach = true;

        [Header("Debug")]
        public bool logCounts = true;
        public bool logSectorHop = true;
        [Tooltip("Auto-open doors when you stand next to them")]
        public bool debugProximityDoors = true;

        readonly Dictionary<int, PidLevel> catalogue = new Dictionary<int, PidLevel>();
        readonly Dictionary<int, PidObjectSet> objectSets = new Dictionary<int, PidObjectSet>();
        readonly Dictionary<int, Material> tileMaterials = new Dictionary<int, Material>();
        readonly List<PidDoor> doors = new List<PidDoor>();

        PidTextureManifest manifest;
        bool warnedNoWorldSize;
        PidLevel current;
        GameObject levelRoot;
        Vector2Int lastSector = new Vector2Int(-1, -1);

        //Without this, the trigger re-fires and the player ping-pongs between two levels at frame rate
        bool transitionArmed = true;

        void Start()
        {
            LoadManifest();
            LoadObjects();
            if (!BuildCatalogue()) return;
            LoadLevel(startLevel, startSector.x, startSector.y);
        }

        void LoadManifest()
        {
            if (textureManifest == null)
            {
                Debug.LogWarning("[PID] No texture manifest; walls will use flatMaterial.");
                return;
            }
            try { manifest = PidTextureManifest.Parse(textureManifest.text); }
            catch (JsonException e) { Debug.LogError($"[PID] Manifest parse failed: {e.Message}"); }
        }

        void LoadObjects()
        {
            if (objectJsons == null) return;
            foreach (var ta in objectJsons)
            {
                if (ta == null) continue;
                try
                {
                    var set = JsonConvert.DeserializeObject<PidObjectSet>(ta.text);
                    if (set?.objects != null) objectSets[set.level] = set;
                }
                catch (JsonException e)
                {
                    Debug.LogError($"[PID] {ta.name} failed to parse: {e.Message}");
                }
            }
            if (objectSets.Count > 0)
                Debug.Log($"[PID] objects: {objectSets.Count} level(s).");
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

        //**********Materials**********

        //Missing tiles fall back to flatMaterial so you can see where they are.
        Material MaterialForTile(int key, Material template = null)
        {
            if (tileMaterials.TryGetValue(key, out var cached)) return cached;

            Material mat = null;
            if (key != PidLevelMesher.TileKeyMissing && manifest != null)
            {
                PidLevelMesher.SplitTileKey(key, out int res, out int s1, out int palette);
                var tile = manifest.Get(res)?.Get(s1);
                string path = tile?.Png(palette);
                if (!string.IsNullOrEmpty(path))
                {
                    int dot = path.LastIndexOf('.');
                    if (dot > 0) path = path.Substring(0, dot);
                    var tex = Resources.Load<Texture2D>($"{tileResourcePath}/{path}");
                    if (tex != null)
                    {
                        //Force Point Filter here so a mis-imported asset is obvious rather than quietly blurry
                        tex.filterMode = FilterMode.Point;
                        tex.wrapMode = TextureWrapMode.Clamp;
                        var src = template != null ? template
                                : wallMaterialTemplate != null ? wallMaterialTemplate
                                : flatMaterial;
                        mat = src != null ? new Material(src) : null;
                        if (mat != null)
                        {
                            mat.name = $"tile_{res}_{s1}_v{palette}";
                            mat.mainTexture = tex;

                            int lv = current != null ? current.level_number : 0;
                            string lutName = PidShade.LutName(lv, res, palette);
                            var lut = Resources.Load<Texture2D>($"{lutResourcePath}/{lutName}");
                            if (lut != null)
                            {
                                lut.filterMode = FilterMode.Point;
                                lut.wrapMode = TextureWrapMode.Clamp;
                                mat.SetTexture("_Lut", lut);
                            }
                            else Debug.LogWarning($"[PID] no shade LUT {lutName}; " +
                                                  "tile will render as raw indices.");
                        }
                    }
                    else Debug.LogWarning($"[PID] tile texture not found: {tileResourcePath}/{path}");
                }
            }

            if (mat == null) mat = flatMaterial;   //missing tiles stay visible
            tileMaterials[key] = mat;
            return mat;
        }

        //**********Level Construction**********

        public void LoadLevel(int number, int spawnX, int spawnY)
        {
            if (!catalogue.TryGetValue(number, out var lvl))
            {
                Debug.LogError($"[PID] Level {number} not in catalogue; staying put.");
                return;
            }

            if (levelRoot != null) Destroy(levelRoot);
            //Materials bind a level-specific LUT
            tileMaterials.Clear();
            doors.Clear();
            current = lvl;

            levelRoot = new GameObject($"L{number:D2}_{lvl.name}");
            levelRoot.transform.SetParent(transform, false);

            PidLevelMesher.drawMissingTiles = drawMissingTiles;
            var r = PidLevelMesher.Build(lvl, manifest);

            if (logCounts)
                Debug.Log($"[PID] L{lvl.level_number:D2} '{lvl.name}'  " +
                          $"nonVoid={r.nonVoid} pillars={r.pillars} walkable={r.walkable}  " +
                          $"floors={r.floorQuads} wallQuads={r.wallQuads} " +
                          $"colliderQuads={r.colliderQuads} " +
                          $"pillarColliders={r.pillarBoundaryColliders} " +
                          $"skippedSolidSolid={r.skippedSolidSolid} " +
                          $"skippedTag0={r.skippedTagZero} missingTiles={r.missingTiles}  " +
                          $"submeshes={r.submeshTileKey.Count + 1}  " +
                          $"components={r.components} largest={r.largestComponent}");

            if (r.missingTiles > 0)
                Debug.LogWarning($"[PID] {r.missingTiles} wall quad(s) had no tile in the " +
                                 "manifest and use flatMaterial.");

            if (r.unreachable.Count > 0)
                Debug.LogError($"[PID] {r.unreachable.Count} walkable region(s) with no arrival " +
                               $"coordinate: {string.Join("; ", r.unreachable)}");

            var geo = new GameObject("Geometry");
            geo.transform.SetParent(levelRoot.transform, false);
            geo.AddComponent<MeshFilter>().sharedMesh = r.render;

            //Submesh 0 is floors, 1 is ceilings, 2+ are all wall tiles
            var mats = new Material[r.submeshTileKey.Count + 2];
            mats[0] = floorMaterial != null ? floorMaterial : flatMaterial;
            mats[1] = ceilingMaterial != null ? ceilingMaterial : flatMaterial;
            for (int i = 0; i < r.submeshTileKey.Count; i++)
                mats[i + 2] = MaterialForTile(r.submeshTileKey[i]);
            geo.AddComponent<MeshRenderer>().sharedMaterials = mats;

            var phys = new GameObject("Collision");
            phys.transform.SetParent(levelRoot.transform, false);
            phys.AddComponent<MeshCollider>().sharedMesh = r.collision;

            BuildMarkers(r);
            BuildDoors(lvl);
            BuildObjects(lvl);
            PlacePlayer(spawnX, spawnY);

            lastSector = new Vector2Int(spawnX, spawnY);
            transitionArmed = false;   //for level transitions
        }

        Material MarkerMaterialFor(int sectorType)
        {
            switch (sectorType)
            {
                case PidConst.TypeChangeLevel: return changeLevelMaterial;
                case PidConst.TypeSave: return saveMaterial;
                case PidConst.TypeDoorTrigger:
                case PidConst.TypeOtherTrigger: return triggerMaterial;
                case PidConst.TypeDoor:
                case PidConst.TypeSecretDoor: return doorTileMaterial;
                case PidConst.TypeCorpse: return corpseMaterial;
                default: return null;
            }
        }

        void BuildMarkers(PidLevelMesher.Result r)
        {
            if (r.markers == null || r.markers.Count == 0) return;
            var root = new GameObject("Markers");
            root.transform.SetParent(levelRoot.transform, false);

            foreach (var kv in r.markers)
            {
                var mat = MarkerMaterialFor(kv.Key);
                if (mat == null) continue;          //unassigned type: do not draw
                var go = new GameObject($"mark_type{kv.Key}");
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = kv.Value;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
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

                if (def.direction < 0 || def.direction > 3)
                {
                    Debug.LogWarning($"[PID] door ({s.x},{s.y}) direction={def.direction} " +
                                     "out of range 0-3; skipping.");
                    continue;
                }

                var go = new GameObject($"Door_{def.index}_({s.x},{s.y})_tex{def.texture}");
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();

                var dr = manifest?.DoorRate(def.texture);
                if (dr == null)
                {
                    Debug.LogWarning($"[PID] no door_rates row for texture {def.texture}; " +
                                     "falling back to texture 0.");
                    dr = new PidDoorRate { texture = 0, face_s1 = 12, cap_s1 = 13, rate = 12 };
                }

                //Door art lives in the wall resource
                const int DoorResource = 192;
                mr.sharedMaterials = new[]
                {
                    MaterialForTile(PidLevelMesher.TileKey(DoorResource, dr.face_s1, 0)),
                    MaterialForTile(PidLevelMesher.TileKey(DoorResource, dr.cap_s1, 0)),
                };

                var d = go.AddComponent<PidDoor>();

                d.Configure(s.x, s.y, def.index, def.texture, def.direction,
                            PidDoor.Closed, 0, dr.rate);

                doors.Add(d);
                if (logCounts)
                    Debug.Log($"[PID] door {def.index} ({s.x},{s.y}) dir={def.direction} " +
                              $"tex={def.texture} rate={d.Rate}/tick " +
                              $"full={d.TraversalSeconds:F3}s slidesY={d.SlidesY} " +
                              $"anchorHigh={d.AnchorHigh}");
            }

            if (logCounts && doors.Count > 0)
                Debug.Log($"[PID] built {doors.Count} door(s).");
        }

        //One quad per object
        void BuildObjects(PidLevel lvl)
        {
            if (!spawnObjects) return;
            if (!objectSets.TryGetValue(lvl.level_number, out var set)) return;

            var root = new GameObject("Objects");
            root.transform.SetParent(levelRoot.transform, false);

            int spawned = 0, unresolved = 0;
            float unit = PidConst.RawToMetres;   //one conversion, used everywhere

            foreach (var o in set.objects)
            {
                if (o == null || o.IsFree) continue;

                int res = PidConst.Resource(o.descriptor);
                int s1 = PidConst.S1Index(o.descriptor);
                var tile = manifest?.Get(res)?.Get(s1);

                float w, h;
                if (tile != null && tile.world_w > 0 && tile.world_h > 0)
                {
                    w = tile.world_w * unit;
                    h = tile.world_h * unit;
                }
                else if (tile != null && tile.width > 0 && tile.height > 0 && pixelsPerSector > 0f)
                {
                    w = tile.width / pixelsPerSector * PidConst.SectorSize;
                    h = tile.height / pixelsPerSector * PidConst.SectorSize;
                    if (!warnedNoWorldSize)
                    {
                        warnedNoWorldSize = true;
                        Debug.LogWarning("[PID] manifest has no world_w/world_h; sizing sprites " +
                                         "from tile pixels. Re-export the manifest to fix.");
                    }
                }
                else
                {
                    w = h = defaultObjectSize * PidConst.SectorSize;
                }

                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = $"obj_{o.index}_res{res}_s1{s1}";
                go.transform.SetParent(root.transform, false);
                Destroy(go.GetComponent<Collider>());   //collision comes from the map

                //Sprites sit "lift" distance above the floor, which is zero for most things
                float baseY = (tile != null ? tile.lift : 0) * unit;
                go.transform.position = new Vector3(
                    o.x_raw * unit, baseY + h * 0.5f, -(o.y_raw * unit));
                go.transform.localScale = new Vector3(w, h, 1f);

                //Object sprites aren't in any level's texture list
                int key = PidLevelMesher.TileKey(res, s1, 0);
                var mat = MaterialForTile(key, billboardMaterialTemplate);
                if (mat == flatMaterial) unresolved++;
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;

                var b = go.AddComponent<PidBillboard>();
                if (playerCamera != null) b.camOverride = playerCamera;
                b.objectIndex = o.index; b.descriptor = o.descriptor;
                b.resource = res; b.s1Index = s1;
                b.flags = o.flags; b.nextLink = o.link;
                b.rawPosition = new Vector2(o.x_raw, o.y_raw);

                if (logObjectSizes && spawned < 8)
                    Debug.Log($"[PID] obj {o.index} res{res} s1{s1} " +
                              $"raw({o.x_raw},{o.y_raw}) sector({o.SectorX},{o.SectorY}) " +
                              $"tile={(tile == null ? "null" : $"{tile.width}x{tile.height}")} " +
                              $"world=({(tile?.world_w ?? 0)},{(tile?.world_h ?? 0)}) " +
                              $"world=({tile?.world_w ?? 0},{tile?.world_h ?? 0}) lift={tile?.lift ?? 0} " +
                              $"-> {w:F2}m x {h:F2}m");
                spawned++;
            }

            if (logCounts)
                Debug.Log($"[PID] objects: spawned={spawned} unresolvedSprites={unresolved}");
        }

        void PlacePlayer(int x, int y)
        {
            if (player == null) return;
            //CharacterController overwrites direct transform writes while enabled, so disable it for the teleport
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            float eye = eyeHeight > 0f ? eyeHeight : PidConst.EyeHeight;
            player.transform.position = PidLevelMesher.SectorCentre(x, y, eye);

            //Handles fixed 4:3 FOV stuff
            if (applyOriginalFov && playerCamera != null)
            {
                playerCamera.fieldOfView = PidConst.VerticalFovDegrees;
                playerCamera.usePhysicalProperties = false;
            }
            if (cc != null) cc.enabled = true;
        }

        //**********Per-Frame**********

        void Update()
        {
            if (current == null || player == null) return;

            var p = player.transform.position;
            var s = new Vector2Int(Mathf.FloorToInt(p.x / PidConst.SectorSize),
                                   Mathf.FloorToInt(-p.z / PidConst.SectorSize));

            if (s != lastSector)
            {
                lastSector = s;
                transitionArmed = true;
                if (logSectorHop)
                {
                    var sec = current.At(s.x, s.y);
                    Debug.Log($"[PID] sector ({s.x},{s.y}) {(sec == null ? "OOB" : sec.type_name)}");
                }
            }

            if (openOnApproach) UpdateDoors(s);
            CheckTransition(s);
        }

        //Placeholder for proximity open doors, since triggers aren't understood yet
        void UpdateDoors(Vector2Int at)
        {
            if (!debugProximityDoors) return;

            foreach (var d in doors)
            {
                bool near = (at.x == d.sectorX && at.y == d.sectorY) ||
                            (d.SlidesY
                                ? at.x == d.sectorX && Mathf.Abs(at.y - d.sectorY) == 1
                                : at.y == d.sectorY && Mathf.Abs(at.x - d.sectorX) == 1);

                if (near) d.Open(); else d.Close();
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

        //Development readout for sectors
        void OnGUI()
        {
            if (current == null) return;

            var sec = current.At(lastSector.x, lastSector.y);
            string what = sec == null ? "out of bounds" : sec.type_name;
            string extra = "";
            if (sec != null && sec.type_addl != 0) extra = $"  addl={sec.type_addl}";
            if (sec != null && sec.item != -1) extra += $"  item={sec.item}";

            string wall = "";
            if (sec != null && sec.walls != null && sec.walls.Count >= 2)
            {
                int w0 = sec.walls[0].Word, w1 = sec.walls[1].Word;
                wall = $"N ${w0:X4} tag{PidConst.Tag(w0)} res{PidConst.Resource(w0)} " +
                       $"s1={PidConst.S1Index(w0)}   " +
                       $"W ${w1:X4} tag{PidConst.Tag(w1)} res{PidConst.Resource(w1)} " +
                       $"s1={PidConst.S1Index(w1)}";
            }

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = Color.white }
            };

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(6, 6, 660, 78), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(12, 10, 700, 96),
                $"L{current.level_number:D2}  {current.name}\n" +
                $"sector ({lastSector.x},{lastSector.y})  {what}{extra}\n" +
                wall,
                style);
        }
    }
}