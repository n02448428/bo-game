using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(BoMovement))]
public class BoGameplay : MonoBehaviour
{
    // References
    private BoMovement movement;

    // UI
    [Header("UI Elements")]
    public Text scoreText;

    // Game state
    private int score = 0;
    private bool isDead = false;

    // Death handling
    [Header("Death Settings")]
    public float deadTime = 1.0f;
    private float deathTimer = 0.0f;
    private bool reloadQueued = false;

    // Enhancement parameters
    [Header("Collectible Enhancement")]
    public float speedBoost = 0.3f;
    public float jumpTimeBoost = 0.01f;
    public float jumpSpeedReduction = 0.2f;

    void Awake()
    {
        movement = GetComponent<BoMovement>();
        isDead = false;

        // Update UI
        UpdateScoreUI();
    }

    void OnEnable()
    {
        // Subscribe to movement events
        if (movement != null)
        {
            movement.OnGrounded += OnCharacterGrounded;
            movement.OnHitDeadZone += OnCharacterHitDeadZone;
        }
    }

    void OnDisable()
    {
        // Unsubscribe from movement events
        if (movement != null)
        {
            movement.OnGrounded -= OnCharacterGrounded;
            movement.OnHitDeadZone -= OnCharacterHitDeadZone;
        }
    }

    void Update()
    {
        if (isDead)
        {
            movement.CanMove = false;
            reloadQueued = HandleDeathTimer();

            if (reloadQueued)
            {
                SceneManager.LoadScene("SampleScene");
            }
        }
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        // Handle collectibles
        if (collision.gameObject.CompareTag("Collectible"))
        {
            Destroy(collision.gameObject);
            IncreaseScore();
            movement.UpdateMovementParams(speedBoost, jumpTimeBoost, jumpSpeedReduction);
        }

        // Handle death
        if (collision.gameObject.CompareTag("DeadZone"))
        {
            scoreText.text = "Try again";
            isDead = true;
        }
    }

    private void OnCharacterGrounded(Collision2D collision)
    {
        // Can perform any gameplay-specific logic when the character lands
        // For example: play landing sound, create dust particles, etc.
    }

    private void OnCharacterHitDeadZone(Collision2D collision)
    {
        scoreText.text = "Try again";
        isDead = true;
    }

    private void IncreaseScore()
    {
        score++;
        UpdateScoreUI();
    }

    private void UpdateScoreUI()
    {
        if (scoreText != null)
        {
            scoreText.text = score.ToString() + " ";
        }
    }

    private bool HandleDeathTimer()
    {
        deathTimer += Time.deltaTime;
        if (deathTimer > deadTime)
        {
            ResetDeathTimer();
            return true;
        }

        return false;
    }

    private void ResetDeathTimer()
    {
        deathTimer = 0.0f;
    }
}