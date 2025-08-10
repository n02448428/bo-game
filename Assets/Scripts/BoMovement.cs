using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
public class BoMovement : MonoBehaviour
{
    // Components
    Rigidbody2D rb;
    SpriteRenderer sr;
    [SerializeField] private Animator _animator;

    // Input
    private PlayerInputActions inputActions;
    private Vector2 moveInput;
    private bool jumpPressed;
    private bool jumpWasReleased;
    private bool rollPressed;
    private bool puffHeld;
    private bool flattenHeld;

    // Movement params (tweak in inspector)
    [Header("Movement")]
    public float moveSpeed = 6f;
    public float jumpSpeed = 40f;
    public float jumpTime = 0.1f;

    [Header("Enhanced Jumping")]
    public float jumpBufferTime = 0.2f;
    public float fallGravityMultiplier = 0.5f;
    public float lowJumpMultiplier = 4f;
    public float jumpCutThreshold = 5f;

    [Header("Roll")]
    public float rollSpeedBonus = 6f;

    [Header("Puff (float)")]
    public float puffGravityMultiplier = 0.1f;

    [Header("Flatten (stick & crawl)")]
    public float flattenGravityMultiplier = 4f;
    public float crawlSpeed = 2f;
    public float miniHopSpeed = 15f;

    [Header("Super Jump")]
    public float superJumpMultiplier = 2.5f;
    public float superJumpTransitionWindow = 0.3f;

    // New features
    [Header("Puff Flap (hop while puffed)")]
    public float puffFlapForce = 25f;
    public float puffFlapCooldown = 0.25f;
    private float puffFlapTimer = 0f;

    [Header("Teleport (phase)")]
    public float teleportDistance = 3f;
    public LayerMask teleportObstacles; // destination must NOT overlap this mask
    public float teleportCooldown = 0.8f;
    private float teleportTimer = 0f;
    public float teleportClearRadius = 0.4f;

    // State
    public const string RIGHT = "right";
    public const string LEFT = "left";
    private string currentDirection;
    private bool isGrounded = false;
    private bool isJumping = false;
    private bool canMove = true;

    // Advanced state flags
    private float jumpBufferTimer = 0f;
    private bool jumpConsumed = false;
    private float originalGravityScale;
    private bool isPuffed = false;
    private bool isFlattened = false;
    private float lastStateChangeTime = 0f;
    private bool wasPreviouslyFlattened = false;
    private bool isStuckToGround = false;

    // Timer
    private float timer = 0.0f;

    // Events
    public delegate void GroundedEvent(Collision2D collision);
    public event GroundedEvent OnGrounded;
    public delegate void DeadZoneEvent(Collision2D collision);
    public event DeadZoneEvent OnHitDeadZone;

    // Properties
    public bool CanMove { get => canMove; set => canMove = value; }
    public bool IsGrounded => isGrounded;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        originalGravityScale = rb.gravityScale;

