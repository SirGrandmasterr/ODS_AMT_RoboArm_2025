using UnityEngine;
using UnityEngine.InputSystem;

public class TargetController : MonoBehaviour
{
    float speed = 10.0f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (Keyboard.current == null) return;

        Vector3 move = Vector3.zero;
        if (Keyboard.current.upArrowKey.isPressed) move.z += 1f;
        if (Keyboard.current.downArrowKey.isPressed) move.z -= 1f;
        if (Keyboard.current.leftArrowKey.isPressed) move.x -= 1f;
        if (Keyboard.current.rightArrowKey.isPressed) move.x += 1f;
        if (Keyboard.current.wKey.isPressed) move.y += 1f;
        if (Keyboard.current.sKey.isPressed) move.y -= 1f;

        if (move != Vector3.zero)
        {
            // normalize so diagonal movement isn't faster
            Vector3 delta = new Vector3(move.x, move.y, move.z).normalized * speed * Time.deltaTime;
            transform.position += delta;
        }
    }
}
