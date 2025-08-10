using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(BoMovement))]
public class BoGameplay : MonoBehaviour
{
    private BoMovement movement;

    [Header("UI")]
    public Text scoreText;

    private int score = 0;
    private bool isDead = false;

    [Header("Death Settings")]
    public float deadTime = 1.0f;
    private float deathTimer = 0.0f;

    [Header("Collectible Enhancement")]
    public float speedBoost = 0.3f;
    public float jumpTimeBoost = 0.01f;
    public float jumpSpeedReduction = 0.2f;

    void Awake()
    {
        movement = GetComponent<BoMovement>();
        UpdateScoreUI();
    }

    void OnEnable()
    {
        if (movement != null)
        {
            movement.OnGrounded += OnCharacterGrounded;
            movement.OnHitDeadZone += OnCharacterHitDeadZone;
        }
    }

    void OnDisable()
    {
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
            deathTimer += Time.deltaTime;
            if (deathTimer > deadTime)
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }
        }
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Collectible"))
        {
            Destroy(collision.gameObject);
            score++;
            UpdateScoreUI();
            movement.UpdateMovementParams(speedBoost, jumpTimeBoost, jumpSpeedReduction);
        }

        if (collision.gameObject.CompareTag("DeadZone"))
        {
            isDead = true;
            scoreText.text = "Try again";
        }
    }

    private void OnCharacterGrounded(Collision2D collision)
    {
        // Hook for landing VFX / SFX
    }

    private void OnCharacterHitDeadZone(Collision2D collision)
    {
        isDead = true;
        scoreText.text = "Try again";
    }

    private void UpdateScoreUI()
    {
        if (scoreText != null) scoreText.text = score.ToString() + " ";
    }
}
