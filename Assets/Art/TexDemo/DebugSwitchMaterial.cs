using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(MeshRenderer))]
public class DebugSwitchMaterial : MonoBehaviour
{
    MeshRenderer meshRenderer;
    [SerializeField] public Material[] materials;
    int materialCount = 0;
    int currentMaterialIndex = 0;

    void Start()
    {
        if (materials == null || materials.Length == 0)
        {
            Debug.LogError("No materials assigned to DebugSwitchMaterial.");
            return;
        }
        else
        {
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                Debug.LogError("MeshRenderer component not found.");
            }
        }
        materialCount = materials.Length;
        meshRenderer.sharedMaterial = materials[currentMaterialIndex];
        currentMaterialIndex++;
    }


    [DebugCallable]
    public void SwitchMaterialLoop()
    {
        if (materials == null || materials.Length == 0 || meshRenderer == null)
            return;

        meshRenderer.sharedMaterial = materials[currentMaterialIndex];

        currentMaterialIndex = (currentMaterialIndex + 1) % materials.Length;
    }
}
