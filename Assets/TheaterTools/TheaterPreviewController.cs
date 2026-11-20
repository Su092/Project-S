using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
public sealed class TheaterPreviewController : MonoBehaviour
{
    public Transform view;
    public int shotMask;
    public int shotOnlyMask;
    public float mouseDegreesPerPixel = .095f;
    public float walkMetersPerSecond = 5f;

    CharacterController controller;
    Transform impact;
    float pitch;
    float verticalSpeed;
    float markerUntil;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        controller.excludeLayers |= shotOnlyMask;
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(marker.GetComponent<Collider>());
        marker.name = "QA shot impact";
        marker.transform.localScale = Vector3.one * .10f;
        marker.SetActive(false);
        impact = marker.transform;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        Vector2 travel = Vector2.zero, look = Vector2.zero;
        bool escape = false, trigger = false;
#if ENABLE_INPUT_SYSTEM
        var keys = Keyboard.current;
        var mouse = Mouse.current;
        if (keys != null)
        {
            travel = new Vector2((keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0),
                                (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0));
            escape = keys.escapeKey.wasPressedThisFrame;
        }
        if (mouse != null)
        {
            look = mouse.delta.ReadValue() * mouseDegreesPerPixel;
            trigger = mouse.leftButton.wasPressedThisFrame;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        travel = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 2;
        escape = Input.GetKeyDown(KeyCode.Escape);
        trigger = Input.GetMouseButtonDown(0);
#endif
        if (escape) Cursor.lockState = CursorLockMode.None;
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (trigger) Cursor.lockState = CursorLockMode.Locked;
            return;
        }

        pitch = Mathf.Clamp(pitch - look.y, -82, 82);
        transform.Rotate(0, look.x, 0, Space.World);
        view.localRotation = Quaternion.Euler(pitch, 0, 0);
        var input = Vector2.ClampMagnitude(travel, 1);
        var planar = transform.right * input.x + transform.forward * input.y;
        verticalSpeed = controller.isGrounded ? -2.3f : Mathf.Max(-20, verticalSpeed - 18 * Time.deltaTime);
        controller.Move((planar * walkMetersPerSecond + Vector3.up * verticalSpeed) * Time.deltaTime);

        if (trigger && Physics.Raycast(view.position, view.forward, out var hit, 70, shotMask, QueryTriggerInteraction.Ignore))
        {
            impact.position = hit.point + hit.normal * .044f;
            impact.gameObject.SetActive(true);
            markerUntil = Time.unscaledTime + .30f;
        }
        if (impact.gameObject.activeSelf && Time.unscaledTime >= markerUntil)
            impact.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (impact) Destroy(impact.gameObject);
        Cursor.lockState = CursorLockMode.None;
    }
}
