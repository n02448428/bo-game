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

    // State
    public const string RIGHT = "right";
    public const string LEFT = "left";
    private string currentDirection;
    private bool isGrounded = false;
    private bool isJumping = false;
    private bool canMove = true;

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
    // Input callbacks (robust: accepts Vector2 or single float axes like z/rz)
    // -------------------------
    private void OnMovePerformed(InputAction.CallbackContext ctx)
    {
        // Try to read Vector2, but some controllers expose Z/Rz as single float axes.
        object raw = ctx.ReadValueAsObject();
        if (raw is Vector2 v)
        {
            moveInput = v;
        }
        else if (raw is float f)
        {
            // Map float control to X or Y based on control path (z/rz)
            string path = ctx.control?.path?.ToLower() ?? "";
            if (path.Contains("/z")) moveInput.x = f;
            else if (path.Contains("/rz")) moveInput.y = f;
            else moveInput = new Vector2(f, 0f); // fallback
        }
        else
        {
            // fallback: read action aggregated value (may still work)
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
        // Buffer jump
        jumpPressed = true;
        jumpWasReleased = false;
        jumpBufferTimer = jumpBufferTime;
        jumpConsumed = false;

        // Puff-flap: works on ground or air while puffed (cooldown enforced)
        if (isPuffed && puffFlapTimer <= 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, puffFlapForce);
            puffFlapTimer = puffFlapCooldown;
            // don't let this also trigger a normal jump
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
        // smooth into puff: record prior horizontal velocity and start short smoothing
        prevHorizontalVel = rb.linearVelocity.x;
        puffEntryTimer = puffEntrySmoothingDuration;
        puffVelX = 0f;
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

    private void OnTeleportPerformed(InputAction.CallbackContext ctx)
    {
        if (teleportTimer > 0f || !canMove) return;

        Vector2 dir = DetermineFacingDirection();
        if (dir == Vector2.zero) dir = Vector2.right;

        Vector2 targetPos = (Vector2)transform.position + dir * teleportDistance;

        // If full destination is free, teleport — otherwise sample along the line for closest free spot
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
        // timers
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

        // Direction & sprite flip
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

        // Handle jump input
        HandleJumpInput();

        // Puff entry smoothing (smoothly reduce prior horizontal momentum to zero)
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
                // apply the light/float horizontal system
                ApplyPuffHorizontalMovement();
            }
        }

        // Flatten hold lock: if currently flattened AND the player is holding flatten and grounded -> lock horizontal
        if (isFlattened && flattenHeld && isGrounded && !flattenAllowsCrawl)
        {
            rb.linearVelocity = new Vector2(0f, 0f); // hold position on ground/ramp
            return;
        }

        // Normal movement or flattened crawl
        if (!isJumping)
        {
            HandleMovementByState();
        }
        else
        {
            HandleJumpPhysics();
        }

        // gravity & other
        ApplyEnhancedGravity();
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
            // prioritize the one that wasn't already active
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

        // Super-jump (flatten -> puff quickly while grounded)
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
            // normal jump disabled while puffed (puff-flap handled in input)
            return;
        }

        if (isFlattened)
        {
            // small hop while flattened
            if (jumpPressed && isGrounded && !jumpConsumed && flattenAllowsCrawl)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, miniHopSpeed);
                jumpConsumed = true;
            }
            return;
        }

        // Normal buffered jump
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
                if (currentDirection == RIGHT)
                    rb.linearVelocity = new Vector2(crawlSpeed, rb.linearVelocity.y);
                else if (currentDirection == LEFT)
                    rb.linearVelocity = new Vector2(-crawlSpeed, rb.linearVelocity.y);
                else
                    rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            }
            else
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y); // hold position horizontal
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

    // Puff horizontal motion: exponential-like convergence to target, decays when released
    private void ApplyPuffHorizontalMovement()
    {
        float inputX = moveInput.x;

        // Exponential-style LERP toward a target: choose target and use a dt-based lerp factor
        float target = Mathf.Clamp(inputX, -1f, 1f) * puffMaxSpeed;

        if (Mathf.Abs(inputX) > 0.1f)
        {
            // lerpFactor ~ 1 - exp(-accel * dt) produces exponential smoothing
            float lerpFactor = 1f - Mathf.Exp(-puffAcceleration * Time.fixedDeltaTime);
            puffVelX = Mathf.Lerp(puffVelX, target, lerpFactor);
        }
        else
        {
            // decay toward zero smoothly
            puffVelX = Mathf.MoveTowards(puffVelX, 0f, puffDecay * Time.fixedDeltaTime);
        }

        // Apply to rigidbody
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
            rb.gravityScale = originalGravityScale;
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
        // fallback to sprite orientation
        return sr.flipX ? Vector2.left : Vector2.right;
    }

    // External tuning used by gameplay
    public void UpdateMovementParams(float speedChange, float jumpTimeChange, float jumpSpeedChange)
    {
        moveSpeed += speedChange;
        jumpTime += jumpTimeChange;
        jumpSpeed -= jumpSpeedChange;
    }
}
