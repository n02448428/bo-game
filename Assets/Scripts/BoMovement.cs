using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
public class BoMovement : MonoBehaviour
{
    // References
    private Rigidbody2D rb;
    private SpriteRenderer sr;
    [SerializeField] private Animator _animator;

    // Movement parameters
    [Header("Movement Parameters")]
    public float moveSpeed = 6f;
    public float jumpSpeed = 40f;
    public float jumpTime = 0.1f;

    [Header("Enhanced Jump Settings")]
    [Tooltip("Time window to buffer jump input before landing")]
    public float jumpBufferTime = 0.2f;

    [Tooltip("Gravity multiplier when falling (makes jumps feel snappier)")]
    public float fallGravityMultiplier = 0.5f;

    [Tooltip("Gravity multiplier for short hops when jump is released early")]
    public float lowJumpMultiplier = 4f;

    [Tooltip("Jump cut threshold - minimum velocity to allow jump cutting")]
    public float jumpCutThreshold = 5f;

    // New State System Parameters
    [Header("Puff State Settings")]
    [Tooltip("Gravity multiplier when puffed (floating)")]
    public float puffGravityMultiplier = 0.1f;

    [Header("Flatten State Settings")]
    [Tooltip("Gravity multiplier when flattened (heavy fall)")]
    public float flattenGravityMultiplier = 4f;
    [Tooltip("Speed when crawling while flattened")]
    public float crawlSpeed = 2f;
    [Tooltip("Mini hop speed when jumping while flattened")]
    public float miniHopSpeed = 15f;

    [Header("Super Jump Settings")]
    [Tooltip("Super jump height multiplier")]
    public float superJumpMultiplier = 2.5f;
    [Tooltip("Time window to trigger super jump after state transition")]
    public float superJumpTransitionWindow = 0.3f;

    // Movement state
    public const string RIGHT = "right";
    public const string LEFT = "left";
    private string currentDirection;
    private bool isGrounded = false;
    private bool isJumping = false;
    private bool canMove = true;

    // Enhanced jump state
    private float jumpBufferTimer = 0f;
    private bool jumpWasReleased = false;
    private float originalGravityScale;

    // New State Management
    private bool isPuffed = false;
    private bool isFlattened = false;
    private float lastStateChangeTime = 0f;
    private bool wasPreviouslyFlattened = false;
    private bool isStuckToGround = false;

    // Timer
    private float timer = 0.0f;

    // Input System
    private PlayerInputActions inputActions;
    private Vector2 moveInput;
    private bool jumpPressed;
    private bool jumpConsumed = false;
    private bool rollPressed = false;
    private bool puffHeld = false;
    private bool flattenHeld = false;

    // Events
    public delegate void GroundedEvent(Collision2D collision);
    public event GroundedEvent OnGrounded;

    public delegate void DeadZoneEvent(Collision2D collision);
    public event DeadZoneEvent OnHitDeadZone;

    // Properties
    public bool CanMove
    {
        get { return canMove; }
        set { canMove = value; }
    }

    public bool IsGrounded
    {
        get { return isGrounded; }
    }

