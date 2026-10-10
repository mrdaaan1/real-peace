using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace RealPeace
{
    /// <summary>
    /// Навигация строится кодом из тех же данных, что и карта (без пакета AI Navigation):
    /// земля — проходима, русло реки, дома, стены, изгороди и стволы — нет, настилы мостов — проходимы.
    /// Два навмеша: для пехоты (узкий агент) и для танков (широкий — по пешеходным мостикам и сквозь лес не пройдут).
    /// </summary>
    public static class NavBuilder
    {
        public static int InfantryAgent { get; private set; }
        public static int TankAgent { get; private set; } = -1;

        static readonly List<NavMeshDataInstance> instances = new List<NavMeshDataInstance>();
        const int NotWalkable = 1;

        public static void Build()
        {
            Clear();
            var sources = CollectSources();
            var bounds = new Bounds(new Vector3(0f, 5f, 0f), new Vector3(MapLayout.Size + 4f, 40f, MapLayout.Size + 4f));

            var inf = NavMesh.GetSettingsByID(0);
            inf.agentRadius = 0.45f;
            inf.agentHeight = 1.8f;
            inf.agentClimb = 0.5f;
            inf.agentSlope = 40f;
            inf.overrideVoxelSize = true;
            inf.voxelSize = 0.22f;
            inf.overrideTileSize = true;
            inf.tileSize = 256;
            InfantryAgent = inf.agentTypeID;
            Add(NavMeshBuilder.BuildNavMeshData(inf, sources, bounds, Vector3.zero, Quaternion.identity));

            var tank = NavMesh.CreateSettings();
            tank.agentRadius = 2.2f;
            tank.agentHeight = 3f;
            tank.agentClimb = 0.5f;
            tank.agentSlope = 28f;
            tank.overrideVoxelSize = true;
            tank.voxelSize = 0.5f;
            tank.overrideTileSize = true;
            tank.tileSize = 128;
            TankAgent = tank.agentTypeID;
            Add(NavMeshBuilder.BuildNavMeshData(tank, sources, bounds, Vector3.zero, Quaternion.identity));
        }

        static void Add(NavMeshData data)
        {
            if (data == null)
            {
                Debug.LogError("[Real Peace] Навмеш не построился");
                return;
            }
            instances.Add(NavMesh.AddNavMeshData(data));
        }

        public static void Clear()
        {
            foreach (var i in instances) i.Remove();
            instances.Clear();
            if (TankAgent >= 0)
            {
                NavMesh.RemoveSettings(TankAgent);
                TankAgent = -1;
            }
        }

        static List<NavMeshBuildSource> CollectSources()
        {
            var list = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = MapBuilder.GroundMesh,
                    transform = Matrix4x4.identity,
                    area = 0,
                },
            };

            // Настилы мостов
            foreach (var b in MapLayout.Bridges)
                list.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = new Vector3(MapLayout.BridgeHalfLength * 2f, 0.3f, b.halfWidth * 2f),
                    transform = Matrix4x4.TRS(new Vector3(MapLayout.RiverX(b.z), MapLayout.DeckY - 0.15f, b.z), Quaternion.identity, Vector3.one),
                    area = 0,
                });

            // Русло: непроходимо всё, что ниже бровки (настил мостов выше — он остаётся)
            for (float z = -MapLayout.Half - 4f; z <= MapLayout.Half + 4f; z += 4f)
                Block(new Vector3(MapLayout.RiverX(z), (MapLayout.BedY - 1f + 0.05f) / 2f, z),
                    new Vector3((MapLayout.RiverBedHalf + 2.2f) * 2f, -MapLayout.BedY + 1f + 0.05f, 4.6f), 0f, list);

            foreach (var b in MapLayout.Buildings)
                Block(b.center + Vector3.up * 5f, new Vector3(b.size.x + 0.4f, 30f, b.size.z + 0.4f), b.yaw, list);

            foreach (var b in MapLayout.Barriers)
            {
                if (b.kind == BarrierKind.Fence) continue; // заборчики перелезают
                var a = new Vector3(b.a.x, 0f, b.a.y);
                var c = new Vector3(b.b.x, 0f, b.b.y);
                var d = c - a;
                float w = b.kind == BarrierKind.Hedge ? 1.1f : 0.7f;
                Block((a + c) / 2f + Vector3.up * 5f, new Vector3(w, 30f, d.magnitude), Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, list);
            }

            foreach (var t in MapLayout.Trees)
                Block(new Vector3(t.x, 5f, t.z), new Vector3(0.7f * t.y, 30f, 0.7f * t.y), 0f, list);

            return list;
        }

        static void Block(Vector3 center, Vector3 size, float yaw, List<NavMeshBuildSource> list)
        {
            list.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.ModifierBox,
                size = size,
                transform = Matrix4x4.TRS(center, Quaternion.Euler(0f, yaw, 0f), Vector3.one),
                area = NotWalkable,
            });
        }

        /// <summary>Ближайшая точка навмеша нужного агента (или исходная, если рядом ничего).</summary>
        public static bool Snap(Vector3 p, int agentType, out Vector3 result, float radius = 8f)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
            if (NavMesh.SamplePosition(p, out var hit, radius, filter))
            {
                result = hit.position;
                return true;
            }
            result = p;
            return false;
        }
    }
}
