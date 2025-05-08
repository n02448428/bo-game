using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine.UI;
using UnityEngine.SceneManagement;

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


    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        dead = false;
    }


    void Update()
    {
        pos = transform.position;
        if (moving)
        {
            if (Input.GetKey(KeyCode.RightArrow))
            {
                buttonPressed = RIGHT;
                sr.flipX = false;
            }
            else if (Input.GetKey(KeyCode.LeftArrow))
            {
                buttonPressed = LEFT;
                sr.flipX = true;
            }
            else
            {
                buttonPressed = null;
            }

            if (Input.GetKey(KeyCode.Space))
            {
                if (isGrounded)
                {
                    isJumping = true;
                }
            }

            if (Input.GetKeyUp(KeyCode.Space)) isJumping = false;
        }
        else rb.linearVelocity = Move(0, 0);
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
                SceneManager.LoadScene("SampleScene");
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
            //dead = true;
        }
    }

    void DeathSequence()
    {
        dead = StartTimer(deadTime);
        if (timer > 3.0f)
        {
            dead = true;
            ResetTimer();
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
            if (buttonPressed == LEFT)
            {
                rb.linearVelocity = Move(-moveSpeed, jumpSpeed * 0.6f);
            }
            if (buttonPressed == null)
            {
                rb.linearVelocity = Move(0, jumpSpeed * 0.6f);
            }

            isJumping = !StartTimer(jumpTime);
            
        }
        else ResetTimer();
    }

    bool StartTimer(float limit)
    {
        timer += Time.deltaTime;
        if (timer > limit)
        {
            ResetTimer();
            return true;
        }
        else return false;
    }

    void ResetTimer()
    {
        timer = 0.0f;
    }
}