        // Input asset
        inputActions = new PlayerInputActions();
    }

    void OnEnable()
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

        inputActions.Player.Teleport.performed += OnTeleportPerformed;
    }

    void OnDisable()
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

        inputActions.Player.Teleport.performed -= OnTeleportPerformed;

        inputActions.Player.Disable();
    }

    // -------------------------
    // Input callbacks
    // -------------------------
    private void OnMovePerformed(InputAction.CallbackContext ctx)
    {
        moveInput = ctx.ReadValue<Vector2>();
        _animator?.SetBool("isWalking", true);
    }

    private void OnMoveCanceled(InputAction.CallbackContext ctx)
    {
        moveInput = Vector2.zero;
        _animator?.SetBool("isWalking", false);
    }

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        // Buffer jump
        jumpPressed = true;
        jumpWasReleased = false;
        jumpBufferTimer = jumpBufferTime;
        jumpConsumed = false;

        // Puff-flap: works on ground or air while puffed
        if (isPuffed && puffFlapTimer <= 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, puffFlapForce);
            puffFlapTimer = puffFlapCooldown;
            // consume the jump so it doesn't trigger normal jump as well
            jumpConsumed = true;
            jumpBufferTimer = 0f;
        }
    }

    private void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        jumpPressed = false;
        jumpWasReleased = true;
    }

    private void OnRollPerformed(InputAction.CallbackContext ctx)
    {
        if (!isFlattened)
        {
            rollPressed = true;
            moveSpeed += rollSpeedBonus;
        }
    }

    private void OnRollCanceled(InputAction.CallbackContext ctx)
    {
        if (rollPressed)
        {
            rollPressed = false;
            moveSpeed -= rollSpeedBonus;
        }
    }

    private void OnPuffPerformed(InputAction.CallbackContext ctx)
    {
        puffHeld = true;
        HandleStateTransition(true, isFlattened);
    }

    private void OnPuffCanceled(InputAction.CallbackContext ctx)
    {
        puffHeld = false;
        HandleStateTransition(false, isFlattened);
    }

    private void OnFlattenPerformed(InputAction.CallbackContext ctx)
    {
        flattenHeld = true;
        HandleStateTransition(isPuffed, true);
    }

    private void OnFlattenCanceled(InputAction.CallbackContext ctx)
    {
        flattenHeld = false;
        HandleStateTransition(isPuffed, false);
    }

    // Teleport: allow teleporting "through" intermediate obstacles as long as destination is free
    private void OnTeleportPerformed(InputAction.CallbackContext ctx)
    {
        if (teleportTimer > 0f || !canMove) return;

        Vector2 dir = DetermineFacingDirection();
        if (dir == Vector2.zero) dir = Vector2.right;

        Vector2 targetPos = (Vector2)transform.position + dir * teleportDistance;

        // Destination must be free (we only check the destination, not intermediate obstacles)
        if (!Physics2D.OverlapCircle(targetPos, teleportClearRadius, teleportObstacles))
        {
            // Optionally: spawn VFX here (teleport-out/in)
            transform.position = targetPos;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f); // reset vertical velocity after teleport
            teleportTimer = teleportCooldown;
        }
        else
        {
            // If destination blocked, try to find closest free spot between current and target
            // Sample increments along the line (short loop, efficient)
            int samples = 6;
            for (int i = samples - 1; i >= 1; --i)
            {
                float t = i / (float)samples;
                Vector2 samplePos = (Vector2)transform.position + dir * (teleportDistance * t);
                if (!Physics2D.OverlapCircle(samplePos, teleportClearRadius, teleportObstacles))
                {
                    transform.position = samplePos;
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
                    teleportTimer = teleportCooldown;
                    break;
                }
            }
        }
    }

    // -------------------------
    // Unity lifecycle
    // -------------------------
    void Update()
    {
        // Timers
        if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;
        if (puffFlapTimer > 0f) puffFlapTimer -= Time.deltaTime;
        if (teleportTimer > 0f) teleportTimer -= Time.deltaTime;
    }

    void FixedUpdate()
    {
        if (!canMove)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        // Handle direction and sprite flip
        if (moveInput.x > 0.1f)
        {
            currentDirection = RIGHT;
            sr.flipX = false;
        }
        else if (moveInput.x < -0.1f)
        {
            currentDirection = LEFT;
            sr.flipX = true;
        }
        else
        {
            currentDirection = null;
        }

        // Jump buffering & consumption
        HandleJumpInput();

        // Movement by state
        if (!isJumping)
        {
            HandleMovementByState();
        }
        else
        {
            HandleJumpPhysics();
        }

        // Gravity adjustments for feel
        ApplyEnhancedGravity();

        // Rotation / roll visual
        HandleRotation();
    }

    // -------------------------
    // Movement helpers
    // -------------------------
    private void HandleStateTransition(bool newPuffState, bool newFlattenState)
    {
        wasPreviouslyFlattened = isFlattened;

        // Mutually exclusive states
        if (newPuffState && newFlattenState)
        {
            // If both pressed simultaneously, prioritize the one that wasn't active
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

        lastStateChangeTime = Time.time;
        _animator?.SetBool("isPuffed", isPuffed);
        _animator?.SetBool("isFlattened", isFlattened);

        // Super jump detection: flattened -> puff quickly while grounded
        if (wasPreviouslyFlattened && isPuffed && isGrounded)
        {
            float timeSince = Time.time - lastStateChangeTime;
            if (timeSince <= superJumpTransitionWindow)
            {
                TriggerSuperJump();
            }
        }

        if (!isFlattened) isStuckToGround = false;
    }

    private void HandleJumpInput()
    {
        if (isPuffed)
        {
            // don't perform normal jump while puffed (puff-flap handled in input)
            return;
        }

        if (isFlattened)
        {
            // mini hop when flattened
            if (jumpPressed && isGrounded && !jumpConsumed)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, miniHopSpeed);
                jumpConsumed = true;
            }
            return;
        }

        // Normal state: use buffer
        if (isGrounded && !jumpConsumed)
        {
            if (jumpPressed)
            {
                isJumping = true;
                isGrounded = false;
                jumpConsumed = true;
                jumpBufferTimer = 0f;
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpSpeed);
            }
            else if (jumpBufferTimer > 0f)
            {
                isJumping = true;
                isGrounded = false;
                jumpConsumed = true;
                jumpBufferTimer = 0f;
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpSpeed);
            }
        }
    }

    private void HandleJumpPhysics()
    {
        if (!isGrounded && isJumping)
        {
            Vector2 newVel;
            if (currentDirection == RIGHT)
                newVel = new Vector2(moveSpeed, jumpSpeed * 0.6f);
            else if (currentDirection == LEFT)
                newVel = new Vector2(-moveSpeed, jumpSpeed * 0.6f);
            else
                newVel = new Vector2(0f, jumpSpeed * 0.6f);

            rb.linearVelocity = new Vector2(newVel.x, newVel.y);

            // Use jumpTime timer style to end jumping (original approach)
            isJumping = !StartTimer(jumpTime);
        }
        else
        {
            ResetTimer();
        }
    }

    private void HandleMovementByState()
    {
        // Flattened + stuck to ground: crawl or hold position
        if (isFlattened && isStuckToGround)
        {
            if (currentDirection == RIGHT)
                rb.linearVelocity = new Vector2(crawlSpeed, 0f);
            else if (currentDirection == LEFT)
                rb.linearVelocity = new Vector2(-crawlSpeed, 0f);
            else
                rb.linearVelocity = new Vector2(0f, 0f); // hold position on ground/ramp (glued)
            return;
        }

        // Normal movement (works for normal and puffed states)
        if (currentDirection == RIGHT)
            rb.linearVelocity = new Vector2(moveSpeed, rb.linearVelocity.y * 0.6f);
        else if (currentDirection == LEFT)
            rb.linearVelocity = new Vector2(-moveSpeed, rb.linearVelocity.y * 0.6f);
        else
            rb.linearVelocity = new Vector2(rb.linearVelocity.x * 0.6f, rb.linearVelocity.y * 0.6f);
    }

    private void ApplyEnhancedGravity()
    {
        if (!isGrounded)
        {
            if (isPuffed)
            {
                rb.gravityScale = originalGravityScale * puffGravityMultiplier;
            }
            else if (isFlattened)
            {
                rb.gravityScale = originalGravityScale * flattenGravityMultiplier;
            }
            else
            {
                if (rb.linearVelocity.y < 0) // falling
                    rb.gravityScale = originalGravityScale * fallGravityMultiplier;
                else if (rb.linearVelocity.y > 0 && jumpWasReleased)
                    rb.gravityScale = originalGravityScale * lowJumpMultiplier;
                else
                    rb.gravityScale = originalGravityScale;
            }
        }
        else
        {
            rb.gravityScale = originalGravityScale;
        }
    }

    private void HandleRotation()
    {
        // Roll only works in normal or puffed state and on ground movement
        if (rollPressed && isGrounded && Mathf.Abs(rb.linearVelocity.x) > 0.01f && !isFlattened)
        {
            float rotationAmount = rb.linearVelocity.x * 5f * Time.fixedDeltaTime;
            transform.Rotate(0f, 0f, -rotationAmount);
        }
        else
        {
            transform.rotation = Quaternion.identity;
        }
    }

    private void TriggerSuperJump()
    {
        if (isGrounded)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpSpeed * superJumpMultiplier);
            isGrounded = false;
            isJumping = true;
            Debug.Log("Super Jump Triggered!");
        }
    }

    // -------------------------
    // Utility & collisions
    // -------------------------
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Platform") || collision.gameObject.CompareTag("Ground"))
        {
            isGrounded = true;
            isJumping = false;
            ResetTimer();

            // If flattened, stick to ground (prevents sliding on slopes)
            if (isFlattened)
            {
                isStuckToGround = true;
            }

            OnGrounded?.Invoke(collision);
        }

        if (collision.gameObject.CompareTag("DeadZone"))
        {
            OnHitDeadZone?.Invoke(collision);
        }
    }

    // Simple timer helpers (kept from original design)
    private bool StartTimer(float limit)
    {
        timer += Time.deltaTime;
        if (timer > limit)
        {
            ResetTimer();
            return true;
        }
        return false;
    }

    private void ResetTimer() => timer = 0f;

    // Small helper: returns facing direction as Vector2
    private Vector2 DetermineFacingDirection()
    {
        if (currentDirection == RIGHT) return Vector2.right;
        if (currentDirection == LEFT) return Vector2.left;
        // fallback to sprite orientation
        return sr.flipX ? Vector2.left : Vector2.right;
    }

    // Public method used by gameplay to tune movement via collectibles
    public void UpdateMovementParams(float speedChange, float jumpTimeChange, float jumpSpeedChange)
    {
        moveSpeed += speedChange;
        jumpTime += jumpTimeChange;
        jumpSpeed -= jumpSpeedChange;
    }
}
