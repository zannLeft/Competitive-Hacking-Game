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

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        if (!IsOwner) {
            cam.gameObject.SetActive(false);
        }

        motor = GetComponent<PlayerMotor>();
        standingPosition = cam.transform.localPosition;
        crouchingPosition = new Vector3(standingPosition.x, standingPosition.y - 1.1f, standingPosition.z);
        slidingPosition = new Vector3(standingPosition.x, standingPosition.y - 1.3f, standingPosition.z);

        // Set the default FOV
        cam.fieldOfView = defaultFOV;
    }

    void Update() {
        Vector3 targetHeight;

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
        float targetFOV = (motor.sprinting || motor.sliding) ? sprintFOV : defaultFOV;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, Time.deltaTime * fovTransitionSpeed);

        // Log the sprinting status (for debugging purposes)
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
