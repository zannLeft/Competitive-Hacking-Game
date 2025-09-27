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

    // FOV settings
    private float defaultFOV = 90f;
    private float sprintFOV = 100f;  // Adjust this value to your preference
    private float fovTransitionSpeed = 5f;

    //public Transform phone;

    // Variables to introduce smooth delay in rotation
    //private Quaternion targetPhoneRotation;
    private float targetYaw; // Delayed yaw for smoothness

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        if (!IsOwner) {
            cam.gameObject.SetActive(false);
        }

        motor = GetComponent<PlayerMotor>();
        standingPosition = cam.transform.localPosition;
        crouchingPosition = new Vector3(standingPosition.x, standingPosition.y - 1.1f, standingPosition.z);
        slidingPosition = new Vector3(standingPosition.x, standingPosition.y - 1.1f, standingPosition.z - 0.5f);

        // Set the default FOV
        cam.fieldOfView = defaultFOV;

        // Initialize target yaw to match player's starting yaw
        targetYaw = transform.eulerAngles.y;
        //targetPhoneRotation = cam.transform.localRotation;
    }

    void Update()
    {
        Vector3 targetHeight;

        if (motor.sliding) {
            if (xRotation > 0f) {
                xRotation = Mathf.Lerp(xRotation, 0f, Time.deltaTime * 5f);
            }
        }

        // Handle camera position based on player state
        if (motor.sliding) {
            targetHeight = slidingPosition;
        } else if (motor.crouching) {
            targetHeight = crouchingPosition;
        } else {
            targetHeight = standingPosition;
        }
        cam.transform.localPosition = Vector3.Lerp(cam.transform.localPosition, targetHeight, Time.deltaTime * 7f);

        // Smooth FOV transition between sprinting and non-sprinting
        float targetFOV = ((motor.sprinting && motor.inputDirection.y > 0) || motor.sliding) ? sprintFOV : defaultFOV;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, Time.deltaTime * fovTransitionSpeed);
    }

    public void ProcessLook(Vector2 input)
    {
        float mouseX = input.x;
        float mouseY = input.y;

        // Calculate camera rotation (pitch)
        if (motor.sliding && xRotation > 0) {
            if (mouseY > 0) {
                xRotation -= mouseY * Time.deltaTime * ySensitivity;
            }
        } else {
            xRotation -= mouseY * Time.deltaTime * ySensitivity;
        }
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        // Apply vertical rotation to the camera
        cam.transform.localRotation = Quaternion.Euler(xRotation, 0, 0);

        // Update player's rotation (yaw)
        transform.Rotate(Vector3.up * (mouseX * Time.deltaTime) * xSensitivity);

        // Smoothly update the target yaw to create a delayed effect
        targetYaw = Mathf.LerpAngle(targetYaw, transform.eulerAngles.y, Time.deltaTime * 8f); // Adjust "5f" for delay smoothness

        // Determine the target rotation for the phone using both delayed yaw and camera pitch
        //targetPhoneRotation = Quaternion.Euler(xRotation, targetYaw, 0);

        // Apply consistent smoothness to all axes
        //phone.rotation = Quaternion.Slerp(phone.rotation, targetPhoneRotation, Time.deltaTime * 8f);  // Adjust "5f" for desired smoothness
    }
}
