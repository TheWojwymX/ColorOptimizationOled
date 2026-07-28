using UnityEngine;
using UnityEngine.InputSystem;

public class CameraMove : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float lookSpeed = 150f;

    float pitch;
    float yaw;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        // Mouse look
        Vector2 mouse = Mouse.current.delta.ReadValue();

        yaw += mouse.x * lookSpeed * Time.deltaTime;
        pitch -= mouse.y * lookSpeed * Time.deltaTime;
        pitch = Mathf.Clamp(pitch, -90f, 90f);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0);

        // Movement
        Vector3 direction = Vector3.zero;

        if (Keyboard.current.wKey.isPressed)
            direction += transform.forward;

        if (Keyboard.current.sKey.isPressed)
            direction -= transform.forward;

        if (Keyboard.current.aKey.isPressed)
            direction -= transform.right;

        if (Keyboard.current.dKey.isPressed)
            direction += transform.right;

        if (Keyboard.current.eKey.isPressed)
            direction += transform.up;

        if (Keyboard.current.qKey.isPressed)
            direction -= transform.up;

        float speed = moveSpeed;

        if (Keyboard.current.leftShiftKey.isPressed)
            speed *= 2f;

        transform.position += direction.normalized * speed * Time.deltaTime;
    }
}