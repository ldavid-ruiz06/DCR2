using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace DCR2
{
    // gets attribute from SchoolSpawner to have "static" access to it at all time
    [RequireMatchingQueriesForUpdate]
    [UpdateAfter(typeof(FishSystem))] // needs up-to-date centroids/positions from this frame
    public partial struct SchoolManagerSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var world = state.WorldUnmanaged;

            // Create the one-and-only manager singleton entity.
            var singleton = state.EntityManager.CreateEntity();

            // set up SchoolManagerSingleton component
            state.EntityManager.AddComponentData(singleton, new SchoolManagerSingleton
            {
                schoolCount = 0,
                nextSchoolID = 0
            });
            

            Debug.Log("Manager Created!");
        }

        public void OnUpdate(ref SystemState state)
        {
            // Log the current school count
            var manager = SystemAPI.GetSingleton<SchoolManagerSingleton>();
            Debug.Log(FixedString.Format("School count: {0}", manager.schoolCount));

            // print number of fish per school
            // foreach (var (schoolRecord, memberBuffer) in
            //  SystemAPI.Query<RefRO<SchoolRecord>, DynamicBuffer<SchoolMemberElement>>())
            // {
            //     Debug.Log(FixedString.Format("School {0} has {1} members", schoolRecord.ValueRO.schoolID, memberBuffer.Length));

            //     for (int i = 0; i < memberBuffer.Length; i++)
            //     {
            //         Debug.Log(FixedString.Format("  Member {0}: Entity {1}", i, memberBuffer[i].FishEntity.Index));
            //     }
            // }
        }

    }
}