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
    // gets attribute from SchoolSpawner to have "static" access to it at all time
    [RequireMatchingQueriesForUpdate]
    [UpdateAfter(typeof(FishSystem))] // needs up-to-date centroids/positions from this frame
    public partial struct SchoolManagerSystem : ISystem
    {

        public float strayDistance;

        [BurstCompile]
        // public void OnCreate(ref SystemState state)
        // {
        //     var world = state.WorldUnmanaged;

        //     // Create the one-and-only manager singleton entity.
        //     var singleton = state.EntityManager.CreateEntity();

        //     // set up SchoolManagerSingleton component
        //     state.EntityManager.AddComponentData(singleton, new SchoolManagerSingleton
        //     {
        //         schoolCount = 0,
        //         nextSchoolID = 0
        //     });
            
        //     strayDistance = 10f;
        //     Debug.Log("Manager Created!");
        // }

        public void OnUpdate(ref SystemState state)
        {
            // Log the current school count
            var manager = SystemAPI.GetSingleton<SchoolManagerSingleton>();
            var managerEntity = SystemAPI.GetSingletonEntity<SchoolManagerSingleton>();
            var managerRW = SystemAPI.GetComponentRW<SchoolManagerSingleton>(managerEntity);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            //Debug.Log(FixedString.Format("School count: {0}", manager.schoolCount));
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



            // check for stray fish
            strayDistance = 20.0f;
            var straySplitRequestQueue = new NativeQueue<StraySplitRequest>(Allocator.TempJob); // we use a queue so we can easily add and remove elements
            var detectStrayJob = new DetectStrayFishJob
            {
                strayDistance = strayDistance,
                strayRequestWriter = straySplitRequestQueue.AsParallelWriter(),
            };

            state.Dependency = detectStrayJob.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();

            // if theres fish in the queue, split into new school
            while (straySplitRequestQueue.TryDequeue(out StraySplitRequest request))
            {
                SplitFishIntoNewSchool(ref state, ecb, request, ref managerRW.ValueRW);
            }
            straySplitRequestQueue.Dispose();






        

        }

        // jobs 
        partial struct DetectStrayFishJob: IJobEntity
        {
            [ReadOnly] public float strayDistance;
            public NativeQueue<StraySplitRequest>.ParallelWriter strayRequestWriter; // start as an empty queue
            public SemiStaticSchool oldSchool;
            void Execute(Entity entity, in LocalToWorld localToWorld, in DynamicSchool dynamicSchool)
            {
                // get distance from centroid
                float distanceFromCentroid = math.distance(localToWorld.Position, dynamicSchool.centroid);
                // if fish is too far, add to the queue
                if (distanceFromCentroid > strayDistance)
                {
                    strayRequestWriter.Enqueue(new StraySplitRequest
                    {
                        fishEntity = entity,
                        fishPosition = localToWorld.Position,
                        oldSchoolSettings = oldSchool // copy of the blittable shared-component value
                    });
                }

            

            }
        }

        partial struct StraySplitRequest
        {
            public Entity fishEntity;
            public float3 fishPosition;
            public SemiStaticSchool oldSchoolSettings;
        }

        // function that separates the stray fish into their own school
        // it cannot be turned into a job due that it spawns a centroidGizmo for
        // each new school
        private void SplitFishIntoNewSchool(ref SystemState state, EntityCommandBuffer ecb, StraySplitRequest request, ref SchoolManagerSingleton manager)
        {
            int newSchoolID = manager.nextSchoolID;
            manager.nextSchoolID++;
            manager.schoolCount++;

            // Copy the settings captured by the job, just stamp in the new ID.
            var newSchoolSettings = request.oldSchoolSettings;
            newSchoolSettings.schoolID = newSchoolID;

            ecb.SetSharedComponent(request.fishEntity, newSchoolSettings);
            ecb.SetComponent(request.fishEntity, new DynamicSchool { centroid = request.fishPosition });

            // Persistent school-data entity, same as before.
            var newSchoolEntity = ecb.CreateEntity();
            ecb.AddComponent(newSchoolEntity, new SchoolRecord
            {
                schoolID = newSchoolID,
                centroid = request.fishPosition,
                memberCount = 1
            });
            var buffer = ecb.AddBuffer<SchoolMemberElement>(newSchoolEntity);
            buffer.Add(new SchoolMemberElement { FishEntity = request.fishEntity });

            // NEW: spawn a centroid gizmo for this runtime-created school.
            var newGizmoEntity = ecb.Instantiate(manager.centroidPrefab);
            ecb.SetComponent(newGizmoEntity, new centroidGizmo
            {
                centroidPrefab = manager.centroidPrefab,
                centroidID = newSchoolID
            });
            // Optional: give it a distinct color so it's visually clear it's a new/split-off school.
            ecb.SetComponent(newGizmoEntity, new URPMaterialPropertyBaseColor
            {
                Value = new float4(1f, 0f, 0f, 1f) // e.g. red, to flag "runtime-created"
            });

            Debug.Log(FixedString.Format("Fish strayed - created new school {0}", newSchoolID));
        }
    }
}