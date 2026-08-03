using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Profiling;
using Unity.Physics;

//These are used to enable test things like Debug.Log()
using UnityEngine;
using Unity.Rendering;

//Summary :: Once the SchoolSpawner controller entity updates, it spawns its corresponding fish,
//  afterwards, this entity is deleted so that it doesn't spawn again
namespace DCR2
{
    // RequireMatcingQueriesForUpdates :: Skips the OnUpdate system if 
    // there are no entities found in the EntityQueries that you do
    //  Basically, this doens't run OnUpdate until there are entities that match the quesries done in this system (Until we've defined our entity spawner)
    [RequireMatchingQueriesForUpdate]
    [UpdateAfter(typeof(FishSystem))]
    [BurstCompile]
    public partial struct CentroidGizmoSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var centroidQuery = SystemAPI.QueryBuilder().WithAll<centroidGizmoComponent>().WithAll<LocalToWorld>().Build();
            if (centroidQuery.CalculateEntityCount() > 0)
            {
                // get query of gizmo to have each centroidID
                var gizmoQuery = SystemAPI.QueryBuilder().WithAll<centroidGizmo>().Build();
                Debug.Log(FixedString.Format("Gizmo count: {0}", gizmoQuery.CalculateEntityCount()));
                
                

                // query and array of fish entities
                var fishQuery = SystemAPI.QueryBuilder().WithAll<DynamicSchool>().Build();
                NativeArray<Entity> entityArray = fishQuery.ToEntityArray(Allocator.TempJob);

                // quick return if there are no fish
                if (fishQuery.CalculateEntityCount() == 0)
                {
                    return;
                }
                
                //var centroidVal = state.EntityManager.GetComponentData<DynamicSchool>(entityArray[0]).centroid;

                var localToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>();
                //I think this could be done in the SchoolSpawner (instantiating the centroids)
                //Array to store the amount of gizmos
                //TODO :: Right now it only stores 1 entity, it should store the same amount as the amount of schoolSpawners

                //Instantiate the sphere prefab and put it in the centroidGizmoArray
                //state.EntityManager.Instantiate(centerGizmo.ValueRO.spherePrefab);
                //Debug.Log("In Onpudate");
                NativeArray<Entity> centroidEntityArray = centroidQuery.ToEntityArray(Allocator.TempJob);

                

                // attempt 2 of getting id of fish in different schools
                var world = state.WorldUnmanaged;
                NativeArray<Entity> fishEntities = fishQuery.ToEntityArray(Allocator.Temp);
                int centroidCount = centroidQuery.CalculateEntityCount();
                NativeArray<int> uniqueFishID = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(centroidCount, ref world.UpdateAllocator);
                int id = -1;
                int i = 0;
                foreach (var entity in fishEntities)
                {
                    SemiStaticSchool school = state.EntityManager.GetSharedComponentManaged<SemiStaticSchool>(entity);
                    //Debug.Log(FixedString.Format("Entity {1} schoolID: {0}", school.schoolID, i));
                    if (id != school.schoolID)
                    {
                        id = school.schoolID;
                        uniqueFishID[id] = i;
                    }
                    i++;
                }

                
                // Put the entity on the position of the centroid of one of the schools
                // This code is what makes the gizmo move
                using NativeArray<centroidGizmo> gizmos = gizmoQuery.ToComponentDataArray<centroidGizmo>(Allocator.TempJob);
                for (int g = 0; g < centroidCount; g++)
                {
                    int x = uniqueFishID[g];
                    //Debug.Log(FixedString.Format("uniqueFish ID: {0}", x));
                    var centroidVal = state.EntityManager.GetComponentData<DynamicSchool>(entityArray[x]).centroid;
                    //Debug.Log(FixedString.Format("{0}, {1}, {2}", centroidVal.x, centroidVal.y, centroidVal.z));
                    var localToWorld = new LocalToWorld
                            {
                                Value = float4x4.TRS(centroidVal, quaternion.LookRotationSafe(new float3(0f,0f,0f), math.up()), new float3(1.0f, 1.0f, 1.0f))
                               // Value = float4x4.TRS(centroidVal, quaternion.LookRotationSafe(new float3(0f,0f,0f), math.up()), new float3(10.0f, 10.0f, 10.0f))
                            };
                    localToWorldLookup[centroidEntityArray[g]] = localToWorld;


                    

                }

                // // // drawing a raycast
                // if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physicsWorld)) return;

                // // 2. Define the ray inputs
                // float3 rayStart = new float3(0, 50, 0);
                // float3 rayEnd = new float3(0, -50, 0);

                // RaycastInput input = new RaycastInput
                // {
                //     Start = rayStart,
                //     End = rayEnd,
                //     Filter = CollisionFilter.Default
                // };

                // // 3. Cast the ray
                // if (physicsWorld.CastRay(input, out Unity.Physics.RaycastHit hit))
                // {
                //     // Draw a green line to the point of impact
                //     Debug.DrawLine(input.Start, hit.Position, Color.green);
                //     Debug.Log("Drawing raycast");
                // }

                // // Enforce safe thread-execution via entities.ForEach or IJobEntity
                // SystemAPI.Query<RefRO<LocalTransform>>()
                // .ForEach((in LocalTransform transform) =>
                // {
                //     float3 start = transform.Position;
                //     float3 end = transform.Position + (transform.Forward() * 2.0f);
                    
                //     // Works perfectly inside Burst jobs!
                //     Debug.DrawLine(start, end, Color.green); 
                // });
                
            }
        }

    }
}

