using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class Bo : MonoBehaviour
{
    Rigidbody2D rb;
    SpriteRenderer sr;
    public float moveSpeed = 9, jumpSpeed = 40;

    public float timer = 0.0f;
    public float jumpTime = 0.1f, deadTime = 1.0f;
    private bool dead = false, reload = false;

    Vector3 pos;

    public const string RIGHT = "right";
    public const string LEFT = "left";

    string buttonPressed;
    bool isGrounded = false, isJumping = false, moving = true;

    private int score = 0;
    public Text scoreText;

    // Input System
    private PlayerInputActions inputActions;
    private Vector2 moveInput;
    private bool jumpPressed;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        dead = false;

        // Initialize Input Actions
        try
        {
            inputActions = new PlayerInputActions();
            Debug.Log("Bo Awake: Input Actions initialized successfully");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Bo Awake: Failed to initialize Input Actions - {e.Message}");
        }
    }

    void OnEnable()
    {
        if (inputActions != null)
        {
            Debug.Log("Enabling Player Input");
            inputActions.Player.Enable();
            inputActions.Player.Move.performed += ctx => 
            {
                moveInput = ctx.ReadValue<Vector2>();
                Debug.Log($"Move performed: {moveInput}");
            };
            inputActions.Player.Move.canceled += ctx => 
            {
                moveInput = Vector2.zero;
                Debug.Log("Move canceled");
            };
            inputActions.Player.Jump.performed += ctx => 
            {
                jumpPressed = true;
                Debug.Log("Jump performed");
            };
            inputActions.Player.Jump.canceled += ctx => 
            {
                jumpPressed = false;
                Debug.Log("Jump canceled");
            };
        }
        else
        {
            Debug.LogError("OnEnable: inputActions is null");
        }
    }

    void OnDisable()
    {
        if (inputActions != null)
        {
            Debug.Log("Disabling Player Input");
            inputActions.Player.Disable();
        }
        else
        {
            Debug.LogWarning("OnDisable: inputActions is null, skipping Disable");
        }
    }

    void Update()
    {
        pos = transform.position;
        if (moving)
        {
            // Handle movement direction
            if (moveInput.x > 0.1f)
            {
                buttonPressed = RIGHT;
                sr.flipX = false;
            }
            else if (moveInput.x < -0.1f)
            {
                buttonPressed = LEFT;
                sr.flipX = true;
            }
            else
            {
                buttonPressed = null;
            }

            // Handle jump
            if (jumpPressed && isGrounded)
            {
                isJumping = true;
            }

            Debug.Log($"Update - Move: {moveInput}, Jump: {jumpPressed}");
        }
        else
        {
            rb.linearVelocity = Move(0, 0);
        }
    }

    private void FixedUpdate()
    {
        if (!isJumping)
        {
            if (buttonPressed == RIGHT)
            {
                rb.linearVelocity = Move(moveSpeed, rb.linearVelocity.y * 0.6f);
            }
            else if (buttonPressed == LEFT)
            {
                rb.linearVelocity = Move(-moveSpeed, rb.linearVelocity.y * 0.6f);
            }
            else
            {
                rb.linearVelocity = Move(rb.linearVelocity.x * 0.6f, rb.linearVelocity.y * 0.6f);
            }
        }
        else
        {
            isGrounded = false;
            fixRotation();
            Jump();
        }

        if (dead)
        {
            moving = false;
            reload = StartTimer(deadTime);
            if (reload)
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }
        }
    }

    void OnCollisionEnter2D(Collision2D col)
    {
        if (col.gameObject.tag == "Platform" || col.gameObject.tag == "Ground")
        {
            isGrounded = true;
            isJumping = false;
            ResetTimer();
        }

        if (col.gameObject.tag == "Collectible")
        {
            Destroy(col.gameObject);
            score++;
            scoreText.text = score + " ";
            moveSpeed += 0.3f;
            jumpTime += 0.01f;
            jumpSpeed -= 0.2f;
        }

        if (col.gameObject.tag == "DeadZone")
        {
            scoreText.text = "Try again";
            dead = true;
        }
    }

    void fixRotation()
    {
        this.transform.rotation = Quaternion.identity;
    }

    public Vector2 Move(float x, float y)
    {
        return new Vector2(x, y);
    }

    void Jump()
    {
        if (!isGrounded && isJumping)
        {
            if (buttonPressed == RIGHT)
            {
                rb.linearVelocity = Move(moveSpeed, jumpSpeed * 0.6f);
            }
            else if (buttonPressed == LEFT)
            {
                rb.linearVelocity = Move(-moveSpeed, jumpSpeed * 0.6f);
            }
            else
            {
                rb.linearVelocity = Move(0, jumpSpeed * 0.6f);
            }

            isJumping = !StartTimer(jumpTime);
        }
        else
        {
            ResetTimer();
        }
    }

    bool StartTimer(float limit)
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

    void ResetTimer()
    {
        timer = 0.0f;
    }
}