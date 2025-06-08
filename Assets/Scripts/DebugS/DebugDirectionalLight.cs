using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[RequireComponent(typeof(Light))]
public class DebugDirectionalLight : MonoBehaviour
{
    Light directionalLight;

    void Start()
    {
        directionalLight = GetComponent<Light>();
        if (directionalLight.type != LightType.Directional)
        {
            Debug.LogError("This script requires a Directional Light component.");
        }
    }

    [DebugCallable]
    public void SetLightOnOff(bool isOn)
    {
        if (directionalLight != null)
        {
            directionalLight.enabled = isOn;
        }
        else
        {
            Debug.LogError("Directional Light component not found.");
        }
    }
}
