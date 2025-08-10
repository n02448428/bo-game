using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(100)] // run after most Update() scripts (Follow_bo runs in Update)
public class CameraOffsetZoom : MonoBehaviour
{
    public Camera cam; // assign main orthographic camera
    [Tooltip("World units to shift the camera when right stick is fully pushed.")]
    public float cameraShiftAmount = 2f;
    public float cameraShiftSpeed = 8f;
    public float zoomOutSize = 12f;

    private Vector2 stickInput = Vector2.zero;
    private Vector2 stickOffset = Vector2.zero;
    private float defaultSize;
    private bool zoomedOut = false;
    private PlayerInputActions inputActions;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) Debug.LogWarning("CameraOffsetZoom: No camera assigned and no Camera.main found.");
        defaultSize = cam != null ? cam.orthographicSize : 5f;
        inputActions = new PlayerInputActions();
    }

    void OnEnable()
    {
        inputActions.Player.Enable();

        inputActions.Player.CameraMove.performed += OnCameraMovePerformed;
        inputActions.Player.CameraMove.canceled += OnCameraMoveCanceled;

        inputActions.Player.CameraZoom.performed += OnCameraZoomPerformed;

        // Any main player action cancels zoom automatically
        inputActions.Player.Move.performed += CancelZoomOnAnyAction;
        inputActions.Player.Jump.performed += CancelZoomOnAnyAction;
        inputActions.Player.Puff.performed += CancelZoomOnAnyAction;
        inputActions.Player.Flatten.performed += CancelZoomOnAnyAction;
        inputActions.Player.Roll.performed += CancelZoomOnAnyAction;
        inputActions.Player.Teleport.performed += CancelZoomOnAnyAction;
    }

    void OnDisable()
    {
        inputActions.Player.CameraMove.performed -= OnCameraMovePerformed;
        inputActions.Player.CameraMove.canceled -= OnCameraMoveCanceled;

        inputActions.Player.CameraZoom.performed -= OnCameraZoomPerformed;

        inputActions.Player.Move.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Jump.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Puff.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Flatten.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Roll.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Teleport.performed -= CancelZoomOnAnyAction;

        inputActions.Player.Disable();
    }

    // Robust camera movement read: accepts Vector2 or single float controls (z/rz)
    private void OnCameraMovePerformed(InputAction.CallbackContext ctx)
    {
        object raw = ctx.ReadValueAsObject();
        Vector2 v = Vector2.zero;

        if (raw is Vector2 vv) v = vv;
        else if (raw is float f)
        {
            string path = ctx.control?.path?.ToLower() ?? "";
            if (path.Contains("/z")) v.x = f;
            else if (path.Contains("/rz")) v.y = f;
            else v.x = f;
        }
        else
        {
            v = inputActions.Player.CameraMove.ReadValue<Vector2>();
        }

        // invert Y if controller returns inverted Y for Rz (tweak as needed)
        stickInput = v;
    }

    private void OnCameraMoveCanceled(InputAction.CallbackContext ctx)
    {
        stickInput = Vector2.zero;
    }

    private void OnCameraZoomPerformed(InputAction.CallbackContext ctx)
    {
        if (cam == null) return;
        cam.orthographicSize = zoomOutSize;
        zoomedOut = true;
    }

    private void CancelZoomOnAnyAction(InputAction.CallbackContext ctx)
    {
        if (!zoomedOut || cam == null) return;
        cam.orthographicSize = defaultSize;
        zoomedOut = false;
    }

    void LateUpdate()
    {
        // base position is whatever Follow_bo left this frame (Follow_bo runs in Update)
        Vector3 basePos = transform.position;

        // compute offset in world units (scale by cameraShiftAmount)
        Vector3 desiredOffset = new Vector3(stickInput.x * cameraShiftAmount, stickInput.y * cameraShiftAmount, 0f);

        // apply smoothing toward basePos + desiredOffset
        Vector3 desired = basePos + desiredOffset;
        transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * cameraShiftSpeed);

        // (zoom handled by input callbacks)
    }
}
