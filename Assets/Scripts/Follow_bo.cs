using UnityEngine;
using UnityEngine.InputSystem;

public class Follow_bo : MonoBehaviour
{
    [Header("References")]
    public GameObject player;        // Bo
    public Camera cam;               // Main camera

    [Header("Follow Settings")]
    public float followSmooth = 0.2f; // Smoothing for following Bo
    public Vector2 externalOffset;    // Optional offset from other systems

    [Header("Camera Shift Settings")]
    [Tooltip("World units to shift the camera when right stick is fully pushed.")]
    public float cameraShiftAmount = 2f;
    public float cameraShiftSpeed = 8f;

    [Header("Zoom Settings")]
    public float zoomOutSize = 12f;
    public float zoomSpeed = 5f; // how quickly zoom changes
    private float defaultSize;
    private float targetZoom; // current target zoom
    private bool zoomedOut = false;

    [Header("Look Ahead Settings")]
    [Tooltip("How far ahead (in seconds) to anticipate player movement. Higher values show more ahead.")]
    public float lookAheadFactor = 0.5f; // Adjust as needed
    [Tooltip("Maximum look-ahead distance (in world units) to prevent excessive offset.")]
    public float maxLookAhead = 3f; // Adjust as needed

    // Internal
    private Rigidbody2D rb;
    private Vector2 stickInput = Vector2.zero;
    private PlayerInputActions inputActions;

    [Header("Camera Inversion (Toggle if directions feel swapped)")]
    [SerializeField] private bool invertHorizontal = false;  // Toggle if left/right feels swapped
    [SerializeField] private bool invertVertical = true;    // Toggle if up/down feels swapped

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (cam != null)
        {
            defaultSize = cam.orthographicSize;
            targetZoom = defaultSize;
        }

        inputActions = new PlayerInputActions();
    }

    void OnEnable()
    {
        rb = player.GetComponent<Rigidbody2D>();

        inputActions.Player.Enable();

        // Camera move
        inputActions.Player.CameraHorizontal.performed += OnCameraHorizontalPerformed;
        inputActions.Player.CameraHorizontal.canceled += OnCameraHorizontalCanceled;
        inputActions.Player.CameraVertical.performed += OnCameraVerticalPerformed;
        inputActions.Player.CameraVertical.canceled += OnCameraVerticalCanceled;

        // Zoom
        inputActions.Player.CameraZoom.performed += OnCameraZoomPerformed;

        // Cancel zoom on any main player action
        inputActions.Player.Move.performed += CancelZoomOnAnyAction;
        inputActions.Player.Jump.performed += CancelZoomOnAnyAction;
        inputActions.Player.Puff.performed += CancelZoomOnAnyAction;
        inputActions.Player.Flatten.performed += CancelZoomOnAnyAction;
        inputActions.Player.Roll.performed += CancelZoomOnAnyAction;
        inputActions.Player.Teleport.performed += CancelZoomOnAnyAction;
    }

    void OnDisable()
    {
        inputActions.Player.CameraHorizontal.performed -= OnCameraHorizontalPerformed;
        inputActions.Player.CameraHorizontal.canceled -= OnCameraHorizontalCanceled;
        inputActions.Player.CameraVertical.performed += OnCameraVerticalPerformed;
        inputActions.Player.CameraVertical.canceled += OnCameraVerticalCanceled;

        inputActions.Player.CameraZoom.performed -= OnCameraZoomPerformed;

        inputActions.Player.Move.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Jump.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Puff.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Flatten.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Roll.performed -= CancelZoomOnAnyAction;
        inputActions.Player.Teleport.performed -= CancelZoomOnAnyAction;

        inputActions.Player.Disable();
    }

    private void OnCameraHorizontalPerformed(InputAction.CallbackContext ctx)
    {
        float value = ctx.ReadValue<float>();
        stickInput.x = invertHorizontal ? -value : value;
    }

    private void OnCameraHorizontalCanceled(InputAction.CallbackContext ctx)
    {
        stickInput.x = 0f;
    }

    private void OnCameraVerticalPerformed(InputAction.CallbackContext ctx)
    {
        float value = ctx.ReadValue<float>();
        stickInput.y = invertVertical ? -value : value;
    }

    private void OnCameraVerticalCanceled(InputAction.CallbackContext ctx)
    {
        stickInput.y = 0f;
    }

    private void OnCameraZoomPerformed(InputAction.CallbackContext ctx)
    {
        if (cam == null) return;
        targetZoom = zoomOutSize;
        zoomedOut = true;
    }

    private void CancelZoomOnAnyAction(InputAction.CallbackContext ctx)
    {
        if (!zoomedOut || cam == null) return;
        targetZoom = defaultSize;
        zoomedOut = false;
    }

    void LateUpdate()
    {
        if (player == null) return;

        // Smooth zoom transition
        if (cam != null)
        {
            cam.orthographicSize = Mathf.Lerp(
                cam.orthographicSize,
                targetZoom,
                Time.deltaTime * zoomSpeed
            );
        }

        // Calculate look-ahead offset based on player's velocity
        Vector2 lookAheadOffset = rb.linearVelocity * lookAheadFactor;
        lookAheadOffset = Vector2.ClampMagnitude(lookAheadOffset, maxLookAhead);

        // Base follow target with look-ahead
        float targetX = player.transform.position.x + externalOffset.x + lookAheadOffset.x;
        float targetY = player.transform.position.y + externalOffset.y + lookAheadOffset.y;

        // Smooth follow
        float smoothX = Mathf.Lerp(transform.position.x, targetX, followSmooth);
        float smoothY = Mathf.Lerp(transform.position.y, targetY, followSmooth);
        Vector3 basePos = new Vector3(smoothX, smoothY, -1);

        // Add right stick shift
        Vector3 desiredOffset = new Vector3(stickInput.x * cameraShiftAmount, stickInput.y * cameraShiftAmount, 0f);
        Vector3 desired = basePos + desiredOffset;

        // Smooth toward final position
        transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * cameraShiftSpeed);
    }
}