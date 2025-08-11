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
    private Vector2 moveInput = Vector2.zero;
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

    [Header("Puff (float) - entry smoothing + float control")]
    public float puffGravityMultiplier = 0.1f;
    public float puffMaxSpeed = 4f;
    [Tooltip("Higher -> faster exponential convergence")]
    public float puffAcceleration = 6f;
    [Tooltip("How quickly puff horizontal velocity decays when no input")]
    public float puffDecay = 5f;
    [Tooltip("Time (seconds) to smooth (lerp) existing horizontal velocity to zero when entering puff")]
    public float puffEntrySmoothingDuration = 0.08f;

    [Header("Flatten (stick & crawl)")]
    public float flattenGravityMultiplier = 4f;
    public float crawlSpeed = 2f;
    public float miniHopSpeed = 15f;
    [Tooltip("If true, allow crawling when flattened; if false, flatten locks horizontal movement while grounded.")]
    public bool flattenAllowsCrawl = true;

    [Header("Super Jump")]
    public float superJumpMultiplier = 2.5f;
    public float superJumpTransitionWindow = 0.3f;

    [Header("Puff Flap (works on ground or air)")]
    public float puffFlapForce = 25f;
    public float puffFlapCooldown = 0.25f;
    private float puffFlapTimer = 0f;

    [Header("Teleport (phase)")]
    public float teleportDistance = 3f;
    public LayerMask teleportObstacles; // destination must NOT overlap this mask
    public float teleportCooldown = 0.8f;
    public float teleportClearRadius = 0.4f;
    private float teleportTimer = 0f;

    [Header("Ground Check")]
    public LayerMask groundMask;
    public float groundCheckDistance = 1f;

    // State
    public const string RIGHT = "right";
    public const string LEFT = "left";
    private string currentDirection;
    private bool isGrounded = false;
    private bool isJumping = false;
    private bool canMove = true;

    // Input state tracking for reversion
    private enum AbilityState { None, Puff, Flatten }
    private AbilityState currentAbility = AbilityState.None;
    private AbilityState heldAbility = AbilityState.None; // Tracks the held state to revert to

    // Jump buffering
    private float jumpBufferTimer = 0f;
    private bool jumpConsumed = false;

    // Gravity
    private float originalGravityScale;

    // Puff internals
    private float puffVelX = 0f;          // the velocity we manage while puffing
    private float prevHorizontalVel = 0f; // used to smooth into puff
    private float puffEntryTimer = 0f;    // counts down while smoothing into puff

    // Flatten internals
    private bool isPuffed = false;
    private bool isFlattened = false;
    private float lastStateChangeTime = 0f;
    private bool wasPreviouslyFlattened = false;
    private bool isStuckToGround = false;
    private Vector2 groundNormal = Vector2.up;

    // Timer style (keeps parity with earlier code)
    private float timer = 0f;

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
        object raw = ctx.ReadValueAsObject();
        if (raw is Vector2 v)
        {
            moveInput = v;
        }
        else if (raw is float f)
        {
            string path = ctx.control?.path?.ToLower() ?? "";
            if (path.Contains("/z")) moveInput.x = f;
            else if (path.Contains("/rz")) moveInput.y = f;
            else moveInput = new Vector2(f, 0f);
        }
        else
        {
            moveInput = inputActions.Player.Move.ReadValue<Vector2>();
        }

        _animator?.SetBool("isWalking", Mathf.Abs(moveInput.x) > 0.1f);
    }

    private void OnMoveCanceled(InputAction.CallbackContext ctx)
    {
        moveInput = Vector2.zero;
        _animator?.SetBool("isWalking", false);
    }

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        jumpPressed = true;
        jumpWasReleased = false;
        jumpBufferTimer = jumpBufferTime;
        jumpConsumed = false;

        if (isPuffed && puffFlapTimer <= 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, puffFlapForce);
            puffFlapTimer = puffFlapCooldown;
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
        prevHorizontalVel = rb.linearVelocity.x;
        puffEntryTimer = puffEntrySmoothingDuration;
        puffVelX = 0f;
        // Prioritize Puff if both are pressed, but track Flatten as held
        if (flattenHeld)
        {
            heldAbility = AbilityState.Flatten;
            HandleStateTransition(true, false);
        }
        else
        {
            heldAbility = AbilityState.None;
            HandleStateTransition(true, false);
        }
    }

    private void OnPuffCanceled(InputAction.CallbackContext ctx)
    {
        puffHeld = false;
        // Revert to held ability (Flatten) if still held, otherwise go to None
        if (flattenHeld)
        {
            HandleStateTransition(false, true);
            heldAbility = AbilityState.None;
        }
        else
        {
            HandleStateTransition(false, false);
            heldAbility = AbilityState.None;
        }
    }

    private void OnFlattenPerformed(InputAction.CallbackContext ctx)
    {
        flattenHeld = true;
        // Prioritize Flatten if both are pressed, but track Puff as held
        if (puffHeld)
        {
            heldAbility = AbilityState.Puff;
            HandleStateTransition(false, true);
        }
        else
        {
            heldAbility = AbilityState.None;
            HandleStateTransition(false, true);
        }

        // Reduce speed to crawl speed
        rb.linearVelocity = new Vector2(Mathf.Clamp(rb.linearVelocity.x, -crawlSpeed, crawlSpeed), rb.linearVelocity.y);
    }

    private void OnFlattenCanceled(InputAction.CallbackContext ctx)
    {
        flattenHeld = false;
        // Revert to held ability (Puff) if still held, otherwise go to None
        if (puffHeld)
        {
            HandleStateTransition(true, false);
            heldAbility = AbilityState.None;
        }
        else
        {
            HandleStateTransition(false, false);
            heldAbility = AbilityState.None;
        }
    }

    private void OnTeleportPerformed(InputAction.CallbackContext ctx)
    {
        if (teleportTimer > 0f || !canMove) return;

        Vector2 dir = DetermineFacingDirection();
        if (dir == Vector2.zero) dir = Vector2.right;

        Vector2 targetPos = (Vector2)transform.position + dir * teleportDistance;

        if (!Physics2D.OverlapCircle(targetPos, teleportClearRadius, teleportObstacles))
        {
            transform.position = targetPos;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
            teleportTimer = teleportCooldown;
        }
        else
        {
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
        if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;
        if (puffFlapTimer > 0f) puffFlapTimer -= Time.deltaTime;
        if (teleportTimer > 0f) teleportTimer -= Time.deltaTime;
        if (puffEntryTimer > 0f) puffEntryTimer -= Time.deltaTime;
    }

    void FixedUpdate()
    {
        if (!canMove)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        // Update ground normal with raycast if grounded
        if (isGrounded)
        {
            RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, groundCheckDistance, groundMask);
            if (hit.collider != null)
            {
                groundNormal = hit.normal;
            }
        }
        else
        {
            groundNormal = Vector2.up;
        }

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

        HandleJumpInput();

        if (isPuffed)
        {
            if (puffEntryTimer > 0f)
            {
                float progress = 1f - Mathf.Clamp01(puffEntryTimer / puffEntrySmoothingDuration);
                float newX = Mathf.Lerp(prevHorizontalVel, 0f, progress);
                rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);
                puffVelX = newX;
            }
            else
            {
                ApplyPuffHorizontalMovement();
            }
        }

        if (!isJumping)
        {
            HandleMovementByState();
        }
        else
        {
            HandleJumpPhysics();
        }

        ApplyEnhancedGravity();
        HandleRotation();
    }

    // -------------------------
    // Movement helpers
    // -------------------------
    private void HandleStateTransition(bool newPuffState, bool newFlattenState)
    {
        wasPreviouslyFlattened = isFlattened;

        isPuffed = newPuffState;
        isFlattened = newFlattenState;
        currentAbility = isPuffed ? AbilityState.Puff : isFlattened ? AbilityState.Flatten : AbilityState.None;

        lastStateChangeTime = Time.time;
        _animator?.SetBool("isPuffed", isPuffed);
        _animator?.SetBool("isFlattened", isFlattened);

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
            return;
        }

        if (isFlattened)
        {
            if (jumpPressed && isGrounded && !jumpConsumed && flattenAllowsCrawl)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, miniHopSpeed);
                jumpConsumed = true;
            }
            return;
        }

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
            float horizontal = 0f;
            if (currentDirection == RIGHT) horizontal = moveSpeed;
            else if (currentDirection == LEFT) horizontal = -moveSpeed;
            else horizontal = 0f;

            rb.linearVelocity = new Vector2(horizontal, jumpSpeed * 0.6f);
            isJumping = !StartTimer(jumpTime);
        }
        else
        {
            ResetTimer();
        }
    }

    private void HandleMovementByState()
    {
        // Flattened & stuck to ground: crawl or hold
        if (isFlattened && isStuckToGround)
        {
            if (flattenAllowsCrawl)
            {
                if (currentDirection != null)
                {
                    // Project movement onto the slope surface
                    Vector2 desired = new Vector2(moveInput.x > 0 ? 1f : -1f, 0f);
                    Vector2 projected = desired - Vector2.Dot(desired, groundNormal) * groundNormal;
                    rb.linearVelocity = projected.normalized * crawlSpeed;
                }
                else
                {
                    rb.linearVelocity = Vector2.zero;
                }
            }
            else
            {
                rb.linearVelocity = Vector2.zero; // hold position horizontal
            }
            return;
        }

        // Normal movement (works for normal and puffed states when not in the puff-specific handler)
        if (currentDirection == RIGHT)
            rb.linearVelocity = new Vector2(moveSpeed, rb.linearVelocity.y * 0.6f);
        else if (currentDirection == LEFT)
            rb.linearVelocity = new Vector2(-moveSpeed, rb.linearVelocity.y * 0.6f);
        else
            rb.linearVelocity = new Vector2(rb.linearVelocity.x * 0.6f, rb.linearVelocity.y * 0.6f);
    }

    private void ApplyPuffHorizontalMovement()
    {
        float inputX = moveInput.x;
        float target = Mathf.Clamp(inputX, -1f, 1f) * puffMaxSpeed;

        if (Mathf.Abs(inputX) > 0.1f)
        {
            float lerpFactor = 1f - Mathf.Exp(-puffAcceleration * Time.fixedDeltaTime);
            puffVelX = Mathf.Lerp(puffVelX, target, lerpFactor);
        }
        else
        {
            puffVelX = Mathf.MoveTowards(puffVelX, 0f, puffDecay * Time.fixedDeltaTime);
        }

        rb.linearVelocity = new Vector2(puffVelX, rb.linearVelocity.y);
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
                if (rb.linearVelocity.y < 0)
                    rb.gravityScale = originalGravityScale * fallGravityMultiplier;
                else if (rb.linearVelocity.y > 0 && jumpWasReleased)
                    rb.gravityScale = originalGravityScale * lowJumpMultiplier;
                else
                    rb.gravityScale = originalGravityScale;
            }
        }
        else
        {
            if (isFlattened)
            {
                rb.gravityScale = 0f; // Prevent sliding on slopes
            }
            else
            {
                rb.gravityScale = originalGravityScale;
            }
        }
    }

    private void HandleRotation()
    {
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
        }
    }

    // -------------------------
    // Collisions & utilities
    // -------------------------
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Platform") || collision.gameObject.CompareTag("Ground"))
        {
            isGrounded = true;
            isJumping = false;
            ResetTimer();

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

    void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Platform") || collision.gameObject.CompareTag("Ground"))
        {
            isGrounded = false;
            isStuckToGround = false;
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
        return false;
    }

    private void ResetTimer() => timer = 0f;

    private Vector2 DetermineFacingDirection()
    {
        if (currentDirection == RIGHT) return Vector2.right;
        if (currentDirection == LEFT) return Vector2.left;
        return sr.flipX ? Vector2.left : Vector2.right;
    }

    public void UpdateMovementParams(float speedChange, float jumpTimeChange, float jumpSpeedChange)
    {
        moveSpeed += speedChange;
        jumpTime += jumpTimeChange;
        jumpSpeed -= jumpSpeedChange;
    }
}