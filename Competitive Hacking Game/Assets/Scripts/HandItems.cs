using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class HandItems : MonoBehaviour
{

    public GameObject phone;
    public RigBuilder rigBuilder;

    bool isEquipped = false;


    public void Equip(int itemId) {

        isEquipped = !isEquipped;

        phone.SetActive(isEquipped);
        rigBuilder.enabled = isEquipped;
    }
}
