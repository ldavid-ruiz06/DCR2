using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Unity.Rendering;

namespace DCR2
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateAfter(typeof(FishSystem))] // needs up-to-date centroids/positions from this frame
    [UpdateAfter(typeof(SchoolManagerSystem))]
    public partial struct DebugLineSystem : ISystem
    {
        [BurstCompile]

        public void OnUpdate(ref SystemState state)
        {
            var fishQuery = SystemAPI.QueryBuilder().WithAll<DynamicSchool>().WithAll<SemiStaticSchool>().WithAll<Fish>().Build();
            var fishArray = fishQuery.ToEntityArray(Allocator.TempJob);
            //Debug.Log(FixedString.Format("Debug Line Fish Count: {0}", fishQuery.CalculateEntityCount()));
            // int i = 0;

            foreach (var fish in fishArray)
            {
                var transform = SystemAPI.GetComponent<LocalToWorld>(fish);
                var centroidData = SystemAPI.GetComponent<DynamicSchool>(fish);

                Debug.DrawLine(transform.Position, centroidData.centroid);
                // Debug.Log(FixedString.Format("Debug I: {0}", i));
                // i++;
            }
        }
    }
}