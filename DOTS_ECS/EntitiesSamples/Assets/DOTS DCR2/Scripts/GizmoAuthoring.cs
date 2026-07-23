using System;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using Unity.Mathematics;

namespace DCR2
{
    public class GizmoAuthoring : MonoBehaviour
    {
        public GameObject centroidPrefab;
        public int showCentroid;
        public Color centroidColor = Color.white;
        public int centroidID;
        public int test_p;
        class Baker : Baker<GizmoAuthoring>
        {
            // Function to bake the entity that contains the BoidSchool component
            // authoring :: This is the value that has the inputs that the user gave 
            public override void Bake(GizmoAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Renderable);
                
                if (authoring.showCentroid == 1)
                {
                    AddComponent(entity, new centroidGizmo
                    {
                        centroidPrefab = GetEntity(authoring.centroidPrefab, TransformUsageFlags.Dynamic),
                        test = authoring.test_p,
                        centroidColor = new float4(authoring.centroidColor.r,
                                                    authoring.centroidColor.g,
                                                    authoring.centroidColor.b,
                                                    authoring.centroidColor.a)
                    });
                }
            }
        }
    }

    
    //Define each of the components that apply to the fish
    //semiStaticSchool :: Store class caracteristics that don't change often during run time
    //  The reason for that is because for ISharedComponentData its not advised to put values that will change often
    //  that is because it will affect the location where each fish of that class is going to be stored, which will take time processing, and if its done ofthen then that time will stack up
    //
    [Serializable]
    [WriteGroup(typeof(LocalToWorld))]
    public struct centroidGizmo : IComponentData
    {
        public Entity centroidPrefab;
        public float4 centroidColor;
        public int centroidID;
        public int test;
    }
}

