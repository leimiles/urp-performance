using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class Test_Attack : MonoBehaviour
{

    public void Attack()
    {
        Debug.Log("Attack Invoked");
    }

    public void Attack(int damage)
    {
        Debug.Log($"Attack Invoked with damage: {damage}");
    }

    public void Attack(int damage, float time)
    {
        Debug.Log($"Attack Invoked with damage: {damage} and time: {time}");
    }

    public void Defend()
    {
        Debug.Log("Defend Invoked");
    }
}
