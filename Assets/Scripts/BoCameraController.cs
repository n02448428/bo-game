using UnityEngine;
using UnityEngine.InputSystem;

public class BoCameraController : MonoBehaviour
{
    public Transform target; // assign main character
    public Camera cam;       // orthographic camera
    public float cameraShiftAmount = 2f;
    public float cameraShiftSpeed = 8f;
    public float zoomOutSize = 12f;

    private Vector2 stickOffset = Vector2.zero;
    private float defaultSize;
    private bool zoomedOut = false;
    private PlayerInputActions inputActions;

    void Awake()
    {
        inputActions = new PlayerInputActions();
        defaultSize = cam.orthographicSize;
    }

    void OnEnable()
    {
        inputActions.Player.Enable();

        // Right stick movement (CameraMove)
        inputActions.Player.CameraMove.performed += ctx => stickOffset = ctx.ReadValue<Vector2>() * cameraShiftAmount;
        inputActions.Player.CameraMove.canceled += ctx => stickOffset = Vector2.zero;

        // Zoom (R3 press)
        inputActions.Player.CameraZoom.performed += ctx => { ZoomOut(); };

        // Subscribe to a set of Player actions that should cancel zoom automatically.
        // When any of these actions fire, we restore camera if it was zoomed.
        inputActions.Player.Move.performed += CancelZoomOnAnyAction;
        inputActions.Player.Jump.performed += CancelZoomOnAnyAction;
        inputActions.Player.Puff.performed += CancelZoomOnAnyAction;
        inputActions.Player.Flatten.performed += CancelZoomOnAnyAction;
        inputActions.Player.Roll.performed += CancelZoomOnAnyAction;
        inputActions.Player.Teleport.performed += CancelZoomOnAnyAction;
    }

    void OnDisable()
    {
        inputActions.Player.Move.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Jump.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Puff.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Flatten.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Roll.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Teleport.performed -= CancelZoomOnAnyAction;

        inputActions.Player.CameraMove.performed -= ctx => stickOffset = ctx.ReadValue<Vector2>() * cameraShiftAmount;
        inputActions.Player.CameraMove.canceled -= ctx => stickOffset = Vector2.zero;
        inputActions.Player.CameraZoom.performed -= ctx => { ZoomOut(); };

        inputActions.Player.Disable();
    }

    void LateUpdate()
    {
        if (target == null || cam == null) return;

        Vector3 desired = target.position + (Vector3)stickOffset;
        cam.transform.position = Vector3.Lerp(cam.transform.position,
            new Vector3(desired.x, desired.y, cam.transform.position.z),
            Time.deltaTime * cameraShiftSpeed);
    }

    private void ZoomOut()
    {
        cam.orthographicSize = zoomOutSize;
        zoomedOut = true;
    }

    private void CancelZoomOnAnyAction(InputAction.CallbackContext ctx)
    {
        if (zoomedOut)
        {
            cam.orthographicSize = defaultSize;
            zoomedOut = false;
        }
    }
}
