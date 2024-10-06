using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerLook : NetworkBehaviour
{
    private PlayerMotor motor;
    private Vector3 standingPosition;
    private Vector3 crouchingPosition;
    private Vector3 slidingPosition;



    public Camera cam;
    private float xRotation = 0f;
    public float xSensitivity = 30f;
    public float ySensitivity = 30f;

    
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        if (!IsOwner) {
            cam.gameObject.SetActive(false);
        }

        motor = GetComponent<PlayerMotor>();
        standingPosition = cam.transform.localPosition;
        crouchingPosition = new Vector3(standingPosition.x, standingPosition.y - 0.7f, standingPosition.z);
        slidingPosition = new Vector3(standingPosition.x, standingPosition.y - 1f, standingPosition.z);
    }

    void Update() {
        
        Vector3 targetHeight;
        if (motor.sliding) {
            targetHeight = slidingPosition;
        } else if (motor.crouching) {
            targetHeight = crouchingPosition;
        } else {
            targetHeight = standingPosition;
        }
        cam.transform.localPosition = Vector3.Lerp(cam.transform.localPosition, targetHeight, Time.deltaTime * 5f);
    }


    public void ProcessLook(Vector2 input)
    {
        float mouseX = input.x;
        float mouseY = input.y;

        // calculate camera rotation
        xRotation -= (mouseY * Time.deltaTime) * ySensitivity;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        // apply to camera transform
        cam.transform.localRotation = Quaternion.Euler(xRotation, 0 , 0);
        // rotate player to look left and right
        transform.Rotate(Vector3.up * (mouseX * Time.deltaTime) * xSensitivity);
    }
}
