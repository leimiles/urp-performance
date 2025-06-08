using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class Test_Attack : MonoBehaviour
{

    [DebugCallable]
    public void Attack()
    {
        Debug.Log("Attack Invoked");
    }

    [DebugCallable]
    public void Attack(int damage)
    {
        Debug.Log($"Attack Invoked with damage: {damage}");
    }

    [DebugCallable]
    public void Attack(int damage, float time)
    {
        Debug.Log($"Attack Invoked with damage: {damage} and time: {time}");
    }

    [DebugCallable]
    public void Defend()
    {
        Debug.Log("Defend Invoked");
    }

    [DebugCallable]
    public void Fun(Test_Attack test)
    {
        Debug.Log("Fun Invoked");
    }
}