    void Awake()
    {
        // Get components
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        originalGravityScale = rb.gravityScale;

        // Initialize Input Actions
        try
        {
            inputActions = new PlayerInputActions();
            Debug.Log("BoMovement: Input Actions initialized successfully");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"BoMovement: Failed to initialize Input Actions - {e.Message}");
        }
    }

    void OnEnable()
    {
        if (inputActions != null)
        {
            inputActions.Player.Enable();
            inputActions.Player.Move.performed += OnMovePerformed;
            inputActions.Player.Move.canceled += OnMoveCanceled;
            inputActions.Player.Jump.performed += OnJumpPerformed;
            inputActions.Player.Jump.canceled += OnJumpCanceled;
            inputActions.Player.Roll.performed += OnRollPerformed;
            inputActions.Player.Roll.canceled += OnRollCanceled;
            inputActions.Player.Puff.performed += OnPuffPerformed;
            inputActions.Player.Puff.canceled += OnPuffCanceled;
            inputActions.Player.Flatten.performed += OnFlattenPerformed;
            inputActions.Player.Flatten.canceled += OnFlattenCanceled;
        }
    }

    void OnDisable()
    {
        if (inputActions != null)
        {
            inputActions.Player.Move.performed -= OnMovePerformed;
            inputActions.Player.Move.canceled -= OnMoveCanceled;
            inputActions.Player.Jump.performed -= OnJumpPerformed;
            inputActions.Player.Jump.canceled -= OnJumpCanceled;
            inputActions.Player.Roll.performed -= OnRollPerformed;
            inputActions.Player.Roll.canceled -= OnRollCanceled;
            inputActions.Player.Puff.performed -= OnPuffPerformed;
            inputActions.Player.Puff.canceled -= OnPuffCanceled;
            inputActions.Player.Flatten.performed -= OnFlattenPerformed;
            inputActions.Player.Flatten.canceled -= OnFlattenCanceled;
            inputActions.Player.Disable();
        }
    }

    private void OnMovePerformed(InputAction.CallbackContext ctx)
    {
        moveInput = ctx.ReadValue<Vector2>();
        _animator.SetBool("isWalking", true);
    }

    private void OnMoveCanceled(InputAction.CallbackContext ctx)
    {
        moveInput = Vector2.zero;
        _animator.SetBool("isWalking", false);
    }

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        jumpPressed = true;
        jumpWasReleased = false;
        jumpBufferTimer = jumpBufferTime;
        jumpConsumed = false;
    }

    private void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        jumpPressed = false;
        jumpWasReleased = true;
    }

    private void OnRollPerformed(InputAction.CallbackContext ctx)
    {
        // Roll only works in normal or puffed state
        if (!isFlattened)
        {
            rollPressed = true;
            moveSpeed += 6f;
        }
    }

    private void OnRollCanceled(InputAction.CallbackContext ctx)
    {
        if (rollPressed)
        {
            rollPressed = false;
            moveSpeed -= 6f;
        }
    }

    private void OnPuffPerformed(InputAction.CallbackContext ctx)
    {
        puffHeld = true;
        HandleStateTransition(true, false);
    }

    private void OnPuffCanceled(InputAction.CallbackContext ctx)
    {
        puffHeld = false;
        HandleStateTransition(false, flattenHeld);
    }

    private void OnFlattenPerformed(InputAction.CallbackContext ctx)
    {
        flattenHeld = true;
        HandleStateTransition(puffHeld, true);
    }

    private void OnFlattenCanceled(InputAction.CallbackContext ctx)
    {
        flattenHeld = false;
        HandleStateTransition(puffHeld, false);
    }

    private void HandleStateTransition(bool newPuffState, bool newFlattenState)
    {
        // Store previous state for super jump detection
        wasPreviouslyFlattened = isFlattened;

        // States are mutually exclusive - new input cancels previous
        if (newPuffState && newFlattenState)
        {
            // If both pressed simultaneously, prioritize the one that wasn't already active
            if (isPuffed)
            {
                isPuffed = false;
                isFlattened = true;
            }
            else
            {
                isPuffed = true;
                isFlattened = false;
            }
        }
        else
        {
            isPuffed = newPuffState;
            isFlattened = newFlattenState;
        }

        // Record state change time
        lastStateChangeTime = Time.time;

        // Update animator states
        _animator.SetBool("isPuffed", isPuffed);
        _animator.SetBool("isFlattened", isFlattened);

        // Check for super jump condition
        if (wasPreviouslyFlattened && isPuffed && isGrounded)
        {
            float timeSinceStateChange = Time.time - lastStateChangeTime;
            if (timeSinceStateChange <= superJumpTransitionWindow)
            {
                TriggerSuperJump();
            }
        }

        // Reset stuck to ground when leaving flattened state
        if (!isFlattened)
        {
            isStuckToGround = false;
        }
    }

    private void TriggerSuperJump()
    {
        if (isGrounded)
        {
            // Perform super jump
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpSpeed * superJumpMultiplier);
            isGrounded = false;
            isJumping = true;
            
            Debug.Log("Super Jump Triggered!");
        }
    }

    void Update()
    {
        if (canMove)
        {
            // Handle movement direction
            if (moveInput.x > 0.1f)
            {
                currentDirection = RIGHT;
                sr.flipX = true;
            }
            else if (moveInput.x < -0.1f)
            {
                currentDirection = LEFT;
                sr.flipX = false;
            }
            else
            {
                currentDirection = null;
            }

            // Update jump buffer timer
            if (jumpBufferTimer > 0)
            {
                jumpBufferTimer -= Time.deltaTime;
            }

            // Handle jump input based on current state
            HandleJumpInput();

            // Apply variable jump height (only in normal state)
            if (!isPuffed && !isFlattened)
            {
                HandleVariableJumpHeight();
            }
        }
        else
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        }
    }

    void FixedUpdate()
    {
        if (!isJumping)
        {
            // Movement based on current state
            if (canMove)
            {
                HandleMovementByState();
            }
        }
        else
        {
            // Jump movement
            HandleJump();
        }
        
        // Apply enhanced gravity based on state
        ApplyEnhancedGravity();
        
        HandleRotation();
    }

    private void HandleMovementByState()
    {
        if (isFlattened && isStuckToGround)
        {
            // Crawling movement when flattened and stuck to ground
            if (currentDirection == RIGHT)
            {
                rb.linearVelocity = new Vector2(crawlSpeed, rb.linearVelocity.y);
            }
            else if (currentDirection == LEFT)
            {
                rb.linearVelocity = new Vector2(-crawlSpeed, rb.linearVelocity.y);
            }
            else
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x * 0.6f, rb.linearVelocity.y);
            }
        }
        else
        {
            // Normal movement (works for normal and puffed states)
            if (currentDirection == RIGHT)
            {
                rb.linearVelocity = new Vector2(moveSpeed, rb.linearVelocity.y * 0.6f);
            }
            else if (currentDirection == LEFT)
            {
                rb.linearVelocity = new Vector2(-moveSpeed, rb.linearVelocity.y * 0.6f);
            }
            else
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x * 0.6f, rb.linearVelocity.y * 0.6f);
            }
        }
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Platform") || collision.gameObject.CompareTag("Ground"))
        {
            isGrounded = true;
            isJumping = false;
            ResetTimer();

            // If flattened, stick to ground
            if (isFlattened)
            {
                isStuckToGround = true;
            }

            // Notify subscribers that we've landed
            if (OnGrounded != null)
            {
                OnGrounded(collision);
            }
        }

        if (collision.gameObject.CompareTag("DeadZone"))
        {
            if (OnHitDeadZone != null)
            {
                OnHitDeadZone(collision);
            }
        }
    }

    private void HandleJumpInput()
    {
        if (isPuffed)
        {
            // No jumping allowed when puffed
            return;
        }

        if (isFlattened)
        {
            // Mini hop when flattened
            if (jumpPressed && isGrounded && !jumpConsumed)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, miniHopSpeed);
                jumpConsumed = true;
            }
            return;
        }

        // Normal jump logic (only in normal state)
        if (isGrounded && !jumpConsumed)
        {
            if (jumpPressed)
            {
                isJumping = true;
                isGrounded = false;
                jumpConsumed = true;
                jumpBufferTimer = 0;
            }
            else if (jumpBufferTimer > 0)
            {
                isJumping = true;
                isGrounded = false;
                jumpConsumed = true;
                jumpBufferTimer = 0;
            }
        }
    }

    private void HandleVariableJumpHeight()
    {
        // Cut jump short if button is released early (only in normal state)
        if (jumpWasReleased && rb.linearVelocity.y > jumpCutThreshold && !isGrounded)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * 0.5f);
            jumpWasReleased = false;
        }
    }

    private void ApplyEnhancedGravity()
    {
        if (!isGrounded)
        {
            if (isPuffed)
            {
                // Very light gravity when puffed
                rb.gravityScale = originalGravityScale * puffGravityMultiplier;
            }
            else if (isFlattened)
            {
                // Heavy gravity when flattened
                rb.gravityScale = originalGravityScale * flattenGravityMultiplier;
            }
            else
            {
                // Normal enhanced gravity system
                if (rb.linearVelocity.y < 0) // Falling
                {
                    rb.gravityScale = originalGravityScale * fallGravityMultiplier;
                }
                else if (rb.linearVelocity.y > 0 && jumpWasReleased) // Rising but jump released
                {
                    rb.gravityScale = originalGravityScale * lowJumpMultiplier;
                }
                else
                {
                    rb.gravityScale = originalGravityScale;
                }
            }
        }
        else
        {
            rb.gravityScale = originalGravityScale;
        }
    }

    // Adjust movement parameters
    public void UpdateMovementParams(float speedChange, float jumpTimeChange, float jumpSpeedChange)
    {
        moveSpeed += speedChange;
        jumpTime += jumpTimeChange;
        jumpSpeed -= jumpSpeedChange;
    }

    private void HandleJump()
    {
        if (!isGrounded && isJumping)
        {
            if (currentDirection == RIGHT)
            {
                rb.linearVelocity = new Vector2(moveSpeed, jumpSpeed * 0.6f);
            }
            else if (currentDirection == LEFT)
            {
                rb.linearVelocity = new Vector2(-moveSpeed, jumpSpeed * 0.6f);
            }
            else
            {
                rb.linearVelocity = new Vector2(0, jumpSpeed * 0.6f);
            }

            isJumping = !StartTimer(jumpTime);
        }
        else
        {
            ResetTimer();
        }
    }

    private void HandleRotation()
    {
        // Roll only works in normal or puffed state
        if (rollPressed && isGrounded && rb.linearVelocity.x != 0 && !isFlattened)
        {
            float rotationAmount = rb.linearVelocity.x * 5f * Time.fixedDeltaTime;
            transform.Rotate(0, 0, -rotationAmount);
        }
        else
        {
            transform.rotation = Quaternion.identity;
        }
    }

    private bool StartTimer(float limit)
    {
        timer += Time.deltaTime;
        if (timer > limit)
        {
            ResetTimer();
            return true;
        }
        else
        {
            return false;
        }
    }

    private void ResetTimer()
    {
        timer = 0.0f;
    }
}