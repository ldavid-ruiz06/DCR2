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
    [UpdateAfter(typeof(CentroidGizmoSystem))] 
    public partial struct SchoolManagerSystem : ISystem
    {

        public float strayDistance;
        public bool splitting;

        //[BurstCompile]

        public void OnUpdate(ref SystemState state)
        {
            // Managers and EntityCommandBuffer
            var manager = SystemAPI.GetSingleton<SchoolManagerSingleton>();
            var managerEntity = SystemAPI.GetSingletonEntity<SchoolManagerSingleton>();
            var managerRW = SystemAPI.GetComponentRW<SchoolManagerSingleton>(managerEntity);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            
            // hashmap that has schoolID -> current fish count map
            // used to avoid empty schools because all the fish left
            var schoolQuery = SystemAPI.QueryBuilder().WithAll<SchoolRecord>().Build();
            int schooulCountNow = schoolQuery.CalculateEntityCount();
            var schoolMemberCounts = new NativeHashMap<int, int>(math.max(schooulCountNow, 1), Allocator.TempJob);
            foreach (var (schoolRecord, memberBuffer) in
                     SystemAPI.Query<RefRO<SchoolRecord>, DynamicBuffer<SchoolMemberElement>>())
            {
                schoolMemberCounts.TryAdd(schoolRecord.ValueRO.schoolID, memberBuffer.Length);
            }


            // check for stray fish
            strayDistance = 20.0f;
            splitting = false;
            var straySplitRequestQueue = new NativeQueue<StraySplitRequest>(Allocator.TempJob); // we use a queue so we can easily add and remove elements
            var detectStrayJob = new DetectStrayFishJob
            {
                strayDistance = strayDistance,
                strayRequestWriter = straySplitRequestQueue.AsParallelWriter(),
                schoolMemberCounts = schoolMemberCounts,
                splitting = splitting,
            };

            state.Dependency = detectStrayJob.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();
            schoolMemberCounts.Dispose();

            // if theres fish in the queue, split into new school
            while (straySplitRequestQueue.TryDequeue(out StraySplitRequest request))
            {
                SplitFishIntoNewSchool(ref state, ecb, request, ref managerRW.ValueRW);
            }
            straySplitRequestQueue.Dispose();






        

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        // jobs 
        partial struct DetectStrayFishJob: IJobEntity
        {
            [ReadOnly] public float strayDistance;
            public NativeQueue<StraySplitRequest>.ParallelWriter strayRequestWriter; // start as an empty queue
            public SemiStaticSchool oldSchool;
            [ReadOnly] public NativeHashMap<int, int> schoolMemberCounts; // schoolID -> current fish
            public bool splitting;
            
            
            void Execute(Entity entity, in LocalToWorld localToWorld, in DynamicSchool dynamicSchool)
            {
                // get distance from centroid
                float distanceFromCentroid = math.distance(localToWorld.Position, dynamicSchool.centroid);
                // Debug.Log(FixedString.Format("Fish Position: ({0}, {1}, {2})", localToWorld.Position.x, localToWorld.Position.y, localToWorld.Position.z));
                // Debug.Log(FixedString.Format("Centroid Position: ({0}, {1}, {2})", dynamicSchool.centroid.x, dynamicSchool.centroid.y, dynamicSchool.centroid.z));
                // Debug.Log(FixedString.Format("Distance: {0}", distanceFromCentroid));
                
                // if fish is too far, add to the queue
                if (distanceFromCentroid > strayDistance)
                {
                    if (splitting)
                    {
                        // Debug.Log("A fish is already separing this frame");
                        return;
                    } 

                    splitting = true;
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

            Debug.Log(FixedString.Format("Fish strayed - created new school {0}", newSchoolID));



            

        }
    }
}