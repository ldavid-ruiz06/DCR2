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
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateAfter(typeof(FishSystem))] // needs up-to-date centroids/positions from this frame
    [UpdateAfter(typeof(CentroidGizmoSystem))] 
    public partial struct SchoolManagerSystem : ISystem
    {

        public float strayDistance;
        public float mergeDistance;
        public bool splitting;

        //[BurstCompile]

        public void OnUpdate(ref SystemState state)
        {
            // make sure singleton exists before running anything
            if (!SystemAPI.HasSingleton<SchoolManagerSingleton>()) return;

            // Managers and EntityCommandBuffer
            var manager = SystemAPI.GetSingleton<SchoolManagerSingleton>();
            var managerEntity = SystemAPI.GetSingletonEntity<SchoolManagerSingleton>();
            var managerRW = SystemAPI.GetComponentRW<SchoolManagerSingleton>(managerEntity);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            
            // set count of school and next free id
            manager.schoolCount = SystemAPI.QueryBuilder().WithAll<SchoolRecord>().Build().CalculateEntityCount();
            manager.nextSchoolID = manager.schoolCount;
            
    
            // check for stray fish
            strayDistance = 20.0f;
            splitting = false;
            var straySplitRequestQueue = new NativeQueue<StraySplitRequest>(Allocator.TempJob); // we use a queue so we can easily add and remove elements
            var detectStrayJob = new DetectStrayFishJob
            {
                strayDistance = strayDistance,
                strayRequestWriter = straySplitRequestQueue.AsParallelWriter(),
                splitting = splitting,
            };

            state.Dependency = detectStrayJob.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();

            // if theres fish in the queue, split into new school
            while (straySplitRequestQueue.TryDequeue(out StraySplitRequest request))
            {
                SplitFishIntoNewSchool(ref state, ecb, request, ref managerRW.ValueRW);
            }
            straySplitRequestQueue.Dispose();

            



            // merging schools
            mergeDistance = 15.0f; // max distance between centroids for merging
            var schoolQuery = SystemAPI.QueryBuilder().WithAll<SchoolRecord>().Build();
            var schoolEntities = schoolQuery.ToEntityArray(Allocator.Temp);
            var schoolRecords = schoolQuery.ToComponentDataArray<SchoolRecord>(Allocator.Temp);
            var dynamicSchoolQuery = SystemAPI.QueryBuilder().WithAll<DynamicSchool>().Build(); // get all centroids
            //Debug.Log(FixedString.Format("Dynamic School Record: {0}", dynamicSchoolQuery.CalculateEntityCount()));
            var dynamicSchoolEntities = dynamicSchoolQuery.ToEntityArray(Allocator.Temp);
            var dynamicRecord = dynamicSchoolQuery.ToComponentDataArray<DynamicSchool>(Allocator.Temp);


            var alreadyMerged = new NativeHashSet<Entity>(schoolEntities.Length, Allocator.Temp);

            

            for (int i = 0; i < schoolEntities.Length; i++)
            {
                // update schoolRecord.centroid to the actual centroid 
                foreach (var (schoolRecord, memberBuffer) in
                             SystemAPI.Query<RefRW<SchoolRecord>, DynamicBuffer<SchoolMemberElement>>())
                {
                    if (memberBuffer.Length == 0) 
                    {
                        continue; // handled separately by the empty-school cleanup
                    }
                    Entity anyFish = memberBuffer[0].FishEntity;
                    if (!state.EntityManager.Exists(anyFish))
                    {
                        continue; // safety guard
                    } 

                    var dynamicSchool = state.EntityManager.GetComponentData<DynamicSchool>(anyFish);
                    schoolRecord.ValueRW.centroid = dynamicSchool.centroid;
                }
                //Debug.Log(FixedString.Format("Centroid value: {0}", schoolRecords[i].centroid.x));


                // see if school is already merged during this frame
                if (alreadyMerged.Contains(schoolEntities[i])) continue;

                // compare with other centroids
                for (int j = i + 1; j < schoolEntities.Length; j++)
                {
                    // see if school is already merged with other school
                    if (alreadyMerged.Contains(schoolEntities[j])) continue;
                    //if (schoolRecords[i].modificationTimer > 0f) continue;

                    // calculate distance between centroids
                    float distance = math.distance(schoolRecords[i].centroid, schoolRecords[j].centroid);
                    // Debug.Log(FixedString.Format("Centroid 1: ({0}, {1}, {2})", schoolRecords[i].centroid.x, dynamicRecord[i].centroid.y, dynamicRecord[i].centroid.z));
                    // Debug.Log(FixedString.Format("Centroid 2: ({0}, {1}, {2})", schoolRecords[j].centroid.x, dynamicRecord[j].centroid.y, dynamicRecord[j].centroid.z));
                    
                    // Debug.Log(FixedString.Format("Distance: {0}", distance));
                    if (distance > mergeDistance) continue;


                    // from this point onward, it is assumed schools are close enough to merge
                    // Debug.Log(FixedString.Format("School {0} and {1} are merging.", i, j));

                    // get school's I settings
                    var keepBuffer = state.EntityManager.GetBuffer<SchoolMemberElement>(schoolEntities[i]);
                    if (keepBuffer.Length == 0) continue; // safety guard
                    // Debug.Log(FixedString.Format("School {0}'s length: {1}", i, keepBuffer.Length));
                    Entity templateFish = keepBuffer[0].FishEntity;
                    var keepSettings = state.EntityManager.GetSharedComponentManaged<SemiStaticSchool>(templateFish);

                    // reassign school J members to school I
                    var removeBuffer = state.EntityManager.GetBuffer<SchoolMemberElement>(schoolEntities[j]);
                    // Debug.Log(FixedString.Format("School {0}'s length: {1}", j, removeBuffer.Length));
                    var mergedBuffer = ecb.SetBuffer<SchoolMemberElement>(schoolEntities[i]);

                    for (int f = 0; f < keepBuffer.Length; f++)
                        mergedBuffer.Add(keepBuffer[f]);

                    for (int f = 0; f < removeBuffer.Length; f++)
                    {
                        Entity fish = removeBuffer[f].FishEntity;
                        ecb.SetSharedComponent(fish, keepSettings);
                        mergedBuffer.Add(removeBuffer[f]);
                    }

                    // update school I record
                    ecb.SetComponent(schoolEntities[i], new SchoolRecord
                    {
                        schoolID = schoolRecords[i].schoolID,
                        centroid = (schoolRecords[i].centroid + schoolRecords[j].centroid) * 0.5f,
                        memberCount = keepBuffer.Length + removeBuffer.Length,
                    });

                    // destroy absorbed school record
                    ecb.DestroyEntity(schoolEntities[j]);

                    manager.schoolCount--;

                    // add merged school J to the set
                    alreadyMerged.Add(schoolEntities[j]);
                    // Debug.Log("Schools successfully merged!");
                }

                
            }

            Debug.Log(FixedString.Format("Amount of schools: {0}", manager.schoolCount));
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        // jobs 
        partial struct DetectStrayFishJob: IJobEntity
        {
            [ReadOnly] public float strayDistance;
            public NativeQueue<StraySplitRequest>.ParallelWriter strayRequestWriter; // start as an empty queue
            public SemiStaticSchool oldSchool;
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
                        oldSchoolSettings = oldSchool, // copy of the blittable shared-component value
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
            
            
            // // subtract from previous SchoolRecord count member
            // var oldSchoolRecord = SystemAPI.GetComponent<SchoolRecord>(GetSchoolRecordEntity(ref state, request.fishEntity));
            // oldSchoolRecord.memberCount--;



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
                memberCount = 1,
            });
            var buffer = ecb.AddBuffer<SchoolMemberElement>(newSchoolEntity);
            buffer.Add(new SchoolMemberElement { FishEntity = request.fishEntity });

            // set dynamic centroid to fish position
            ecb.SetComponent(request.fishEntity, new DynamicSchool
            {
                centroid = request.fishPosition,
            });

            // Debug.Log(FixedString.Format("Fish strayed - created new school {0}", newSchoolID));
            

        }

        // returns the school's SchoolRecord Component given a fishEntity
        // AKA fish -> SchoolRecordComponent of fish's school
        private Entity GetSchoolRecordEntity(ref SystemState state, Entity fishEntity)
        {
            // Read this fish's current schoolID off its shared component.
            var fishSchoolSettings = state.EntityManager.GetSharedComponentManaged<SemiStaticSchool>(fishEntity);
            int schoolID = fishSchoolSettings.schoolID;

            // Search all SchoolRecord entities for the one with a matching schoolID.
            // O(n) search
            foreach (var (schoolRecord, schoolEntity) in
                    SystemAPI.Query<RefRO<SchoolRecord>>().WithEntityAccess())
            {
                if (schoolRecord.ValueRO.schoolID == schoolID)
                {
                    return schoolEntity;
                }
            }

            return Entity.Null; // no matching SchoolRecord found
        }
    }
}