using System;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using Unity.Mathematics;


namespace DCR2
{
    public class SchoolManagerAuthoring : MonoBehaviour
    {
        

        public GameObject centroidPrefab;

        class Baker : Baker<SchoolManagerAuthoring>
        {
            public override void Bake(SchoolManagerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new SchoolManagerSingleton
                {
                    schoolCount = 0,
                    nextSchoolID = 0,
                    centroidPrefab = GetEntity(authoring.centroidPrefab, TransformUsageFlags.Dynamic)
                });
            }
        }
    }

    public struct SchoolManagerSingleton : IComponentData
    {
        public int schoolCount;   // how many schools currently exist
        public int nextSchoolID;  // next free ID to hand out (always increases, never reused,
                                // so merged/removed IDs don't collide with new ones)
        public Entity centroidPrefab;
    }

    // Lives on ONE entity PER SCHOOL
    public struct SchoolRecord : IComponentData
    {
        public int schoolID;
        public float3 centroid;   // last known centroid (mirrors DynamicSchool.centroid on members)
        public int memberCount;   // cached length of the buffer below, kept in sync manually
    }

    // Buffer attached to the SAME entity as SchoolRecord.
    // Each element is one fish that belongs to this school.
    public struct SchoolMemberElement : IBufferElementData
    {
        public Entity FishEntity;
    }
}