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
    private Animator _animator;

    // Movement parameters
    [Header("Movement Parameters")]
    public float moveSpeed = 6f;
    public float jumpSpeed = 40f;
    public float jumpTime = 0.1f;

    [Header("Enhanced Jump Settings")]
    [Tooltip("Time window to buffer jump input before landing")]
    public float jumpBufferTime = 0.2f;
    
    [Tooltip("Gravity multiplier when falling (makes jumps feel snappier)")]
    public float fallGravityMultiplier = 1.5f;
    
    [Tooltip("Gravity multiplier for short hops when jump is released early")]
    public float lowJumpMultiplier = 4f;
    
    [Tooltip("Jump cut threshold - minimum velocity to allow jump cutting")]
    public float jumpCutThreshold = 5f;

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

    // Timer
    private float timer = 0.0f;

    // Input System
    private PlayerInputActions inputActions;
    private Vector2 moveInput;
    private bool jumpPressed;
    private bool jumpConsumed = false;
    private bool rollPressed = false;

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
            inputActions.Player.Disable();
        }
    }

    private void OnMovePerformed(InputAction.CallbackContext ctx)
    {
        moveInput = ctx.ReadValue<Vector2>();
    }

    private void OnMoveCanceled(InputAction.CallbackContext ctx)
    {
        moveInput = Vector2.zero;
    }

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        jumpPressed = true;
        jumpWasReleased = false;
        jumpBufferTimer = jumpBufferTime; // Start jump buffer
        jumpConsumed = false; // Reset consumed flag for new input
    }

    private void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        jumpPressed = false;
        jumpWasReleased = true;
    }

    private void OnRollPerformed(InputAction.CallbackContext ctx)
    {
        rollPressed = true;
        moveSpeed += 6f;
    }

    private void OnRollCanceled(InputAction.CallbackContext ctx)
    {
        rollPressed = false;
        moveSpeed -= 6f;
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
            
            // Handle jump input with buffering
            HandleJumpInput();
            
            // Apply variable jump height
            HandleVariableJumpHeight();
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
            // Normal movement
            if (canMove)
            {
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
        else
        {
            // Jump movement
            HandleJump();
        }
        
        // Apply enhanced gravity
        ApplyEnhancedGravity();
        
        HandleRotation();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Platform") || collision.gameObject.CompareTag("Ground"))
        {
            isGrounded = true;
            isJumping = false;
            ResetTimer();

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
        // Handle both immediate jumps and buffered jumps
        if (isGrounded && !jumpConsumed)
        {
            // Immediate jump when grounded and jump is pressed
            if (jumpPressed)
            {
                isJumping = true;
                isGrounded = false; // Set not grounded to trigger jump logic
                jumpConsumed = true;
                jumpBufferTimer = 0; // Clear buffer
            }
            // Buffered jump - execute if we have buffered input
            else if (jumpBufferTimer > 0)
            {
                isJumping = true;
                isGrounded = false; // Set not grounded to trigger jump logic
                jumpConsumed = true;
                jumpBufferTimer = 0; // Clear buffer
            }
        }
    }

    private void HandleVariableJumpHeight()
    {
        // Cut jump short if button is released early (variable jump height)
        if (jumpWasReleased && rb.linearVelocity.y > jumpCutThreshold && !isGrounded)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * 0.5f);
            jumpWasReleased = false;
        }
    }

    private void ApplyEnhancedGravity()
    {
        // Apply different gravity based on jump state
        if (!isGrounded)
        {
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
        if (rollPressed && isGrounded && rb.linearVelocity.x != 0)
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