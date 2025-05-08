using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Follow_bo : MonoBehaviour
{
    public GameObject player;

    Rigidbody2D rb;

    public float px, py, pvx, pvy, cx, cy;


    // Start is called before the first frame update
    void Start()
    {
        rb = player.GetComponent<Rigidbody2D>();
    }

    // Update is called once per frame
    void Update()
    {
        px = player.transform.position.x;
        py = player.transform.position.y;

        pvx = rb.linearVelocity.x;
        pvy = rb.linearVelocity.y;

        cx = transform.position.x;
        cy = transform.position.y;

        transform.position = new Vector3(px, py, -1);

        if (cx < px)
        {
            cx = cx * 1.2f;
        }

        if (cx > px)
        {
            cx = cx * 0.8f;
        }

        transform.position = new Vector3(cx, cy, -1);
            //player.transform.position + new Vector3((0*0.8f), (1 * 0.8f), -5);

        //transform.position 
    }
}
