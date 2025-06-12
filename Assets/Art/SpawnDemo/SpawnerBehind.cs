using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpawnerBehind : MonoBehaviour
{
    [SerializeField] Vector3 spawnDirection = Vector3.back;
    [SerializeField] PrimitiveType primitiveType = PrimitiveType.Cube;
    [SerializeField] Material material;

    private List<GameObject> spawnedObjects = new List<GameObject>();

    [DebugCallable]
    public void SpawnPrimitives(float distance = 10.0f, int count = 5)
    {
        RemovePrimitives(); // 先清理已生成的对象
        Vector3 startPos = transform.position;
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = startPos + spawnDirection.normalized * distance * (i + 1);
            GameObject obj = GameObject.CreatePrimitive(primitiveType);
            obj.transform.position = pos;

            // 使用指定材质
            if (material != null)
            {
                var renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.sharedMaterial = material;
            }

            spawnedObjects.Add(obj);
        }
    }

    [DebugCallable]
    public void RemovePrimitives()
    {
        foreach (var obj in spawnedObjects)
        {
            if (obj != null)
                Destroy(obj);
        }
        spawnedObjects.Clear();
    }

}
